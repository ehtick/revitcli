using System.Linq;
using System.Text.Json;
using RevitCli.Output;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Output;

public class DiffReviewRendererTests
{
    private static SnapshotDiff SampleDiff()
    {
        var diff = new SnapshotDiff { SchemaVersion = 1, From = "snap-mon.json", To = "snap-fri.json" };
        diff.Categories["walls"] = new CategoryDiff
        {
            Removed = new() { new RemovedItem { Id = 5, Key = "walls:W5", Name = "W5" } },
            Modified = new()
            {
                new ModifiedItem
                {
                    Id = 1,
                    Key = "walls:W1",
                    Changed = new() { ["Comments"] = new ParamChange { From = "old", To = "new" } },
                    OldHash = "h1",
                    NewHash = "h2"
                }
            }
        };
        diff.Categories["doors"] = new CategoryDiff
        {
            Modified = new()
            {
                new ModifiedItem
                {
                    Id = 20,
                    Key = "doors:D20",
                    Changed = new() { ["Fire Rating"] = new ParamChange { From = "90min", To = "60min" } },
                    OldHash = "d1",
                    NewHash = "d2"
                }
            }
        };
        diff.Categories["rooms"] = new CategoryDiff
        {
            Modified = new()
            {
                new ModifiedItem
                {
                    Id = 100,
                    Key = "rooms:Office",
                    Changed = new() { ["Number"] = new ParamChange { From = "101", To = "" } },
                    OldHash = "r1",
                    NewHash = "r2"
                }
            }
        };
        diff.Sheets.Modified.Add(new ModifiedItem
        {
            Id = 200,
            Key = "sheet:A-101",
            OldHash = "s1",
            NewHash = "s2"
        });
        return diff;
    }

    [Fact]
    public void Build_GroupsSuspiciousChangesBySeverity()
    {
        var report = DiffReviewRenderer.Build(SampleDiff());

        Assert.Equal("anomaly", report.HighestSeverity);
        Assert.Equal(5, report.TotalChanges);
        Assert.Contains(report.Groups, group => group.Severity == "anomaly" && group.ChangeType == "removed");
        Assert.Contains(report.Groups, group => group.Severity == "anomaly" && group.ChangeType == "lost-values");
        Assert.Contains(report.Groups, group => group.Severity == "notable" && group.Parameter == "Fire Rating");
        Assert.Contains(report.Groups, group => group.Severity == "routine" && group.Parameter == "Comments");
        Assert.Contains(report.RecommendedActions, action => action.Contains("blank rooms.Number"));
    }

    [Fact]
    public void RenderTable_PrintsReviewSummaryAndActions()
    {
        var output = DiffReviewRenderer.Render(DiffReviewRenderer.Build(SampleDiff()), "table", maxRows: 20);

        Assert.Contains("Review: 5 changes", output);
        Assert.Contains("Highest severity: anomaly", output);
        Assert.Contains("Anomaly", output);
        Assert.Contains("Recommended actions", output);
        Assert.Contains("Fire Rating", output);
    }

    [Fact]
    public void RenderJson_EmitsGroupsForAgents()
    {
        var output = DiffReviewRenderer.Render(DiffReviewRenderer.Build(SampleDiff()), "json", maxRows: 20);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;

        Assert.Equal("anomaly", root.GetProperty("highestSeverity").GetString());
        Assert.Equal(5, root.GetProperty("totalChanges").GetInt32());
        var groups = root.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Contains(groups, group => group.GetProperty("parameter").GetString() == "Fire Rating");
    }

    [Fact]
    public void RenderMarkdown_UsesReviewHeader()
    {
        var output = DiffReviewRenderer.Render(DiffReviewRenderer.Build(SampleDiff()), "markdown", maxRows: 20);

        Assert.StartsWith("## Diff review", output);
        Assert.Contains("### Anomaly", output);
        Assert.Contains("### Notable", output);
    }
}
