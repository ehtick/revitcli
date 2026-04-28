using System;
using System.IO;
using System.Linq;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Profile;

/// <summary>
/// Verifies the v1.9 deep-merge semantics: opt-in only, replaces remain the
/// default, dictionary entries combine on collision (child fields win, list
/// fields concatenate), and existing replace-mode profiles are unaffected.
/// </summary>
public class ProfileResolverDeepMergeTests : IDisposable
{
    private readonly string _root;

    public ProfileResolverDeepMergeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-deep-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, body);
        return path;
    }

    [Fact]
    public void Replace_Default_ChildOverridesNamedEntryWholesale()
    {
        // Replace mode is the historical behaviour. A named entry on the child
        // wholly replaces the parent's same-named entry; any rule the parent
        // defined under that entry is gone.
        Write("base.yml", @"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
      - rule: room-bounds
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
checks:
  default:
    failOn: warning
    auditRules:
      - rule: naming
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("warning", profile.Checks["default"].FailOn);
        Assert.Single(profile.Checks["default"].AuditRules);
        Assert.Equal("naming", profile.Checks["default"].AuditRules[0].Rule);
    }

    [Fact]
    public void DeepMerge_AddsMissingKeysWithoutOverwriting()
    {
        // Child only sets failOn; parent's auditRules must survive.
        Write("base.yml", @"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
extendsStrategy: deep-merge
checks:
  default:
    failOn: warning
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("warning", profile.Checks["default"].FailOn);
        // Parent's audit rule is inherited.
        Assert.Single(profile.Checks["default"].AuditRules);
        Assert.Equal("naming", profile.Checks["default"].AuditRules[0].Rule);
    }

    [Fact]
    public void DeepMerge_ListFields_ConcatenateParentThenChild()
    {
        Write("base.yml", @"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
extendsStrategy: deep-merge
checks:
  default:
    auditRules:
      - rule: room-bounds
");

        var profile = ProfileLoader.Load(childPath);
        var rules = profile.Checks["default"].AuditRules.Select(r => r.Rule).ToList();
        Assert.Equal(new[] { "naming", "room-bounds" }, rules);
    }

    [Fact]
    public void DeepMerge_ChildScalarWinsOnConflict()
    {
        Write("base.yml", @"
version: 1
publish:
  main:
    precheck: parent-check
    presets: [dwg]
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
extendsStrategy: deep-merge
publish:
  main:
    precheck: child-check
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("child-check", profile.Publish["main"].Precheck);
        // Presets came from the parent and are kept (deep-merge concatenates).
        Assert.Contains("dwg", profile.Publish["main"].Presets);
    }

    [Fact]
    public void DeepMerge_NestedDefaults_FieldLevelMerge()
    {
        // Defaults always uses field-level merge regardless of strategy; this
        // pins the behaviour so a future strategy refactor cannot regress it.
        Write("base.yml", @"
version: 1
defaults:
  outputDir: ./parent-out
  notify: https://parent
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
extendsStrategy: deep-merge
defaults:
  outputDir: ./child-out
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("./child-out", profile.Defaults.OutputDir);
        Assert.Equal("https://parent", profile.Defaults.Notify);
    }

    [Fact]
    public void DeepMerge_OnlyAppliesWhenChildOptsIn_ReplaceParentStrategyIgnored()
    {
        // Parent has no strategy — defaults to replace. Child does not opt in
        // → behaviour stays replace, even if a sibling parent does opt in.
        Write("base.yml", @"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
checks:
  default:
    auditRules:
      - rule: room-bounds
");

        var profile = ProfileLoader.Load(childPath);
        // Replace semantics: child's auditRules wholly replaces parent's, so
        // the merged list is just [room-bounds].
        Assert.Single(profile.Checks["default"].AuditRules);
        Assert.Equal("room-bounds", profile.Checks["default"].AuditRules[0].Rule);
    }

    [Fact]
    public void DeepMerge_AddsParentOnlyEntries()
    {
        Write("base.yml", @"
version: 1
checks:
  parent-only:
    failOn: error
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
extendsStrategy: deep-merge
checks:
  child-only:
    failOn: warning
");

        var profile = ProfileLoader.Load(childPath);
        Assert.True(profile.Checks.ContainsKey("parent-only"));
        Assert.True(profile.Checks.ContainsKey("child-only"));
    }

    [Fact]
    public void DeepMerge_PreservesParentExports()
    {
        Write("base.yml", @"
version: 1
exports:
  dwg:
    format: dwg
    sheets: [A101, A102]
");
        var childPath = Write("child.yml", @"
version: 1
extends: base.yml
extendsStrategy: deep-merge
exports:
  dwg:
    sheets: [A201]
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("dwg", profile.Exports["dwg"].Format);
        // Lists on ExportPreset are child-wins (override-style); the parent's
        // sheet list is replaced by the child's narrower set, matching the
        // intent that an override layer redefines what to export.
        Assert.Equal(new[] { "A201" }, profile.Exports["dwg"].Sheets);
    }
}
