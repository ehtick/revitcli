using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Actions;
using EmbedIO.WebApi;
using RevitCli.Addin.Handlers;
using RevitCli.Shared;

namespace RevitCli.Addin.Server;

public class ApiServer : IDisposable
{
    private WebServer? _server;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly int _port;
    private readonly IRevitOperations _operations;
    private readonly string _revitVersion;
    private readonly string _serverInfoPath;
    private readonly string _serverInfoMutexName;
    private int _actualPort;
    private string _token = "";
    private static readonly TimeSpan ServerInfoMutexTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ServerStopTimeout = TimeSpan.FromSeconds(2);
    private const int ServerInfoPublishAttempts = 8;
    private static readonly TimeSpan ServerInfoPublishRetryDelay = TimeSpan.FromMilliseconds(50);

    private static readonly string DefaultServerInfoPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcli", "server.json");

    public ApiServer(int port, IRevitOperations operations, string revitVersion = "", string? serverInfoPath = null)
    {
        _port = port;
        _operations = operations;
        _revitVersion = revitVersion;
        _serverInfoPath = string.IsNullOrWhiteSpace(serverInfoPath)
            ? DefaultServerInfoPath
            : serverInfoPath;
        _serverInfoMutexName = CreateServerInfoMutexName(_serverInfoPath);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _token = Guid.NewGuid().ToString("N");

        // Try ports starting from _port, fallback to next 10
        int actualPort = _port;
        for (int i = 0; i <= 10; i++)
        {
            WebServer? server = null;
            Task? runTask = null;
            try
            {
                actualPort = _port + i;
                server = CreateServer(actualPort);
                runTask = server.RunAsync(_cts.Token);
                if (!WaitForListening(server, runTask, ServerStartTimeout))
                {
                    server.Dispose();
                    ObserveRunTask(runTask);
                    if (i == 10)
                        throw new InvalidOperationException($"RevitCli API server did not start listening on port {actualPort}.");
                    continue;
                }

                _server = server;
                _runTask = runTask;
                _actualPort = actualPort;
                WriteServerInfo(actualPort);
                return;
            }
            catch (Exception)
            {
                server?.Dispose();
                ObserveRunTask(runTask);
                _server = null;
                _runTask = null;
                if (i == 10) throw;
            }
        }
    }

    private static bool WaitForListening(WebServer server, Task runTask, TimeSpan timeout)
    {
        if (server.State == WebServerState.Listening)
            return true;

        using var changed = new ManualResetEventSlim(false);
        WebServerStateChangedEventHandler handler = (_, e) =>
        {
            if (e.NewState == WebServerState.Listening || e.NewState == WebServerState.Stopped)
                changed.Set();
        };

        server.StateChanged += handler;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (server.State == WebServerState.Listening)
                    return true;
                if (runTask.IsFaulted || runTask.IsCanceled || server.State == WebServerState.Stopped)
                    return false;

                var remaining = timeout - stopwatch.Elapsed;
                var wait = remaining < TimeSpan.FromMilliseconds(50)
                    ? remaining
                    : TimeSpan.FromMilliseconds(50);
                if (wait > TimeSpan.Zero)
                    changed.Wait(wait);
                changed.Reset();
            }

