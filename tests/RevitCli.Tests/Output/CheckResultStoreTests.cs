using System;
using System.Collections.Generic;
using System.IO;
using RevitCli.Output;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Output;

public class CheckResultStoreTests
{
    [Fact]
    public void Save_CreatesResultFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var issues = new List<AuditIssue>
        {
            new() { Rule = "naming", Severity = "info", Message = "Default name", ElementId = 100 }
        };

        CheckResultStore.Save("default", 1, 0, 0, issues, dir);

        var latestPath = Path.Combine(dir, ".revitcli", "results", "default-latest.json");
        Assert.True(File.Exists(latestPath));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Save_RotatesPreviousFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var issues1 = new List<AuditIssue>
        {
            new() { Rule = "naming", Severity = "info", Message = "Issue 1" }
        };
        var issues2 = new List<AuditIssue>
        {
            new() { Rule = "naming", Severity = "info", Message = "Issue 2" }
        };

        CheckResultStore.Save("default", 1, 0, 0, issues1, dir);
        CheckResultStore.Save("default", 1, 0, 0, issues2, dir);

        var latestPath = Path.Combine(dir, ".revitcli", "results", "default-latest.json");
        var previousPath = Path.Combine(dir, ".revitcli", "results", "default-previous.json");
        Assert.True(File.Exists(latestPath));
        Assert.True(File.Exists(previousPath));

        // Latest should contain Issue 2
        Assert.Contains("Issue 2", File.ReadAllText(latestPath));
        // Previous should contain Issue 1
        Assert.Contains("Issue 1", File.ReadAllText(previousPath));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ComputeDiff_DetectsNewAndResolved()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        // Save initial run
        var issues1 = new List<AuditIssue>
        {
            new() { Rule = "naming", Severity = "info", Message = "Old issue", ElementId = 100 },
            new() { Rule = "room-bounds", Severity = "error", Message = "Unchanged issue", ElementId = 200 }
        };
        CheckResultStore.Save("default", 1, 1, 0, issues1, dir);

        // Current run: one resolved, one new, one unchanged
        var currentIssues = new List<AuditIssue>
        {
            new() { Rule = "room-bounds", Severity = "error", Message = "Unchanged issue", ElementId = 200 },
            new() { Rule = "imported-dwg", Severity = "warning", Message = "New issue", ElementId = 300 }
        };

        var diff = CheckResultStore.ComputeDiffAgainstLatest("default", currentIssues, dir);
        Assert.NotNull(diff);
        Assert.Single(diff!.New);
        Assert.Single(diff.Resolved);
        Assert.Equal(1, diff.Unchanged);

        Assert.Contains(diff.New, i => i.Rule == "imported-dwg");
        Assert.Contains(diff.Resolved, i => i.Rule == "naming");

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ComputeDiff_NoPrevious_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var diff = CheckResultStore.ComputeDiffAgainstLatest("default", new List<AuditIssue>(), dir);
        Assert.Null(diff);

        Directory.Delete(dir, true);
    }
}
