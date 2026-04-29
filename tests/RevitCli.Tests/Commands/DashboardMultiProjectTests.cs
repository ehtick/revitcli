using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// Unit coverage for the v2.0 Multi-project additions to
/// <see cref="DashboardCommand"/> — the <c>--project NAME:DIR</c>
/// parser and the <c>InjectProjectsAsync</c> writer. Both helpers
/// are <c>internal</c>; we reach them via <c>InternalsVisibleTo</c>.
/// </summary>
public class DashboardMultiProjectTests : IDisposable
{
    private readonly string _tempRoot;

    public DashboardMultiProjectTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"revitcli-dashboard-mp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch (IOException) { }
    }

    // ─── ParseProjectSpecs ────────────────────────────────────────────────

    [Fact]
    public void Parse_SingleSpec_SplitsOnFirstColon()
    {
        var pairs = DashboardCommand.ParseProjectSpecs(new[] { "Office:./projA/.revitcli/history" });
        Assert.Single(pairs);
        Assert.Equal("Office", pairs[0].Name);
        Assert.Equal("./projA/.revitcli/history", pairs[0].Dir);
    }

    [Fact]
    public void Parse_WindowsPathWithDriveColon_PreservesPathAfterFirstColon()
    {
        // Windows paths like "C:\proj\hist" contain a colon. The parser
        // splits on the FIRST colon, which is the NAME:DIR separator —
        // the drive-letter colon survives intact.
        var pairs = DashboardCommand.ParseProjectSpecs(new[] { @"Office:C:\proj\hist" });
        Assert.Single(pairs);
        Assert.Equal("Office", pairs[0].Name);
        Assert.Equal(@"C:\proj\hist", pairs[0].Dir);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noseparator")]
    [InlineData(":no-name-just-dir")]
    [InlineData("name-no-dir:")]
    [InlineData("name-no-dir:   ")]
    public void Parse_MalformedSpec_ThrowsReadableException(string spec)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => DashboardCommand.ParseProjectSpecs(new[] { spec }));
        // Message must mention --project and the input so the operator
        // can grep the exact string back from a CI log.
        Assert.Contains("--project", ex.Message);
    }

    [Fact]
    public void Parse_DuplicateNamesCaseInsensitive_Throws()
    {
        // Project names index the resulting projects.json; collisions
        // would silently drop a project on the dashboard side. Refuse
        // upfront with a readable message.
        var ex = Assert.Throws<ArgumentException>(() =>
            DashboardCommand.ParseProjectSpecs(new[] { "Office:./a", "OFFICE:./b" }));
        Assert.Contains("duplicated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptyList_ReturnsEmpty()
    {
        Assert.Empty(DashboardCommand.ParseProjectSpecs(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_TrimsWhitespaceAroundNameAndDir()
    {
        var pairs = DashboardCommand.ParseProjectSpecs(new[] { "  Office  :  ./proj  " });
        Assert.Equal("Office", pairs[0].Name);
        Assert.Equal("./proj", pairs[0].Dir);
    }

    // ─── InjectProjectsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task Inject_RealIndexJson_PassesThroughEntries()
    {
        var historyDir = Path.Combine(_tempRoot, "projA", ".revitcli", "history");
        Directory.CreateDirectory(historyDir);
        var index = """
        {
          "version": 1,
          "entries": [
            { "id": "20260428T100000", "capturedAt": "2026-04-28T10:00:00Z", "score": 75 }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(historyDir, "index.json"), index);

        var target = Path.Combine(_tempRoot, "projects.json");
        var injected = await DashboardCommand.InjectProjectsAsync(
            new[] { ("ProjA", historyDir) }, target);

        Assert.Equal(1, injected); // 1 project had real history
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var projects = root.GetProperty("projects");
        Assert.Equal(1, projects.GetArrayLength());
        var first = projects[0];
        Assert.Equal("ProjA", first.GetProperty("name").GetString());
        var entries = first.GetProperty("history").GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal(75, entries[0].GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task Inject_MissingIndexJson_WritesEmptyHistoryPlaceholder()
    {
        // The /projects route's stub fallback handles missing
        // projects.json, but per-project missing index.json must NOT
        // crash the writer — placeholder { entries: [] } so the
        // dashboard renders a card with "0 captures".
        var historyDir = Path.Combine(_tempRoot, "ghost", ".revitcli", "history");
        // Don't create the directory — confirms the writer doesn't
        // require it to exist.

        var target = Path.Combine(_tempRoot, "projects.json");
        var injected = await DashboardCommand.InjectProjectsAsync(
            new[] { ("Ghost", historyDir) }, target);

        Assert.Equal(0, injected);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        var first = doc.RootElement.GetProperty("projects")[0];
        Assert.Equal("Ghost", first.GetProperty("name").GetString());
        var history = first.GetProperty("history");
        Assert.Equal(0, history.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Inject_MalformedIndexJson_FallsBackToPlaceholder()
    {
        // A corrupt index.json shouldn't blow up the build — that
        // would block deploys for an issue local to one project.
        // Treat as missing data: emit the placeholder, return
        // injected=0 for that one.
        var historyDir = Path.Combine(_tempRoot, "broken", ".revitcli", "history");
        Directory.CreateDirectory(historyDir);
        await File.WriteAllTextAsync(Path.Combine(historyDir, "index.json"), "{not json");

        var target = Path.Combine(_tempRoot, "projects.json");
        var injected = await DashboardCommand.InjectProjectsAsync(
            new[] { ("Broken", historyDir) }, target);

        Assert.Equal(0, injected);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        var entries = doc.RootElement.GetProperty("projects")[0]
            .GetProperty("history").GetProperty("entries");
        Assert.Equal(0, entries.GetArrayLength());
    }

    [Fact]
    public async Task Inject_MultipleProjects_PreservesOrderAndMixedRealVsPlaceholder()
    {
        var realDir = Path.Combine(_tempRoot, "real", ".revitcli", "history");
        Directory.CreateDirectory(realDir);
        await File.WriteAllTextAsync(Path.Combine(realDir, "index.json"),
            """{"version":1,"entries":[{"id":"a","capturedAt":"2026-04-28","score":80}]}""");
        var ghostDir = Path.Combine(_tempRoot, "ghost", ".revitcli", "history");

        var target = Path.Combine(_tempRoot, "projects.json");
        var injected = await DashboardCommand.InjectProjectsAsync(
            new[] { ("Real", realDir), ("Ghost", ghostDir) }, target);

        Assert.Equal(1, injected);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        var projects = doc.RootElement.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()).ToList();
        // Order matches the input list — operator-supplied ordering
        // sometimes carries semantic meaning (priority, section).
        Assert.Equal(new[] { "Real", "Ghost" }, projects);
    }

    [Fact]
    public async Task Inject_AbsoluteHistoryDirIsNotWrittenToDeployArtifact()
    {
        // projects.json is a static deploy artifact. Do not leak the
        // builder's local filesystem path into GitHub Pages output.
        var dir = Path.Combine(_tempRoot, "abs-test");
        Directory.CreateDirectory(dir);

        var target = Path.Combine(_tempRoot, "projects.json");
        await DashboardCommand.InjectProjectsAsync(new[] { ("X", dir) }, target);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        Assert.False(doc.RootElement.GetProperty("projects")[0].TryGetProperty("historyDir", out _));
    }

    [Fact]
    public async Task Inject_RelativeHistoryDirIsKeptAsDisplayLabel()
    {
        var target = Path.Combine(_tempRoot, "projects.json");
        await DashboardCommand.InjectProjectsAsync(new[] { ("X", @".revitcli\history") }, target);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        var historyDirOut = doc.RootElement.GetProperty("projects")[0]
            .GetProperty("historyDir").GetString();
        Assert.Equal(".revitcli/history", historyDirOut);
    }

    [Fact]
    public async Task Inject_OutputJsonIsIndentedForOperatorReadability()
    {
        var target = Path.Combine(_tempRoot, "projects.json");
        await DashboardCommand.InjectProjectsAsync(
            new[] { ("X", Path.Combine(_tempRoot, "no-such")) }, target);

        var raw = await File.ReadAllTextAsync(target);
        // Indented serializer leaves a newline + spaces between top-
        // level fields. Pinning this prevents a quiet regression to
        // a one-liner that breaks `git diff` reviews of the artifact.
        Assert.Contains('\n', raw);
        Assert.Contains("  ", raw);
    }
}
