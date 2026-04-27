using System.Linq;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Profile;

/// <summary>
/// Tests <see cref="ProfileValidator.Validate"/> over hand-built
/// <see cref="ProjectProfile"/> instances. We bypass the YAML loader so we can
/// inject conditions the loader would otherwise reject (e.g. an empty
/// auditRules rule name) and exercise the validator's own rules in isolation.
/// </summary>
public class ProfileValidatorTests
{
    [Fact]
    public void Validate_PrecheckReferencesUnknownCheck_ReturnsError()
    {
        var profile = new ProjectProfile();
        profile.Checks["default"] = new CheckDefinition { FailOn = "error" };
        profile.Publish["main"] = new PublishPipeline
        {
            Precheck = "missing-check",
            Presets = { "dwg" },
        };
        profile.Exports["dwg"] = new ExportPreset { Format = "dwg" };

        var issues = ProfileValidator.Validate(profile);

        var precheckIssue = Assert.Single(issues, i => i.Path == "publish.main.precheck");
        Assert.Equal(ProfileValidationSeverity.Error, precheckIssue.Severity);
        Assert.Contains("missing-check", precheckIssue.Message);
    }

    [Fact]
    public void Validate_PresetReferencesUnknownExport_ReturnsError()
    {
        var profile = new ProjectProfile();
        profile.Checks["default"] = new CheckDefinition { FailOn = "error" };
        profile.Publish["main"] = new PublishPipeline
        {
            Presets = { "ghost-preset" },
        };

        var issues = ProfileValidator.Validate(profile);

        var presetIssue = Assert.Single(issues, i => i.Path == "publish.main.presets[0]");
        Assert.Equal(ProfileValidationSeverity.Error, presetIssue.Severity);
        Assert.Contains("ghost-preset", presetIssue.Message);
    }

    [Fact]
    public void Validate_SeverityOverrideWithoutCategoryOrParameter_ReturnsWarning()
    {
        var profile = new ProjectProfile();
        var check = new CheckDefinition { FailOn = "error" };
        // The required-parameter entry has a valid severity but is missing the
        // category/parameter pair; the loader does not reject this (only
        // validates the severity literal), so the validator must catch it.
        check.RequiredParameters.Add(new RequiredParameterCheck
        {
            Severity = "warning",
            Category = "",
            Parameter = "",
        });
        profile.Checks["default"] = check;

        var issues = ProfileValidator.Validate(profile);

        var deadRule = Assert.Single(issues, i =>
            i.Path == "checks.default.requiredParameters[0]");
        Assert.Equal(ProfileValidationSeverity.Warning, deadRule.Severity);
        Assert.Contains("dead rule", deadRule.Message);
    }

    [Fact]
    public void Validate_CheckSetNotReferencedByPipeline_ReturnsInfoOrphan()
    {
        var profile = new ProjectProfile();
        // 'default' is implicit-referenced; 'unused' should surface as orphan info.
        profile.Checks["default"] = new CheckDefinition { FailOn = "error" };
        profile.Checks["unused"] = new CheckDefinition { FailOn = "error" };
        profile.Publish["main"] = new PublishPipeline { Precheck = "default", Presets = { "dwg" } };
        profile.Exports["dwg"] = new ExportPreset { Format = "dwg" };

        var issues = ProfileValidator.Validate(profile);

        var orphan = Assert.Single(issues, i => i.Path == "checks.unused");
        Assert.Equal(ProfileValidationSeverity.Info, orphan.Severity);
        Assert.Contains("orphan", orphan.Message);
    }

    [Fact]
    public void Validate_CleanProfile_ReturnsEmptyList()
    {
        var profile = new ProjectProfile();
        profile.Checks["default"] = new CheckDefinition { FailOn = "error" };
        profile.Exports["dwg"] = new ExportPreset { Format = "dwg" };
        profile.Publish["main"] = new PublishPipeline
        {
            Precheck = "default",
            Presets = { "dwg" },
        };

        var issues = ProfileValidator.Validate(profile);
        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_MultipleErrors_AggregatesAll()
    {
        var profile = new ProjectProfile();
        profile.Checks["default"] = new CheckDefinition { FailOn = "error" };
        profile.Publish["a"] = new PublishPipeline
        {
            Precheck = "ghost",
            Presets = { "alsoGhost" },
        };
        profile.Publish["b"] = new PublishPipeline
        {
            Precheck = "stillGhost",
            Presets = { "alsoGhost", "anotherGhost" },
        };

        var issues = ProfileValidator.Validate(profile);
        var errors = issues.Where(i => i.Severity == ProfileValidationSeverity.Error).ToList();
        // Two precheck errors + three preset errors = five errors at minimum.
        Assert.True(errors.Count >= 5,
            $"expected ≥5 aggregated errors, got {errors.Count}: {string.Join('|', errors.Select(e => e.Path))}");
    }

    [Fact]
    public void Validate_EmptyAuditRule_ReturnsWarning()
    {
        var profile = new ProjectProfile();
        var check = new CheckDefinition { FailOn = "error" };
        check.AuditRules.Add(new AuditRuleRef { Rule = "" });
        profile.Checks["default"] = check;

        var issues = ProfileValidator.Validate(profile);

        var warn = Assert.Single(issues, i =>
            i.Path == "checks.default.auditRules[0].rule");
        Assert.Equal(ProfileValidationSeverity.Warning, warn.Severity);
    }

    [Fact]
    public void Validate_EmptyPipelinePresets_ReturnsWarning()
    {
        var profile = new ProjectProfile();
        profile.Checks["default"] = new CheckDefinition { FailOn = "error" };
        profile.Publish["empty"] = new PublishPipeline { Precheck = "default" };

        var issues = ProfileValidator.Validate(profile);

        var warn = Assert.Single(issues, i => i.Path == "publish.empty.presets");
        Assert.Equal(ProfileValidationSeverity.Warning, warn.Severity);
        Assert.Contains("no presets", warn.Message);
    }
}
