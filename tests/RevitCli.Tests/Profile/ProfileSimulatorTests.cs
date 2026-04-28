using System.Collections.Generic;
using System.Linq;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Profile;

/// <summary>
/// Unit coverage for <see cref="ProfileSimulator.SimulatePipeline"/>.
/// We construct in-memory <see cref="ProjectProfile"/> values rather
/// than parsing YAML — the loader has its own coverage and the
/// simulator's contract is "given a typed profile, what do you say
/// about pipeline N?".
/// </summary>
public class ProfileSimulatorTests
{
    private static ProjectProfile MinimalProfileWithPipeline(string pipelineName)
    {
        var p = new ProjectProfile();
        p.Exports["dwg"] = new ExportPreset
        {
            Format = "dwg",
            Sheets = new List<string> { "A101", "A102" },
        };
        p.Checks["default"] = new CheckDefinition
        {
            FailOn = "error",
            AuditRules = new List<AuditRuleRef>
            {
                new() { Rule = "naming" },
                new() { Rule = "room-bounds" },
            },
        };
        p.Publish[pipelineName] = new PublishPipeline
        {
            Presets = new List<string> { "dwg" },
            Precheck = "default",
        };
        return p;
    }

    [Fact]
    public void Simulate_KnownPipeline_NoFindings_ReportsCleanly()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");

        Assert.Equal("client-A", report.Name);
        Assert.Equal("default", report.Precheck);
        Assert.Equal(2, report.PrecheckRules.Count);
        Assert.Single(report.Presets);
        Assert.Equal("dwg", report.Presets[0].Format);
        Assert.Equal(new[] { "A101", "A102" }, report.Presets[0].Sheets);
        Assert.False(report.Presets[0].ExportsAllSheets);
        // Worst severity stays at Info when nothing fired — that's the
        // signal the CLI exit code uses.
        Assert.Equal(ProfileSimulator.Severity.Info, report.WorstSeverity);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Simulate_UnknownPipelineName_ThrowsWithAvailableList()
    {
        var profile = MinimalProfileWithPipeline("only");
        var ex = Assert.Throws<System.ArgumentException>(() =>
            ProfileSimulator.SimulatePipeline(profile, "missing"));
        Assert.Contains("Pipeline 'missing'", ex.Message);
        Assert.Contains("only", ex.Message); // available pipelines listed
    }

    [Fact]
    public void Simulate_MissingPreset_FlaggedAsError()
    {
        // The pipeline names a preset that the profile doesn't define.
        // Running this via PublishCommand would crash mid-pipeline; the
        // simulator surfaces the same diagnostic upfront.
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Publish["client-A"].Presets.Add("ghost-preset");

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        var error = Assert.Single(report.Findings, f => f.Code == "preset-missing");
        Assert.Equal(ProfileSimulator.Severity.Error, error.Severity);
        Assert.Contains("ghost-preset", error.Message);
        Assert.Equal(ProfileSimulator.Severity.Error, report.WorstSeverity);
    }