            return server.State == WebServerState.Listening;
        }
        finally
        {
            server.StateChanged -= handler;
        }
    }

    private WebServer CreateServer(int port)
    {
        var token = _token;
        return new WebServer(o => o
                .WithUrlPrefix($"http://localhost:{port}/")
                .WithMode(HttpListenerMode.EmbedIO))
            .WithModule(new TokenAuthModule(token))
            .WithWebApi("/api", m => m
                .WithController(() => new StatusController(_operations))
                .WithController(() => new ElementsController(_operations))
                .WithController(() => new ExportController(_operations))
                .WithController(() => new SetController(_operations))
                .WithController(() => new AuditController(_operations))
                .WithController(() => new ScheduleController(_operations))
                .WithController(() => new SnapshotController(_operations))
                .WithController(() => new FamiliesController(_operations)))
            .WithModule(new ActionModule("/", HttpVerbs.Any, ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            }));
    }

    private sealed class TokenAuthModule : EmbedIO.WebModuleBase
    {
        private readonly string _token;

        public TokenAuthModule(string token) : base("/")
        {
            _token = token;
        }

        public override bool IsFinalHandler => false;

        protected override Task OnRequestAsync(IHttpContext context)
        {
            var provided = context.Request.Headers["X-RevitCli-Token"];
            if (provided != _token)
                throw HttpException.Unauthorized();

            return Task.CompletedTask;
        }
    }

    private void WriteServerInfo(int port)
    {
        WithServerInfoLock(() =>
        {
            var info = new ServerInfo
            {
                Port = port,
                Pid = Process.GetCurrentProcess().Id,
                RevitVersion = _revitVersion,
                StartedAt = DateTime.UtcNow.ToString("o"),
                Token = _token
            };
            var dir = Path.GetDirectoryName(_serverInfoPath)!;
            Directory.CreateDirectory(dir);
            WriteServerInfoAtomically(dir,
                JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        }, throwOnTimeout: true);
    }

    private void WriteServerInfoAtomically(string dir, string json)
    {
        var fileName = Path.GetFileName(_serverInfoPath);
        var tempPath = Path.Combine(dir, $".{fileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json);
        try
        {
            IOException? lastError = null;
            for (var attempt = 1; attempt <= ServerInfoPublishAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(_serverInfoPath))
                        File.Replace(tempPath, _serverInfoPath, null);
                    else
                        File.Move(tempPath, _serverInfoPath);
                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    if (attempt == ServerInfoPublishAttempts)
                        break;

                    Thread.Sleep(ServerInfoPublishRetryDelay);
                }
            }

            throw lastError!;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }
        }
    }

    private void RemoveServerInfo()
    {
        WithServerInfoLock(() =>
        {
            ServerInfo? info;
            try
            {
                if (!File.Exists(_serverInfoPath))
                    return;

                info = JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(_serverInfoPath));
            }
            catch
            {
                return;
            }

            if (info?.Token != _token)
                return;

            var currentPid = Process.GetCurrentProcess().Id;
            if (info.Pid != currentPid && info.Port != _actualPort)
                return;

            try { File.Delete(_serverInfoPath); } catch { }
        }, throwOnTimeout: false);
    }

    private void WithServerInfoLock(Action action, bool throwOnTimeout)
    {
        Mutex? mutex = null;
        var acquired = false;
        try
        {
            mutex = new Mutex(false, _serverInfoMutexName);
            try
            {
                acquired = mutex.WaitOne(ServerInfoMutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                if (throwOnTimeout)
                    throw new TimeoutException($"Timed out waiting for server info lock: {_serverInfoPath}");
                return;
            }

            action();
        }
        catch when (!throwOnTimeout)
        {
            return;
        }
        finally
        {
            if (acquired && mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            }
            mutex?.Dispose();
        }
    }

    private static string CreateServerInfoMutexName(string serverInfoPath)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(serverInfoPath).ToUpperInvariant();
        }
        catch
        {
            normalizedPath = serverInfoPath.ToUpperInvariant();
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "");
        return $@"Local\RevitCli.ApiServer.{hash}";
    }

    public void Stop()
    {
        var runTask = _runTask;
        RemoveServerInfo();
        _cts?.Cancel();
        _server?.Dispose();
        ObserveRunTask(runTask);
        _server = null;
        _runTask = null;
        _cts = null;
    }

    private static void ObserveRunTask(Task? task)
    {
        if (task == null)
            return;

        try { task.Wait(ServerStopTimeout); } catch { }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            return;
        }

        if (!task.IsCompleted)
        {
            task.ContinueWith(t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
