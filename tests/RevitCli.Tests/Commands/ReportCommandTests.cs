using System.Text.Json;
using RevitCli.Commands;
using RevitCli.History;
using RevitCli.Shared;

namespace RevitCli.Tests.Commands;

public sealed class ReportCommandTests : IDisposable
{
    private readonly string _root;
    private readonly DateTimeOffset _now = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    public ReportCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-report-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Weekly_Table_IncludesHistoryDiffAndJournalSummary()
    {
        await SeedHistoryAsync();
        SeedJournal();
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "table",
            reportPath: null,
            output,
            _now);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Weekly report", text);
        Assert.Contains("History snapshots: 2", text);
        Assert.Contains("Diff review: highest=routine, changes=1", text);
        Assert.Contains("Journal entries: 2; affected elements: 8", text);
        Assert.Contains("publish: 1", text);
        Assert.Contains("set: 1", text);
    }

    [Fact]
    public async Task Weekly_Json_IncludesStructuredSections()
    {
        await SeedHistoryAsync();
        SeedJournal();
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "json",
            reportPath: null,
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("snapshotCount").GetInt32());
        Assert.Equal(1, root.GetProperty("diffReview").GetProperty("totalChanges").GetInt32());
        Assert.Equal(2, root.GetProperty("journal").GetProperty("entryCount").GetInt32());
    }

    [Fact]
    public async Task Weekly_ReportPath_WritesMarkdown()
    {
        await SeedHistoryAsync();
        SeedJournal();
        var reportPath = Path.Combine(_root, ".revitcli", "reports", "weekly.md");
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "table",
            reportPath,
            output,
            _now);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath));
        var markdown = File.ReadAllText(reportPath);
        Assert.StartsWith("# Weekly RevitCli Report", markdown);
        Assert.Contains("## Diff Review", markdown);
        Assert.Contains("## Journal", markdown);
        Assert.Contains("Report saved to", output.ToString());
    }

    [Fact]
    public async Task Weekly_MissingHistory_ReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "table",
            reportPath: null,
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("history store not initialised", output.ToString());
    }

    private async Task SeedHistoryAsync()
    {
        var store = HistoryStore.ForProject(_root);
        await store.InitAsync();
        await store.AppendAsync(
            Snapshot((1, "A", "h1")),
            "weekly-health",
            _now.AddDays(-5));
        await store.AppendAsync(
            Snapshot((1, "A", "h1"), (2, "B", "h2")),
            "weekly-health",
            _now.AddDays(-1));
    }

    private void SeedJournal()
    {
        var dir = Path.Combine(_root, ".revitcli");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(
            Path.Combine(dir, "journal.jsonl"),
            new[]
            {
                $$"""{"timestamp":"{{_now.AddDays(-2).ToString("o")}}","action":"publish","category":"sheets","user":"alice","exported":3}""",
                $$"""{"timestamp":"{{_now.AddDays(-1).ToString("o")}}","action":"set","category":"walls","user":"bob","affected":5}""",
                $$"""{"timestamp":"{{_now.AddDays(-20).ToString("o")}}","action":"publish","category":"old","user":"alice","exported":99}""",
            });
    }

    private static ModelSnapshot Snapshot(params (long Id, string Mark, string Hash)[] walls)
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-05-05T00:00:00Z",
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Sample.rvt",
                DocumentPath = "C:/models/Sample.rvt",
            },
            Summary = new SnapshotSummary
            {
                ElementCounts = new Dictionary<string, int> { ["walls"] = walls.Length },
                SheetCount = 1,
                ScheduleCount = 1,
            },
            Sheets =
            {
                new SnapshotSheet
                {
                    Number = "A-101",
                    Name = "Plan",
                    ViewId = 100,
                    MetaHash = "sheet-hash",
                }
            },
            Schedules =
            {
                new SnapshotSchedule
                {
                    Id = 200,
                    Name = "Door Schedule",
                    Category = "doors",
                    RowCount = 1,
                    Hash = "schedule-hash",
                }
            },
        };

        snapshot.Categories["walls"] = walls
            .Select(item => new SnapshotElement
            {
                Id = item.Id,
                Name = $"W{item.Id}",
                Parameters = new Dictionary<string, string> { ["Mark"] = item.Mark },
                Hash = item.Hash,
            })
            .ToList();
        return snapshot;
    }
}