    [Fact]
    public void Simulate_MissingPrecheck_FlaggedAsError()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Publish["client-A"].Precheck = "no-such-check";

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        var error = Assert.Single(report.Findings, f => f.Code == "precheck-missing");
        Assert.Equal(ProfileSimulator.Severity.Error, error.Severity);
        Assert.Contains("no-such-check", error.Message);
    }

    [Fact]
    public void Simulate_AllSheets_FlaggedAsInfo()
    {
        // 'sheets: ALL' is legal but full-document exports take minutes —
        // worth surfacing so the operator confirms intent. Info, not
        // Warning, because in a deliberate "publish everything" pipeline
        // it's correct.
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Exports["dwg"].Sheets = new List<string> { "ALL" };

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.True(report.Presets[0].ExportsAllSheets);
        Assert.Single(report.Findings, f => f.Code == "preset-all-sheets");
    }

    [Fact]
    public void Simulate_UnknownFormat_FlaggedAsWarning()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Exports["dwg"].Format = "tiff"; // not dwg|pdf|ifc

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        var warning = Assert.Single(report.Findings, f => f.Code == "preset-unknown-format");
        Assert.Equal(ProfileSimulator.Severity.Warning, warning.Severity);
    }

    [Fact]
    public void Simulate_MissingFormat_FlaggedAsError()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Exports["dwg"].Format = "";

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Single(report.Findings, f => f.Code == "preset-missing-format");
        Assert.Equal(ProfileSimulator.Severity.Error, report.WorstSeverity);
    }

    [Fact]
    public void Simulate_EmptySelectors_FlaggedAsWarning()
    {
        // Preset with no sheets AND no views is silently a no-op at
        // runtime — PublishCommand skips it. Surface so the operator
        // knows something they typed isn't doing anything.
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Exports["dwg"].Sheets = null;
        profile.Exports["dwg"].Views = null;

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Single(report.Findings, f => f.Code == "preset-empty-selectors");
    }

    [Fact]
    public void Simulate_PrecheckEmpty_FlaggedAsWarning()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Checks["default"].AuditRules = new List<AuditRuleRef>();
        profile.Checks["default"].RequiredParameters.Clear();
        profile.Checks["default"].Naming.Clear();

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Single(report.Findings, f => f.Code == "precheck-empty");
    }

    [Fact]
    public void Simulate_AllRuleSourcesAggregated_IntoPrecheckRules()
    {
        // CheckDefinition has THREE rule sources (audit / required-
        // parameter / naming). The simulator surfaces all three so the
        // report shows what the precheck actually exercises, not just
        // the literal `auditRules:` list the operator wrote.
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Checks["default"].RequiredParameters.Add(
            new RequiredParameterCheck { Category = "doors", Parameter = "Mark" });
        profile.Checks["default"].Naming.Add(
            new NamingCheck { Target = "rooms", Pattern = "^R-" });

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Contains(report.PrecheckRules, r => r == "naming"); // auditRules
        Assert.Contains(report.PrecheckRules, r => r == "required-parameter:doors.Mark");
        Assert.Contains(report.PrecheckRules, r => r == "naming:rooms");
    }

    [Fact]
    public void Simulate_NoPrecheckConfigured_ReportsNullPrecheck()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Publish["client-A"].Precheck = null;

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Null(report.Precheck);
        Assert.Empty(report.PrecheckRules);
        // No "precheck-missing" finding fires when the field is genuinely null —
        // only when it's set to a name that doesn't resolve.
        Assert.DoesNotContain(report.Findings, f => f.Code == "precheck-missing");
    }

    [Fact]
    public void Simulate_WebhookSurfaceIsPropagated()
    {
        // Defaults.Notify becomes the report's WebhookUrl so operators
        // see in the simulation what would fire on real publish.
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Defaults.Notify = "https://hooks.example.com/revitcli";

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Equal("https://hooks.example.com/revitcli", report.WebhookUrl);
    }

    [Fact]
    public void Simulate_IncrementalPipeline_PropagatesSettings()
    {
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Publish["client-A"].Incremental = true;
        profile.Publish["client-A"].BaselinePath = ".revitcli/last-publish.json";
        profile.Publish["client-A"].SinceMode = "meta";

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.True(report.Incremental);
        Assert.Equal(".revitcli/last-publish.json", report.BaselinePath);
        Assert.Equal("meta", report.SinceMode);
    }

    [Fact]
    public void Simulate_MultipleFindings_WorstSeverityIsErrorWhenAnyErrorPresent()
    {
        // Sanity: WorstSeverity is the max across findings, used by the
        // CLI exit-code decision. An Error trumps any number of
        // Warning/Info entries.
        var profile = MinimalProfileWithPipeline("client-A");
        profile.Publish["client-A"].Presets.Add("ghost"); // -> Error
        profile.Exports["dwg"].Sheets = new List<string> { "ALL" }; // -> Info

        var report = ProfileSimulator.SimulatePipeline(profile, "client-A");
        Assert.Contains(report.Findings, f => f.Severity == ProfileSimulator.Severity.Error);
        Assert.Contains(report.Findings, f => f.Severity == ProfileSimulator.Severity.Info);
        Assert.Equal(ProfileSimulator.Severity.Error, report.WorstSeverity);
    }
}
