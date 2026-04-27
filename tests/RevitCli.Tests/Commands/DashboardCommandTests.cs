using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// Unit + smoke coverage for the v2.0 phase 1 dashboard CLI commands.
///
/// We exercise the public test seams (<see cref="DashboardCommand.ExecuteBuildAsync"/>,
/// <see cref="DashboardCommand.ExecuteServeAsync"/>) and a handful of internal
/// helpers via <c>InternalsVisibleTo("RevitCli.Tests")</c>. The real
/// <c>dashboard/build</c> directory is intentionally not required: each test
/// stages its own minimal fake to keep the suite hermetic and CI-portable.
/// </summary>
public class DashboardCommandTests : IDisposable
{
    private readonly string _tempRoot;

    public DashboardCommandTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "revitcli-dashboard-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Tolerate the rare Windows file-handle race; the OS reaps temp eventually.
        }
    }

    // ------------------------------------------------------------------
    // build — preconditions
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_WhenBuildDirMissing_PrintsClearError_AndExitsOne()
    {
        var writer = new StringWriter();
        var missingBuild = Path.Combine(_tempRoot, "no-such-build");
        var output = Path.Combine(_tempRoot, "public");

        var exit = await DashboardCommand.ExecuteBuildAsync(
            outputDir: output,
            historyDir: Path.Combine(_tempRoot, "history"),
            buildDirOverride: missingBuild,
            force: false,
            output: writer);

        Assert.Equal(1, exit);
        var text = writer.ToString();
        Assert.Contains("Dashboard not built", text);
        Assert.Contains("npm install && npm run build", text);
        Assert.Contains(missingBuild, text);
        // Ensure no output dir was leaked when we bailed out early.
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Build_RefusesToOverwriteNonEmptyOutput_WithoutForce()
    {
        var build = StageFakeBuildDir();
        var output = Path.Combine(_tempRoot, "occupied");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "leftover.txt"), "do not delete");

        var writer = new StringWriter();
        var exit = await DashboardCommand.ExecuteBuildAsync(
            outputDir: output,
            historyDir: Path.Combine(_tempRoot, "history"),
            buildDirOverride: build,
            force: false,
            output: writer);

        Assert.Equal(1, exit);
        Assert.Contains("already exists and is not empty", writer.ToString());
        // Our leftover must survive — guard against accidental wipe.
        Assert.True(File.Exists(Path.Combine(output, "leftover.txt")));
    }

    // ------------------------------------------------------------------
    // build — happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_HappyPath_CopiesFiles_AndInjectsHistory()
    {
        var build = StageFakeBuildDir();
        var historyDir = Path.Combine(_tempRoot, "history");
        Directory.CreateDirectory(historyDir);
        var indexJson = "{\"version\":1,\"entries\":[{\"id\":\"a\",\"capturedAt\":\"2026-04-27T00:00:00Z\",\"source\":\"manual\"}]}";
        await File.WriteAllTextAsync(Path.Combine(historyDir, "index.json"), indexJson);

        var output = Path.Combine(_tempRoot, "public");
        var writer = new StringWriter();

        var exit = await DashboardCommand.ExecuteBuildAsync(
            outputDir: output,
            historyDir: historyDir,
            buildDirOverride: build,
            force: false,
            output: writer);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(output, "index.html")));
        Assert.True(File.Exists(Path.Combine(output, "assets", "app.js")));
        var copiedHistory = Path.Combine(output, "data", "history.json");
        Assert.True(File.Exists(copiedHistory));
        Assert.Equal(indexJson, await File.ReadAllTextAsync(copiedHistory));
        Assert.Contains("Dashboard built into", writer.ToString());
        Assert.Contains("history.json sourced from", writer.ToString());
    }

    [Fact]
    public async Task Build_WhenNoHistoryIndex_WritesEmptyPlaceholder_AndStillSucceeds()
    {
        var build = StageFakeBuildDir();
        var output = Path.Combine(_tempRoot, "public");
        var historyDir = Path.Combine(_tempRoot, "history-empty");
        // historyDir intentionally NOT created — exercise the placeholder branch.

        var writer = new StringWriter();
        var exit = await DashboardCommand.ExecuteBuildAsync(
            outputDir: output,
            historyDir: historyDir,
            buildDirOverride: build,
            force: false,
            output: writer);

        Assert.Equal(0, exit);
        var dataFile = Path.Combine(output, "data", "history.json");
        Assert.True(File.Exists(dataFile));
        var content = await File.ReadAllTextAsync(dataFile);
        Assert.Contains("\"entries\"", content);
        Assert.Contains("placeholder written", writer.ToString());
    }

    // ------------------------------------------------------------------
    // build — file-copy helper unit
    // ------------------------------------------------------------------

    [Fact]
    public void CopyDirectory_RecursivelyCopiesNestedFilesAndDirs()
    {
        var src = Path.Combine(_tempRoot, "src");
        var nested = Path.Combine(src, "nested", "deeper");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(src, "root.txt"), "root");
        File.WriteAllText(Path.Combine(nested, "leaf.bin"), "leaf");

        var dst = Path.Combine(_tempRoot, "dst");
        DashboardCommand.CopyDirectory(src, dst);

        Assert.Equal("root", File.ReadAllText(Path.Combine(dst, "root.txt")));
        Assert.Equal("leaf", File.ReadAllText(Path.Combine(dst, "nested", "deeper", "leaf.bin")));
    }

    [Fact]
    public void ResolveStaticFile_BlocksPathTraversal()
    {
        var build = StageFakeBuildDir();
        // Attempt to escape the build root via `..` segments.
        var resolved = DashboardCommand.ResolveStaticFile(build, "/../../etc/passwd");
        // The resolver must refuse, not silently rewrite to index.html.
        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveStaticFile_ReturnsIndexHtmlForUnknownClientRoute()
    {
        var build = StageFakeBuildDir();
        var resolved = DashboardCommand.ResolveStaticFile(build, "/history");
        Assert.NotNull(resolved);
        Assert.Equal(
            Path.Combine(build, "index.html"),
            resolved,
            ignoreCase: true);
    }

    [Fact]
    public void ResolveStaticFile_ServesRealAssetWhenPresent()
    {
        var build = StageFakeBuildDir();
        var resolved = DashboardCommand.ResolveStaticFile(build, "/assets/app.js");
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void GuessContentType_KnownExtensions_ReturnExpectedMime()
    {
        Assert.Equal("text/html; charset=utf-8", DashboardCommand.GuessContentType("a.html"));
        Assert.Equal("application/javascript; charset=utf-8", DashboardCommand.GuessContentType("a.js"));
        Assert.Equal("text/css; charset=utf-8", DashboardCommand.GuessContentType("a.css"));
        Assert.Equal("application/json; charset=utf-8", DashboardCommand.GuessContentType("a.json"));
        Assert.Equal("image/svg+xml", DashboardCommand.GuessContentType("a.svg"));
        Assert.Equal("application/octet-stream", DashboardCommand.GuessContentType("a.unknown"));
    }

    // ------------------------------------------------------------------
    // serve — preconditions
    // ------------------------------------------------------------------

    [Fact]
    public async Task Serve_WhenBuildDirMissing_PrintsClearError_AndExitsOne()
    {
        var writer = new StringWriter();
        var missingBuild = Path.Combine(_tempRoot, "no-such-build");

        var exit = await DashboardCommand.ExecuteServeAsync(
            port: DashboardCommand.FindFreePort(),
            historyDir: Path.Combine(_tempRoot, "history"),
            buildDirOverride: missingBuild,
            output: writer,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, exit);
        var text = writer.ToString();
        Assert.Contains("Dashboard not built", text);
        Assert.Contains("npm install && npm run build", text);
        Assert.Contains(missingBuild, text);
    }

    [Fact]
    public async Task Serve_RejectsOutOfRangePort()
    {
        var writer = new StringWriter();
        var exit = await DashboardCommand.ExecuteServeAsync(
            port: 70000,
            historyDir: _tempRoot,
            buildDirOverride: StageFakeBuildDir(),
            output: writer,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("outside the valid TCP range", writer.ToString());
    }

    // ------------------------------------------------------------------
    // serve — port-binding smoke (localhost only)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Serve_BindsToLocalhost_AndServesIndexHtml()
    {
        var build = StageFakeBuildDir();
        var port = DashboardCommand.FindFreePort();
        var writer = new StringWriter();

        using var cts = new CancellationTokenSource();
        var serveTask = DashboardCommand.ExecuteServeAsync(
            port: port,
            historyDir: Path.Combine(_tempRoot, "history"),
            buildDirOverride: build,
            output: writer,
            cancellationToken: cts.Token);

        // Spin until the listener is accepting (or fail after 2s).
        await WaitUntilListening(port, TimeSpan.FromSeconds(2));

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") };
            using var resp = await http.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("FAKE_DASHBOARD_INDEX", body);

            // External interface must be refused — the listener is bound to
            // localhost only. Probing 127.0.0.1 confirms the localhost loopback
            // handles the request, while a TCP connect to a non-loopback iface
            // would not (we don't try non-loopback in CI to avoid firewall flake).
            using var loopback = new TcpClient();
            await loopback.ConnectAsync(IPAddress.Loopback, port);
            Assert.True(loopback.Connected);
        }
        finally
        {
            cts.Cancel();
            await serveTask;
        }
    }

    [Fact]
    public async Task Serve_ServesHistoryJson_FromHistoryDir()
    {
        var build = StageFakeBuildDir();
        var historyDir = Path.Combine(_tempRoot, "history");
        Directory.CreateDirectory(historyDir);
        const string payload = "{\"version\":1,\"entries\":[{\"id\":\"x\"}]}";
        await File.WriteAllTextAsync(Path.Combine(historyDir, "index.json"), payload);

        var port = DashboardCommand.FindFreePort();
        var writer = new StringWriter();
        using var cts = new CancellationTokenSource();
        var serveTask = DashboardCommand.ExecuteServeAsync(
            port: port,
            historyDir: historyDir,
            buildDirOverride: build,
            output: writer,
            cancellationToken: cts.Token);

        await WaitUntilListening(port, TimeSpan.FromSeconds(2));

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") };
            using var resp = await http.GetAsync("/data/history.json");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("application/json; charset=utf-8", resp.Content.Headers.ContentType?.ToString());
            Assert.Equal(payload, await resp.Content.ReadAsStringAsync());
        }
        finally
        {
            cts.Cancel();
            await serveTask;
        }
    }

    [Fact]
    public void Create_RegistersBothSubcommands()
    {
        var dashboard = DashboardCommand.Create();
        Assert.Equal("dashboard", dashboard.Name);
        Assert.Contains(dashboard.Subcommands, c => c.Name == "serve");
        Assert.Contains(dashboard.Subcommands, c => c.Name == "build");
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Stage a minimal fake `dashboard/build` directory inside the per-test
    /// temp root. Files are tiny but realistic enough to exercise the copy /
    /// resolve / serve paths.
    /// </summary>
    private string StageFakeBuildDir()
    {
        var build = Path.Combine(_tempRoot, "dashboard-build");
        Directory.CreateDirectory(Path.Combine(build, "assets"));
        File.WriteAllText(
            Path.Combine(build, "index.html"),
            "<!doctype html><html><body>FAKE_DASHBOARD_INDEX</body></html>",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(build, "assets", "app.js"),
            "console.log('hi');",
            Encoding.UTF8);
        return build;
    }

    private static async Task WaitUntilListening(int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
                if (probe.Connected) return;
            }
            catch (SocketException)
            {
                await Task.Delay(25);
            }
        }
        throw new TimeoutException($"Listener on port {port} did not come up within {timeout.TotalSeconds:F1}s.");
    }
}
