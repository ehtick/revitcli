using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCli.Commands;

/// <summary>
/// `revitcli dashboard` command group (v2.0 phase 1).
///
/// Two subcommands:
/// <list type="bullet">
///   <item><c>serve</c> — boot a localhost-only static-file server pointed at
///         the prebuilt SvelteKit output in <c>dashboard/build/</c>.</item>
///   <item><c>build</c> — copy that prebuilt output to a deploy-ready
///         directory and inject the user's <c>.revitcli/history/index.json</c>
///         at <c>data/history.json</c>.</item>
/// </list>
///
/// Phase 1 is intentionally a "byte pipe": no SSR, no auth, no API. The C#
/// CLI just packages and serves the front-end files. All interactivity is
/// client-side Svelte. See <c>docs/roadmap-2026q2-q3.md</c> §7 for context
/// and the v2.0 follow-up phases (full Chart.js, History page, Multi-project
/// view).
///
/// We use <see cref="HttpListener"/> for the static server because it ships
/// with .NET 8 BCL on every platform — no new package reference, no Kestrel
/// bootstrap — and because the surface we need (GET / with index.html
/// fallback) is small enough that the few hundred lines below are easier to
/// reason about than wiring up an ASP.NET Core host just for that.
/// </summary>
public static class DashboardCommand
{
    /// <summary>Default name of the prebuilt SvelteKit output directory.</summary>
    public const string DefaultBuildDirName = "build";

    /// <summary>Default top-level dashboard directory (relative to repo root).</summary>
    public const string DefaultDashboardDirName = "dashboard";

    /// <summary>Default conventional history directory (matches <c>HistoryStore</c>).</summary>
    public const string DefaultHistoryRelativeDir = ".revitcli/history";

    /// <summary>Default port used by <c>dashboard serve</c> when no override is given.</summary>
    public const int DefaultPort = 8080;

    public static Command Create()
    {
        var dashboard = new Command(
            "dashboard",
            "Serve or package the RevitCli web dashboard (v2.0 — phase 1)");
        dashboard.AddCommand(CreateServeCommand());
        dashboard.AddCommand(CreateBuildCommand());
        return dashboard;
    }

    // ------------------------------------------------------------------
    // serve
    // ------------------------------------------------------------------

    private static Command CreateServeCommand()
    {
        var portOpt = new Option<int>(
            "--port",
            () => DefaultPort,
            "Port to bind on localhost (default: 8080)");
        var historyDirOpt = new Option<string>(
            "--history-dir",
            () => DefaultHistoryRelativeDir,
            "History directory exposed at /data/history.json (default: .revitcli/history)");
        var buildDirOpt = new Option<string?>(
            "--build-dir",
            "Override path to the prebuilt SvelteKit output (default: ./dashboard/build)");

        var serve = new Command(
            "serve",
            "Serve the prebuilt dashboard on http://localhost:<port>/")
        {
            portOpt,
            historyDirOpt,
            buildDirOpt
        };

        serve.SetHandler(async (int port, string historyDir, string? buildDir) =>
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Environment.ExitCode = await ExecuteServeAsync(
                port, historyDir, buildDir, Console.Out, cts.Token);
        }, portOpt, historyDirOpt, buildDirOpt);

