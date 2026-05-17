using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RevitCli.Commands;
using RevitCli.History;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.History;

public class HistoryDiffTests : IDisposable
{
    private readonly string _root;

    public HistoryDiffTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-history-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string HistoryDir => Path.Combine(_root, "history");

    private static ModelSnapshot MakeSnapshot(int wallCount, int doorCount = 0)
    {
        var snap = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-04-27T10:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", Document = "Sample.rvt", DocumentPath = "C:/p/Sample.rvt" },
            Summary = new SnapshotSummary(),
        };
        snap.Categories["walls"] = new List<SnapshotElement>();
        for (var i = 0; i < wallCount; i++)
        {
            snap.Categories["walls"].Add(new SnapshotElement { Id = i + 1, Name = $"W{i + 1}" });
        }
        snap.Categories["doors"] = new List<SnapshotElement>();
        for (var i = 0; i < doorCount; i++)
        {
            snap.Categories["doors"].Add(new SnapshotElement { Id = 1000 + i + 1, Name = $"D{i + 1}" });
        }
        return snap;
    }

    [Fact]
    public async Task Diff_InvalidReference_ReturnsOne()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "not-a-ref!", "@-1", "table", 20, null, false, HistoryDir, writer);

        Assert.Equal(1, exit);
        Assert.Contains("Invalid history reference", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_NoStore_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "table", 20, null, false, HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("not initialised", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_RefDoesNotResolve_ReturnsOne()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        // Only one snapshot exists; @-2 cannot resolve.
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "table", 20, null, false, HistoryDir, writer);

        Assert.Equal(1, exit);
        Assert.Contains("no snapshot matches reference '@-2'", writer.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_Table_ShowsCategoryDelta()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(4), "manual",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "table", 20, null, false, HistoryDir, writer);

        Assert.Equal(0, exit);
        var text = writer.ToString();
        Assert.Contains("walls", text);
        // 2 added walls (id 3 and id 4).
        Assert.Contains("+2", text);
    }

    [Fact]
    public async Task Diff_Json_EmitsValidJson()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(3), "manual",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "json", 20, null, false, HistoryDir, writer);

        Assert.Equal(0, exit);
        var text = writer.ToString();
        Assert.Contains("\"schemaVersion\"", text);
        Assert.Contains("\"summary\"", text);
        // Round-trip parse to confirm valid JSON.
        var json = System.Text.Json.JsonDocument.Parse(text);
        Assert.NotNull(json);
    }

    [Fact]
    public async Task Diff_Markdown_ContainsSectionHeadings()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(3), "manual",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "markdown", 20, null, false, HistoryDir, writer);

        Assert.Equal(0, exit);
        var text = writer.ToString();
        Assert.Contains("## Model changes", text);
        Assert.Contains("**walls**", text);
    }

    [Fact]
    public async Task Diff_CategoriesFilter_LimitsOutput()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2, 1), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(3, 2), "manual",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "table", 20, "walls", false, HistoryDir, writer);

        Assert.Equal(0, exit);
        var text = writer.ToString();
        Assert.Contains("walls", text);
        Assert.DoesNotContain("doors", text);
    }

    [Fact]
    public async Task Diff_Review_PrintsRuleBasedSummary()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(2), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(1), "manual",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "table", 20, null, false, HistoryDir, writer, review: true);

        Assert.Equal(0, exit);
        var text = writer.ToString();
        Assert.Contains("Highest severity: anomaly", text);
        Assert.Contains("walls: 1 removed", text);
    }

    [Fact]
    public async Task Diff_MissingMaxRows_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            "@-2", "@-1", "table", 0, null, false, HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("--max-rows", writer.ToString());
    }

    [Fact]
    public async Task Diff_EmptyRefs_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteDiffAsync(
            " ", "@-1", "table", 20, null, false, HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("required", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
