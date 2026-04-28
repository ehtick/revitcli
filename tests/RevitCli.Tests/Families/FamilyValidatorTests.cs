using System.Collections.Generic;
using System.Linq;
using RevitCli.Families;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Families;

/// <summary>
/// Unit tests for the local <see cref="FamilyValidator"/>. Each test
/// constructs a single <see cref="FamilyInfo"/> and asserts which rules
/// fire — the goal is to pin the BUILT-IN invariants so a future PR
/// adding profile-driven rules doesn't accidentally regress them.
/// </summary>
public class FamilyValidatorTests
{
    [Fact]
    public void CleanFamily_ProducesNoIssues()
    {
        var families = new[] { Make(name: "MyDoor", category: "Doors", loadable: true, inPlace: false) };
        Assert.Empty(FamilyValidator.Validate(families));
    }

    [Fact]
    public void EmptyName_FlaggedAsError()
    {
        var families = new[] { Make(name: "", category: "Doors", loadable: true, inPlace: false) };
        var issues = FamilyValidator.Validate(families);
        var issue = Assert.Single(issues, i => i.Rule == "name-non-empty");
        Assert.Equal("error", issue.Severity);
    }

    [Theory]
    [InlineData("My/Door")]
    [InlineData("Door:Type")]
    [InlineData("My*Wild?Card")]
    [InlineData("My<Door>Tag")]
    [InlineData("Pipe|Run")]
    public void NameWithPathChars_FlaggedAsError(string name)
    {
        var families = new[] { Make(name: name, category: "Generic Models", loadable: true, inPlace: false) };
        var issues = FamilyValidator.Validate(families);
        var issue = Assert.Single(issues, i => i.Rule == "name-no-path-chars");
        Assert.Equal("error", issue.Severity);
        Assert.Equal(name, issue.FamilyName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<none>")]
    [InlineData("Unknown")]
    [InlineData("(none)")]
    public void PlaceholderCategory_FlaggedAsWarning(string category)
    {
        var families = new[] { Make(name: "Foo", category: category, loadable: true, inPlace: false) };
        var issues = FamilyValidator.Validate(families);
        var issue = Assert.Single(issues, i => i.Rule == "category-known");
        Assert.Equal("warning", issue.Severity);
    }

    [Fact]
    public void LoadableAndInPlaceTogether_FlaggedAsError()
    {
        // Both true is impossible per Revit's data model. If the addin
        // returns it, something's wrong upstream — error severity so CI
        // fails on it by default.
        var families = new[] { Make(name: "Weird", category: "Walls", loadable: true, inPlace: true) };
        var issues = FamilyValidator.Validate(families);
        var issue = Assert.Single(issues, i => i.Rule == "loadable-or-in-place");
        Assert.Equal("error", issue.Severity);
    }

    [Fact]
    public void NeitherLoadableNorInPlace_FlaggedAsWarning()
    {
        var families = new[] { Make(name: "Orphan", category: "Walls", loadable: false, inPlace: false) };
        var issues = FamilyValidator.Validate(families);
        var issue = Assert.Single(issues, i => i.Rule == "loadable-or-in-place");
        Assert.Equal("warning", issue.Severity);
    }

    [Fact]
    public void EnabledRulesFilter_OnlyRunsListedRules()
    {
        // Family violates BOTH "name-no-path-chars" and "category-known",
        // but the test only enables the first one — the second must NOT
        // be reported.
        var families = new[] { Make(name: "Bad/Name", category: "Unknown", loadable: true, inPlace: false) };
        var issues = FamilyValidator.Validate(families, new[] { "name-no-path-chars" });
        Assert.Single(issues);
        Assert.Equal("name-no-path-chars", issues[0].Rule);
    }

    [Fact]
    public void EnabledRules_CaseInsensitive()
    {
        var families = new[] { Make(name: "", category: "Walls", loadable: true, inPlace: false) };
        var issues = FamilyValidator.Validate(families, new[] { "Name-Non-Empty" });
        Assert.Single(issues);
        Assert.Equal("name-non-empty", issues[0].Rule);
    }

    [Fact]
    public void NullEnabledRules_RunsAllRules()
    {
        var families = new[] { Make(name: "Bad/Name", category: "Unknown", loadable: false, inPlace: false) };
        var issues = FamilyValidator.Validate(families, enabledRules: null);
        // 3 distinct rules fire (name-no-path-chars, category-known, loadable-or-in-place)
        Assert.Equal(3, issues.Select(i => i.Rule).Distinct().Count());
    }

    [Fact]
    public void AllRuleIds_ContainsExpectedRules()
    {
        // Pinned so adding a new rule requires updating the test —
        // forces deliberate consideration.
        var expected = new[]
        {
            "name-non-empty",
            "name-no-path-chars",
            "category-known",
            "loadable-or-in-place",
        };
        Assert.Equal(expected, FamilyValidator.AllRuleIds);
    }

    private static FamilyInfo Make(string name, string category, bool loadable, bool inPlace, long id = 1)
        => new()
        {
            Id = id,
            Name = name,
            Category = category,
            IsLoadable = loadable,
            IsInPlace = inPlace,
            IsPlaced = false,
        };
}
