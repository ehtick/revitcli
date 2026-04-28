using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RevitCli.Commands;
using RevitCli.History;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class ScoreCommandTests : IDisposable
{
    private readonly string _root;

    public ScoreCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-score-history-" + Guid.NewGuid().ToString("N"));
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

    private static ModelSnapshot MakeSnapshot(int wallCount, int sheetCount = 2, int scheduleCount = 1)
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
        for (var i = 0; i < sheetCount; i++)
        {
            snap.Sheets.Add(new SnapshotSheet
            {
                Number = $"A{i + 1:D3}",
                Name = $"Sheet {i + 1}",
            });
        }
        for (var i = 0; i < scheduleCount; i++)
        {
            snap.Schedules.Add(new SnapshotSchedule
            {
                Id = 100 + i,
                Name = $"Schedule {i + 1}",
                RowCount = 10,
            });
        }
        return snap;
    }

    [Fact]
    public async Task History_NoStore_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("7d", HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("not initialised", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task History_InvalidWindow_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("notaduration", HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("Invalid window", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task History_EmptyStore_ReportsNoSnapshots()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("7d", HistoryDir, writer);
        Assert.Equal(0, exit);
        Assert.Contains("No snapshots in window", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task History_SevenDailySnapshots_ReturnsSevenRows()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var now = DateTimeOffset.UtcNow;
        for (var d = 6; d >= 0; d--)
        {
            await store.AppendAsync(MakeSnapshot(2 + d), "manual",
                now - TimeSpan.FromDays(d) - TimeSpan.FromHours(1));
        }

        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("7d", HistoryDir, writer);
        Assert.Equal(0, exit);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
        // 1 header + 7 rows.
        Assert.True(lines.Count >= 8, $"Expected at least 8 lines, got {lines.Count}: {string.Join(" | ", lines)}");
        Assert.Contains("date", lines[0]);
        Assert.Contains("score", lines[0]);
        Assert.Contains("letter", lines[0]);
        Assert.Contains("new", lines[0]);
        Assert.Contains("resolved", lines[0]);
        Assert.Contains("unchanged", lines[0]);
    }

    [Fact]
    public async Task History_PartialDayCoverage_GroupsByDateAndKeepsLatest()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var todayMorning = DateTimeOffset.UtcNow.Date.AddHours(8);
        var todayAfternoon = DateTimeOffset.UtcNow.Date.AddHours(15);

        await store.AppendAsync(MakeSnapshot(2), "manual", new DateTimeOffset(todayMorning, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(5), "manual", new DateTimeOffset(todayAfternoon, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("7d", HistoryDir, writer);
        Assert.Equal(0, exit);

        var lines = writer.ToString().Split('\n')
            .Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        // header + exactly one row (one day).
        Assert.Equal(2, lines.Count);
        // Latest snapshot of the day has 5 walls; previous-snapshot baseline starts as unchanged=5.
        Assert.Contains("5", lines[1]);
    }

    [Fact]
    public async Task History_ComputesNewAndResolvedBetweenDays()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var dayOne = DateTimeOffset.UtcNow - TimeSpan.FromDays(2);
        var dayTwo = DateTimeOffset.UtcNow - TimeSpan.FromDays(1);

        // Day 1: walls 1..3
        var snapDay1 = MakeSnapshot(3);
        await store.AppendAsync(snapDay1, "manual", dayOne);

        // Day 2: walls 1, 2 only (id 3 removed) plus ids 4 and 5 added => new=2, resolved=1, unchanged=2
        var snapDay2 = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-04-27T10:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", Document = "Sample.rvt", DocumentPath = "C:/p/Sample.rvt" },
            Summary = new SnapshotSummary(),
        };
        snapDay2.Categories["walls"] = new List<SnapshotElement>
        {
            new() { Id = 1, Name = "W1" },
            new() { Id = 2, Name = "W2" },
            new() { Id = 4, Name = "W4" },
            new() { Id = 5, Name = "W5" },
        };
        snapDay2.Sheets.Add(new SnapshotSheet { Number = "A001", Name = "Plan" });
        snapDay2.Schedules.Add(new SnapshotSchedule { Id = 100, Name = "S1", RowCount = 5 });
        await store.AppendAsync(snapDay2, "manual", dayTwo);

        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("7d", HistoryDir, writer);
        Assert.Equal(0, exit);

        var text = writer.ToString();
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        // Header + 2 day rows.
        Assert.Equal(3, lines.Count);
        // The second row should encode new=2, resolved=1, unchanged=2 in some column order.
        var secondRow = lines[2];
        Assert.Matches(@"\b2\b", secondRow);
        Assert.Matches(@"\b1\b", secondRow);
    }

    [Fact]
    public async Task History_FixBaselinesAreHidden()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var now = DateTimeOffset.UtcNow;

        await store.AppendAsync(MakeSnapshot(2), "fix-baseline", now - TimeSpan.FromDays(1));
        await store.AppendAsync(MakeSnapshot(3), "manual", now - TimeSpan.FromHours(2));

        var writer = new StringWriter();
        var exit = await ScoreCommand.ExecuteHistoryAsync("7d", HistoryDir, writer);
        Assert.Equal(0, exit);

        var lines = writer.ToString().Split('\n')
            .Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        // header + 1 row (only the manual snapshot is included).
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void SnapshotScore_EmptySnapshot_DefaultsToHundred()
    {
        var snap = new ModelSnapshot
        {
            SchemaVersion = 1,
            Revit = new SnapshotRevit { Version = "2026", DocumentPath = "C:/p/Sample.rvt" },
        };
        Assert.Equal(100.0, ScoreCommand.SnapshotScore(snap));
    }

    [Fact]
    public void SnapshotScore_PartialSheetNumbers_PenalisesScore()
    {
        var snap = MakeSnapshot(0, sheetCount: 0, scheduleCount: 0);
        snap.Sheets.Add(new SnapshotSheet { Number = "A001", Name = "Cover" });
        snap.Sheets.Add(new SnapshotSheet { Number = "", Name = "Bad" });
        var score = ScoreCommand.SnapshotScore(snap);
        Assert.NotNull(score);
        Assert.True(score < 100, $"expected < 100 but got {score}");
        Assert.True(score >= 0);
    }

    [Fact]
    public void SnapshotScore_NullSnapshot_ReturnsNull()
    {
        Assert.Null(ScoreCommand.SnapshotScore(null!));
    }
}