        return serve;
    }

    /// <summary>
    /// Test seam for <c>dashboard serve</c>. Returns the exit code
    /// (0 on success, 1 on usage / precondition failures). When the
    /// cancellation token fires the listener stops gracefully and the call
    /// returns 0.
    /// </summary>
    public static async Task<int> ExecuteServeAsync(
        int port,
        string historyDir,
        string? buildDirOverride,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (port < 0 || port > 65535)
        {
            await output.WriteLineAsync(
                $"Error: --port {port} is outside the valid TCP range (0–65535).");
            return 1;
        }

        var buildDir = ResolveBuildDir(buildDirOverride);
        if (!Directory.Exists(buildDir))
        {
            await output.WriteLineAsync(
                "Error: Dashboard not built. Run 'cd dashboard && npm install && npm run build' first.");
            await output.WriteLineAsync($"  (looked for {buildDir})");
            return 1;
        }

        // Bind to localhost ONLY. This is intentional: the dashboard is
        // local-only data tooling. Operators who want LAN access can put
        // their own reverse proxy in front; we won't bake the foot-gun in.
        var prefix = $"http://127.0.0.1:{port}/";

        HttpListener listener;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            await output.WriteLineAsync(
                $"Error: failed to bind {prefix}: {ex.Message}");
            await output.WriteLineAsync(
                "  Try a different --port, or check that no other process is bound to it.");
            return 1;
        }

        await output.WriteLineAsync($"Dashboard serving from {buildDir}");
        await output.WriteLineAsync($"  -> {prefix} (localhost only)");
        await output.WriteLineAsync($"  history dir: {Path.GetFullPath(historyDir)}");
        await output.WriteLineAsync("Press Ctrl+C to stop.");

        // Cancellation handling: the listener.GetContextAsync() call doesn't
        // accept a token, so we register a callback that stops the listener
        // (which makes the pending call throw HttpListenerException, which we
        // swallow as the orderly-shutdown signal).
        using var registration = cancellationToken.Register(() =>
        {
            try { listener.Stop(); } catch { /* listener may already be disposed */ }
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // Fire-and-forget: each request is independent. We don't
                // await the per-request task because slow clients shouldn't
                // block the accept loop.
                _ = Task.Run(() => HandleRequestAsync(context, buildDir, historyDir));
            }
        }
        finally
        {
            try { listener.Stop(); } catch { /* idempotent shutdown */ }
            try { listener.Close(); } catch { /* idempotent shutdown */ }
        }

        await output.WriteLineAsync("Dashboard server stopped.");
        return 0;
    }

    private static async Task HandleRequestAsync(
        HttpListenerContext context,
        string buildDir,
        string historyDir)
    {
        try
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath ?? "/";

            // Special-case the conventional history endpoint: serve the
            // user's index.json directly, bypassing the static directory.
            if (string.Equals(path, "/data/history.json", StringComparison.OrdinalIgnoreCase))
            {
                await ServeHistoryAsync(context, historyDir);
                return;
            }

            var resolved = ResolveStaticFile(buildDir, path);
            if (resolved == null)
            {
                await WriteStatusAsync(context, 404, "Not Found");
                return;
            }

            await ServeFileAsync(context, resolved);
        }
        catch
        {
            // Best-effort: never let a single bad request kill the loop.
            try { await WriteStatusAsync(context, 500, "Internal Server Error"); }
            catch { /* swallow */ }
        }
        finally
        {
            try { context.Response.Close(); } catch { /* idempotent */ }
        }
    }

    private static async Task ServeHistoryAsync(HttpListenerContext context, string historyDir)
    {
        var indexPath = Path.Combine(historyDir, "index.json");
        if (!File.Exists(indexPath))
        {
            // Return an empty document so the dashboard falls back to its
            // built-in stub instead of surfacing a network error.
            const string empty = "{\"version\":1,\"entries\":[]}";
            await WriteBodyAsync(context, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(empty));
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(indexPath);
        }
        catch (IOException)
        {
            await WriteStatusAsync(context, 503, "history file unavailable");
            return;
        }

        await WriteBodyAsync(context, 200, "application/json; charset=utf-8", bytes);
    }

    private static async Task ServeFileAsync(HttpListenerContext context, string filePath)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (IOException)
        {
            await WriteStatusAsync(context, 503, "file unavailable");
            return;
        }

        var contentType = GuessContentType(filePath);
        await WriteBodyAsync(context, 200, contentType, bytes);
    }

    /// <summary>
    /// Resolve a request path to a file inside <paramref name="buildDir"/>.
    /// Falls back to <c>index.html</c> for SPA client-side routes (any path
    /// that doesn't map to a real file with an extension). Returns
    /// <c>null</c> only when even <c>index.html</c> is missing.
    ///
    /// Path traversal is blocked: we reject any resolved path that escapes
    /// the build directory.
    /// </summary>
    internal static string? ResolveStaticFile(string buildDir, string urlPath)
    {
        var rooted = Path.GetFullPath(buildDir);
        var indexHtml = Path.Combine(rooted, "index.html");

        var trimmed = (urlPath ?? "/").TrimStart('/');
        if (trimmed.Length == 0)
        {
            return File.Exists(indexHtml) ? indexHtml : null;
        }

        // Decode URL escapes (e.g. %20). Anything that fails to decode is
        // treated as a 404 — we don't want to serve weird bytes from the FS.
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(trimmed);
        }
        catch (UriFormatException)
        {
            return null;
        }

        // Path.Combine + GetFullPath collapses `..` segments. We then verify
        // the result still sits inside the build directory.
        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(rooted, decoded));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        var rootedWithSep = rooted.EndsWith(Path.DirectorySeparatorChar)
            ? rooted
            : rooted + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootedWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, rooted, StringComparison.OrdinalIgnoreCase))
        {
            // Traversal attempt — refuse.
            return null;
        }

        if (File.Exists(combined)) return combined;

        // SPA fallback: any request without a file extension becomes
        // index.html so client-side routing works (`/history`, `/projects`).
        if (!Path.HasExtension(combined) && File.Exists(indexHtml))
        {
            return indexHtml;
        }

        return null;
    }

    internal static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".map" => "application/json; charset=utf-8",
            ".txt" or ".md" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static Task WriteStatusAsync(HttpListenerContext context, int status, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return WriteBodyAsync(context, status, "text/plain; charset=utf-8", bytes);
    }

    private static async Task WriteBodyAsync(
        HttpListenerContext context, int status, string contentType, byte[] body)
    {
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = body.LongLength;
        await response.OutputStream.WriteAsync(body, 0, body.Length);
        await response.OutputStream.FlushAsync();
    }

    // ------------------------------------------------------------------
    // build
    // ------------------------------------------------------------------

    private static Command CreateBuildCommand()
    {
        var outputOpt = new Option<string>(
            "--output",
            () => "./public",
            "Directory to write the deployable static site (default: ./public)");
        var historyDirOpt = new Option<string>(
            "--history-dir",
            () => DefaultHistoryRelativeDir,
            "History directory whose index.json will be inlined (default: .revitcli/history)");
        var projectOpt = new Option<string[]>(
            "--project",
            description:
                "Multi-project entry, repeatable. Format: \"NAME:DIR\" where DIR is the " +
                "path to a `.revitcli/history/` folder. Example: " +
                "--project \"Office:./projA/.revitcli/history\". The dashboard's /projects " +
                "page reads the resulting data/projects.json and renders comparative cards. " +
                "Orthogonal to --history-dir; pass both for single-project AND multi-project " +
                "views in the same build.")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var buildDirOpt = new Option<string?>(
            "--build-dir",
            "Override path to the prebuilt SvelteKit output (default: ./dashboard/build)");
        var forceOpt = new Option<bool>(
            "--force",
            () => false,
            "Allow overwriting --output even when it already exists with files");

        var build = new Command(
            "build",
            "Copy the prebuilt dashboard + inject history into a deploy-ready folder")
        {
            outputOpt,
            historyDirOpt,
            projectOpt,
            buildDirOpt,
            forceOpt
        };

        build.SetHandler(async (string output, string historyDir, string[] projects, string? buildDir, bool force) =>
        {
            Environment.ExitCode = await ExecuteBuildAsync(
                output, historyDir, projects, buildDir, force, Console.Out);
        }, outputOpt, historyDirOpt, projectOpt, buildDirOpt, forceOpt);

        return build;
    }

    /// <summary>
    /// Test seam for <c>dashboard build</c>. Returns 0 on success, 1 on any
    /// precondition or copy failure. This method is intentionally pure I/O —
    /// no Console writes — so tests can capture output via the supplied
    /// <see cref="TextWriter"/>.
    /// </summary>
    /// <summary>
    /// Backwards-compatible overload: preserves the v2.0 phase-1 contract
    /// (no <c>--project</c> support) so existing callers and tests keep
    /// compiling. Forwards to the multi-project-aware overload with an
    /// empty project list.
    /// </summary>
    public static Task<int> ExecuteBuildAsync(
        string outputDir,
        string historyDir,
        string? buildDirOverride,
        bool force,
        TextWriter output)
        => ExecuteBuildAsync(outputDir, historyDir, Array.Empty<string>(), buildDirOverride, force, output);

    public static async Task<int> ExecuteBuildAsync(
        string outputDir,
        string historyDir,
        string[] projects,
        string? buildDirOverride,
        bool force,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            await output.WriteLineAsync("Error: --output is required.");
            return 1;
        }

        var buildDir = ResolveBuildDir(buildDirOverride);
        if (!Directory.Exists(buildDir))
        {
            await output.WriteLineAsync(
                "Error: Dashboard not built. Run 'cd dashboard && npm install && npm run build' first.");
            await output.WriteLineAsync($"  (looked for {buildDir})");
            return 1;
        }

        var resolvedOutput = Path.GetFullPath(outputDir);

        // Refuse to overwrite an existing populated directory unless --force.
        // This protects against `--output .` mishaps wiping the repo.
        if (Directory.Exists(resolvedOutput) && !force && DirectoryHasContent(resolvedOutput))
        {
            await output.WriteLineAsync(
                $"Error: --output '{resolvedOutput}' already exists and is not empty. Pass --force to overwrite.");
            return 1;
        }

        try
        {
            CopyDirectory(buildDir, resolvedOutput);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: failed to copy dashboard build: {ex.Message}");
            return 1;
        }

        var dataDir = Path.Combine(resolvedOutput, "data");
        try
        {
            Directory.CreateDirectory(dataDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: failed to create data directory: {ex.Message}");
            return 1;
        }

        var historyIndex = Path.Combine(historyDir, "index.json");
        var dataHistory = Path.Combine(dataDir, "history.json");

        bool injectedRealHistory;
        try
        {
            injectedRealHistory = await InjectHistoryAsync(historyIndex, dataHistory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: failed to inject history: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Dashboard built into {resolvedOutput}");
        if (injectedRealHistory)
        {
            await output.WriteLineAsync($"  history.json sourced from {Path.GetFullPath(historyIndex)}");
        }
        else
        {
            await output.WriteLineAsync(
                $"  history.json placeholder written (no index.json at {Path.GetFullPath(historyIndex)})");
        }

        // Multi-project: parse all --project specs upfront, fail fast on
        // any malformed entry, then inject a single projects.json. The
        // /projects route reads it; absence is fine — the route falls
        // back to a "no projects" empty state.
        if (projects is { Length: > 0 })
        {
            List<(string Name, string Dir)> specs;
            try
            {
                specs = ParseProjectSpecs(projects);
            }
            catch (ArgumentException ex)
            {
                await output.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }

            var projectsTarget = Path.Combine(dataDir, "projects.json");
            int injectedReal;
            try
            {
                injectedReal = await InjectProjectsAsync(specs, projectsTarget);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await output.WriteLineAsync($"Error: failed to inject projects: {ex.Message}");
                return 1;
            }

            await output.WriteLineAsync(
                $"  projects.json wrote {specs.Count} project(s) ({injectedReal} with real history, " +
                $"{specs.Count - injectedReal} placeholder).");
        }

        await output.WriteLineAsync(
            "Deploy by uploading the directory to GitHub Pages, S3, or any static host.");
        return 0;
    }

    /// <summary>
    /// Parse a list of <c>NAME:DIR</c> spec strings into typed pairs.
    /// Splits on the FIRST colon so Windows paths (e.g.
    /// <c>"Proj:C:\\proj\\hist"</c>) survive the split intact. Throws
    /// <see cref="ArgumentException"/> with a readable message on any
    /// malformed entry — caller surfaces it as a user-facing diagnostic.
    /// Names must be unique within the list (case-insensitive) so the
    /// resulting projects.json doesn't carry conflicting entries.
    /// </summary>
    internal static List<(string Name, string Dir)> ParseProjectSpecs(IEnumerable<string> specs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, string)>();
        foreach (var raw in specs ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("--project spec cannot be empty.");

            var idx = raw.IndexOf(':');
            if (idx <= 0)
                throw new ArgumentException(
                    $"--project '{raw}' must be in NAME:DIR form (got no name before ':').");

            var name = raw.Substring(0, idx).Trim();
            var dir = raw.Substring(idx + 1).Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(
                    $"--project '{raw}' has an empty name. Use NAME:DIR.");
            if (string.IsNullOrEmpty(dir))
                throw new ArgumentException(
                    $"--project '{raw}' has an empty directory. Use NAME:DIR.");
            if (!seen.Add(name))
                throw new ArgumentException(
                    $"--project name '{name}' is duplicated. Project names must be unique.");

            result.Add((name, dir));
        }
        return result;
    }

    /// <summary>
    /// Build <c>data/projects.json</c> from N <c>(name, history-dir)</c>
    /// pairs. For each pair, reads the directory's <c>index.json</c>; if
    /// absent, writes a placeholder empty entry so the dashboard's
    /// <c>/projects</c> route still renders a card (with "no captures").
    /// Returns the count of projects whose index.json was actually
    /// loaded — used by the caller to print a summary line.
    /// </summary>
    internal static async Task<int> InjectProjectsAsync(
        IReadOnlyList<(string Name, string Dir)> specs, string targetFile)
    {
        var projects = new List<JsonObject>(specs.Count);
        var injectedReal = 0;

        foreach (var (name, dir) in specs)
        {
            var indexPath = Path.Combine(dir, "index.json");
            JsonNode? historyNode = null;
            if (File.Exists(indexPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(indexPath);
                    historyNode = JsonNode.Parse(json);
                    injectedReal++;
                }
                catch (System.Text.Json.JsonException)
                {
                    historyNode = null;
                }
            }

            historyNode ??= new JsonObject
            {
                ["version"] = 1,
                ["entries"] = new JsonArray(),
            };

            var project = new JsonObject
            {
                ["name"] = name,
                ["history"] = historyNode,
            };
            var historyDirLabel = ToDashboardHistoryDirLabel(dir);
            if (!string.IsNullOrWhiteSpace(historyDirLabel))
                project["historyDir"] = historyDirLabel;

            projects.Add(project);
        }

        var doc = new JsonObject
        {
            ["version"] = 1,
            ["projects"] = new JsonArray(projects.ToArray<JsonNode?>()),
        };

        // Indent for readability — these files are small and operators
        // sometimes hand-inspect them in PR review of dashboard deploys.
        var serializerOptions = new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerOptions.Default)
        {
            WriteIndented = true,
        };
        await File.WriteAllTextAsync(targetFile, doc.ToJsonString(serializerOptions), Encoding.UTF8);

        return injectedReal;
    }

    private static string? ToDashboardHistoryDirLabel(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return null;

        if (Path.IsPathRooted(dir))
            return null;

        return dir.Replace('\\', '/');
    }

    /// <summary>
    /// Inject the user's <c>index.json</c> at <paramref name="targetFile"/>.
    /// When the source is missing, writes a minimal empty document so the
    /// dashboard's loader resolves cleanly (and falls back to its stub).
    /// Returns <c>true</c> when real data was copied, <c>false</c> when the
    /// placeholder was written.
    /// </summary>
    internal static async Task<bool> InjectHistoryAsync(string sourceFile, string targetFile)
    {
        if (File.Exists(sourceFile))
        {
            using var src = File.OpenRead(sourceFile);
            using var dst = File.Create(targetFile);
            await src.CopyToAsync(dst);
            return true;
        }

        const string emptyDoc = "{\"version\":1,\"entries\":[]}";
        await File.WriteAllTextAsync(targetFile, emptyDoc, Encoding.UTF8);
        return false;
    }

    /// <summary>
    /// Recursive directory copy. Public for tests so the file-copy helper
    /// can be exercised without relying on a real <c>dashboard/build</c>.
    /// </summary>
    internal static void CopyDirectory(string sourceDir, string destinationDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir))
            throw new ArgumentException("sourceDir is required", nameof(sourceDir));
        if (string.IsNullOrWhiteSpace(destinationDir))
            throw new ArgumentException("destinationDir is required", nameof(destinationDir));

        var src = new DirectoryInfo(sourceDir);
        if (!src.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {src.FullName}");

        Directory.CreateDirectory(destinationDir);

        foreach (var file in src.GetFiles())
        {
            var target = Path.Combine(destinationDir, file.Name);
            file.CopyTo(target, overwrite: true);
        }

        foreach (var dir in src.GetDirectories())
        {
            var nested = Path.Combine(destinationDir, dir.Name);
            CopyDirectory(dir.FullName, nested);
        }
    }

    internal static bool DirectoryHasContent(string dir)
    {
        if (!Directory.Exists(dir)) return false;
        using var entries = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
        return entries.MoveNext();
    }

    /// <summary>
    /// Resolve the SvelteKit build directory. Honours an explicit override,
    /// otherwise looks for <c>./dashboard/build</c> relative to the current
    /// working directory.
    /// </summary>
    internal static string ResolveBuildDir(string? overrideDir)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return Path.GetFullPath(overrideDir);
        }

        return Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), DefaultDashboardDirName, DefaultBuildDirName));
    }

    /// <summary>
    /// Internal probe: try to bind a TCP listener on the requested port to
    /// detect whether it's free. Returns the actual bound port (useful when
    /// callers pass 0 for "any free port"). Closes the probe listener
    /// immediately. Test-only — not used by production code.
    /// </summary>
    internal static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
