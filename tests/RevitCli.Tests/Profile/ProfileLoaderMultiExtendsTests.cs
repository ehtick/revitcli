using System;
using System.IO;
using System.Linq;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Profile;

/// <summary>
/// Validates the v1.9 multi-extends loader: array form, DAG cycle detection,
/// missing parents, strategy pass-through. Existing single-string and
/// single-parent tests live in CheckCommandTests / ProfileResolverTests and
/// must not be regressed by this expansion.
/// </summary>
public class ProfileLoaderMultiExtendsTests : IDisposable
{
    private readonly string _root;

    public ProfileLoaderMultiExtendsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-multi-extends-" + Guid.NewGuid().ToString("N"));
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
    public void SingleStringExtends_PopulatesExtendsListWithOneEntry()
    {
        Write("base.yml", "version: 1\nchecks:\n  default:\n    failOn: error\n");
        var childPath = Write("child.yml", "version: 1\nextends: base.yml\nchecks:\n  default:\n    failOn: warning\n");

        var profile = ProfileLoader.Load(childPath);

        // Loader normalizes to ExtendsList; legacy Extends getter returns the
        // first entry so callers that still read profile.Extends keep working.
        Assert.Equal("warning", profile.Checks["default"].FailOn);
    }

    [Fact]
    public void SingleStringExtends_ProducesIdenticalResultToOriginalImplementation()
    {
        // The single-parent path must remain byte-identical to v1.0–v1.8 — this
        // test pins the exact merged shape so a regression is caught even if
        // the new code paths are added beneath it.
        Write("parent.yml", @"
version: 1
defaults:
  outputDir: ./parent-output
  notify: https://parent
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
exports:
  dwg:
    format: dwg
");
        var childPath = Write("child.yml", @"
version: 1
extends: parent.yml
defaults:
  outputDir: ./child-output
checks:
  child-only:
    failOn: warning
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("./child-output", profile.Defaults.OutputDir);
        Assert.Equal("https://parent", profile.Defaults.Notify);
        Assert.True(profile.Checks.ContainsKey("default"));
        Assert.True(profile.Checks.ContainsKey("child-only"));
        Assert.Single(profile.Checks["default"].AuditRules);
        Assert.True(profile.Exports.ContainsKey("dwg"));
    }

    [Fact]
    public void ArrayExtends_TwoParents_LaterParentOverridesEarlier()
    {
        Write("base.yml", @"
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
");
        Write("team.yml", @"
version: 1
checks:
  default:
    failOn: warning
");
        var childPath = Write("child.yml", @"
version: 1
extends:
  - base.yml
  - team.yml
");

        var profile = ProfileLoader.Load(childPath);

        // team.yml's failOn=warning overrides base.yml's failOn=error because
        // team is right of base in the extends list.
        Assert.Equal("warning", profile.Checks["default"].FailOn);
        // base.yml's exports survive because team.yml does not redefine them.
        Assert.True(profile.Exports.ContainsKey("dwg"));
    }

    [Fact]
    public void ArrayExtends_ChildOverridesAllParents()
    {
        Write("a.yml", "version: 1\nchecks:\n  default:\n    failOn: error\n");
        Write("b.yml", "version: 1\nchecks:\n  default:\n    failOn: error\n");
        var childPath = Write("c.yml", @"
version: 1
extends: [a.yml, b.yml]
checks:
  default:
    failOn: warning
");

        var profile = ProfileLoader.Load(childPath);
        Assert.Equal("warning", profile.Checks["default"].FailOn);
    }

    [Fact]
    public void ArrayExtends_DagCycle_Throws()
    {
        // a -> [b]; b -> [c]; c -> [a]. The depth-first walk must catch the
        // loop at the back-edge from c to a and surface a clear error.
        Write("a.yml", "version: 1\nextends: [b.yml]\n");
        Write("b.yml", "version: 1\nextends: [c.yml]\n");
        Write("c.yml", "version: 1\nextends: [a.yml]\n");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ProfileLoader.Load(Path.Combine(_root, "a.yml")));
        Assert.Contains("Circular profile inheritance", ex.Message);
    }

    [Fact]
    public void ArrayExtends_DiamondInheritance_AllowedNoCycle()
    {
        // a -> [b, c]; b -> [d]; c -> [d]. Diamond is legal: d is reached
        // twice but never re-entered while it is mid-walk.
        Write("d.yml", "version: 1\nchecks:\n  fromD:\n    failOn: error\n");
        Write("b.yml", "version: 1\nextends: d.yml\nchecks:\n  fromB:\n    failOn: error\n");
        Write("c.yml", "version: 1\nextends: d.yml\nchecks:\n  fromC:\n    failOn: error\n");
        var aPath = Write("a.yml", "version: 1\nextends: [b.yml, c.yml]\n");

        var profile = ProfileLoader.Load(aPath);
        Assert.True(profile.Checks.ContainsKey("fromB"));
        Assert.True(profile.Checks.ContainsKey("fromC"));
        Assert.True(profile.Checks.ContainsKey("fromD"));
    }

    [Fact]
    public void ArrayExtends_MissingParent_Throws()
    {
        Write("real.yml", "version: 1\n");
        var childPath = Write("child.yml", "version: 1\nextends: [real.yml, ghost.yml]\n");

        Assert.Throws<FileNotFoundException>(() => ProfileLoader.Load(childPath));
    }

    [Fact]
    public void ArrayExtends_EmptyArray_Throws()
    {
        var childPath = Write("child.yml", "version: 1\nextends: []\n");

        Assert.Throws<InvalidOperationException>(() => ProfileLoader.Load(childPath));
    }

    [Fact]
    public void ArrayExtends_PathEscape_Throws()
    {
        // ../ is rejected even when only one element of the array breaks out.
        Write("inside.yml", "version: 1\n");
        var childPath = Write("child.yml", "version: 1\nextends: [inside.yml, ../escapes.yml]\n");

        Assert.Throws<InvalidOperationException>(() => ProfileLoader.Load(childPath));
    }

    [Fact]
    public void ExtendsStrategy_DefaultIsReplace()
    {
        // No extendsStrategy field → typed enum reads as Replace, raw is null.
        Write("solo.yml", "version: 1\nchecks:\n  default:\n    failOn: error\n");
        var profile = ProfileLoader.Load(Path.Combine(_root, "solo.yml"));
        Assert.Equal(ExtendsStrategy.Replace, profile.ExtendsStrategy);
        Assert.Null(profile.ExtendsStrategyRaw);
    }

    [Fact]
    public void ExtendsStrategy_DeepMergeLiteral_ParsesAndExposesEnum()
    {
        Write("solo.yml", "version: 1\nextendsStrategy: deep-merge\nchecks:\n  default:\n    failOn: error\n");
        var profile = ProfileLoader.Load(Path.Combine(_root, "solo.yml"));
        Assert.Equal(ExtendsStrategy.DeepMerge, profile.ExtendsStrategy);
    }

    [Fact]
    public void ExtendsStrategy_UnknownLiteral_Throws()
    {
        Write("solo.yml", "version: 1\nextendsStrategy: bogus\n");
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProfileLoader.Load(Path.Combine(_root, "solo.yml")));
        Assert.Contains("extendsStrategy", ex.Message);
    }
}
