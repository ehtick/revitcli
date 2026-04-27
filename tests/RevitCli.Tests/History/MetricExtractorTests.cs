using System.Collections.Generic;
using RevitCli.History;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.History;

public class MetricExtractorTests
{
    private static ModelSnapshot MakeSnapshot()
    {
        var snap = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-04-27T10:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", Document = "Sample.rvt", DocumentPath = "C:/p/Sample.rvt" },
            Summary = new SnapshotSummary
            {
                ElementCounts = new Dictionary<string, int>
                {
                    ["walls"] = 12,
                    ["doors"] = 7,
                    ["custom"] = 99,
                },
                SheetCount = 3,
                ScheduleCount = 2,
            },
        };
        snap.Categories["walls"] = new List<SnapshotElement>
        {
            new() { Id = 1, Name = "W1" }, new() { Id = 2, Name = "W2" },
        };
        snap.Categories["Doors"] = new List<SnapshotElement>
        {
            new() { Id = 11, Name = "D1" },
        };
        snap.Sheets.Add(new SnapshotSheet { Number = "A001", Name = "Cover" });
        snap.Sheets.Add(new SnapshotSheet { Number = "A002", Name = "Plan" });
        snap.Sheets.Add(new SnapshotSheet { Number = "A003", Name = "Detail" });
        snap.Schedules.Add(new SnapshotSchedule { Id = 100, Name = "Door schedule", RowCount = 5 });
        snap.Schedules.Add(new SnapshotSchedule { Id = 101, Name = "Window schedule", RowCount = 3 });
        return snap;
    }

    [Fact]
    public void Extract_Score_UsesProvidedLookup()
    {
        var snap = MakeSnapshot();
        var value = MetricExtractor.Extract(snap, "score", _ => 87.5);
        Assert.Equal(87.5, value);
    }

    [Fact]
    public void Extract_Score_NoLookupReturnsNull()
    {
        var snap = MakeSnapshot();
        var value = MetricExtractor.Extract(snap, "score");
        Assert.Null(value);
    }

    [Fact]
    public void Extract_Sheets_UsesSummaryThenList()
    {
        var snap = MakeSnapshot();
        Assert.Equal(3, MetricExtractor.Extract(snap, "sheets"));
    }

    [Fact]
    public void Extract_Sheets_FallsBackToListWhenSummaryZero()
    {
        var snap = MakeSnapshot();
        snap.Summary.SheetCount = 0;
        Assert.Equal(3, MetricExtractor.Extract(snap, "sheets"));
    }

    [Fact]
    public void Extract_Schedules_UsesSummary()
    {
        var snap = MakeSnapshot();
        Assert.Equal(2, MetricExtractor.Extract(snap, "schedules"));
    }

    [Fact]
    public void Extract_ElementsCategory_PrefersCategoriesList()
    {
        var snap = MakeSnapshot();
        Assert.Equal(2, MetricExtractor.Extract(snap, "elements.walls"));
    }

    [Fact]
    public void Extract_ElementsCategory_CaseInsensitive()
    {
        var snap = MakeSnapshot();
        Assert.Equal(1, MetricExtractor.Extract(snap, "elements.doors"));
    }

    [Fact]
    public void Extract_ElementsCategory_FallsBackToSummaryWhenNoList()
    {
        var snap = MakeSnapshot();
        snap.Categories.Clear();
        Assert.Equal(12, MetricExtractor.Extract(snap, "elements.walls"));
    }

    [Fact]
    public void Extract_CountKey_HitsElementCounts()
    {
        var snap = MakeSnapshot();
        Assert.Equal(99, MetricExtractor.Extract(snap, "count.custom"));
    }

    [Fact]
    public void Extract_CountKey_FallsBackToCategories()
    {
        var snap = MakeSnapshot();
        snap.Summary.ElementCounts.Clear();
        Assert.Equal(2, MetricExtractor.Extract(snap, "count.walls"));
    }

    [Fact]
    public void Extract_UnknownMetric_ReturnsNull()
    {
        var snap = MakeSnapshot();
        Assert.Null(MetricExtractor.Extract(snap, "totallymadeup"));
    }

    [Fact]
    public void Extract_EmptyMetric_ReturnsNull()
    {
        var snap = MakeSnapshot();
        Assert.Null(MetricExtractor.Extract(snap, ""));
    }

    [Fact]
    public void Extract_MissingCategory_ReturnsNull()
    {
        var snap = MakeSnapshot();
        Assert.Null(MetricExtractor.Extract(snap, "elements.windows"));
    }

    [Fact]
    public void Series_PreservesOrderAndMissingValues()
    {
        var present = MakeSnapshot();
        var blank = MakeSnapshot();
        blank.Categories.Clear();
        blank.Summary.ElementCounts.Clear();
        var values = MetricExtractor.Series(new[] { present, blank }, "elements.walls");
        Assert.Equal(2, values.Count);
        Assert.Equal(2, values[0]);
        Assert.Null(values[1]);
    }
}
