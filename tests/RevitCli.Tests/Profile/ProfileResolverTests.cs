using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Profile;

/// <summary>
/// Tests <see cref="ProfileResolver"/> by writing real .yml files into a
/// temp dir per test. The resolver depends on <see cref="ProfileLoader"/>
/// internally, so the chain has to live on disk for the loader to walk it —
/// pure in-memory tests are not possible here.
/// </summary>
public class ProfileResolverTests : IDisposable
{
    private readonly string _root;

    public ProfileResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-profile-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string WriteProfile(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, body);
        return path;
    }

    [Fact]
    public void Chain_NoExtends_IsSingleEntry()
    {
        var path = WriteProfile("a.yml", """
version: 1
checks:
  default:
    failOn: error
""");

        var chain = ProfileResolver.GetInheritanceChain(path);

        var only = Assert.Single(chain);
        Assert.Equal(Path.GetFullPath(path), only);
    }

    [Fact]
    public void Render_Yaml_NoExtends_RoundTripsThroughLoader()
    {
        var path = WriteProfile("solo.yml", """
version: 1
defaults:
  outputDir: out/
checks:
  default:
    failOn: warning
exports:
  dwg:
    format: dwg
publish:
  main:
    precheck: default
    presets: [dwg]
""");

        var rendered = ProfileResolver.Render(path, ProfileRenderFormat.Yaml);

        Assert.StartsWith("# Resolved profile (chain:", rendered);
        // Strip the leading comment header and re-load via the loader: the
        // chain header is informational; the body must remain a valid profile.
        var firstNewline = rendered.IndexOf('\n');
        Assert.True(firstNewline > 0, "rendered output must contain a newline after the header");
        var body = rendered.Substring(firstNewline + 1);

        var roundTripPath = WriteProfile("solo-roundtrip.yml", body);
        var reloaded = ProfileLoader.Load(roundTripPath);

        Assert.Equal("warning", reloaded.Checks["default"].FailOn);
        Assert.True(reloaded.Exports.ContainsKey("dwg"));
        Assert.Equal("default", reloaded.Publish["main"].Precheck);
    }

    [Fact]
    public void SingleExtends_ChildKeysReplaceParentNamedEntries()
    {
        WriteProfile("parent.yml", """
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
""");
        var childPath = WriteProfile("child.yml", """
version: 1
extends: parent.yml
checks:
  default:
    failOn: warning
""");

        var merged = ProfileLoader.Load(childPath);
        Assert.Equal("warning", merged.Checks["default"].FailOn);
        // Parent's exports survive because child did not redefine them.
        Assert.True(merged.Exports.ContainsKey("dwg"));

        var chain = ProfileResolver.GetInheritanceChain(childPath);
        Assert.Equal(2, chain.Count);
        Assert.EndsWith("parent.yml", chain[0]);
        Assert.EndsWith("child.yml", chain[1]);
    }

    [Fact]
    public void ChainDepth3_IsResolvedAndReportedInOrder()
    {
        WriteProfile("g.yml", """
version: 1
checks:
  default:
    failOn: error
""");
        WriteProfile("p.yml", """
version: 1
extends: g.yml
exports:
  dwg:
    format: dwg
""");
        var leafPath = WriteProfile("c.yml", """
version: 1
extends: p.yml
publish:
  main:
    precheck: default
    presets: [dwg]
""");

        var chain = ProfileResolver.GetInheritanceChain(leafPath);
        Assert.Equal(3, chain.Count);
        Assert.EndsWith("g.yml", chain[0]);
        Assert.EndsWith("p.yml", chain[1]);
        Assert.EndsWith("c.yml", chain[2]);

        // The merged profile carries entries from all three layers.
        var merged = ProfileLoader.Load(leafPath);
        Assert.True(merged.Checks.ContainsKey("default"));
        Assert.True(merged.Exports.ContainsKey("dwg"));
        Assert.True(merged.Publish.ContainsKey("main"));
    }

    [Fact]
    public void Render_Json_EmitsParseableJsonWithoutCommentHeader()
    {
        var path = WriteProfile("a.yml", """
version: 1
checks:
  default:
    failOn: error
""");

        var rendered = ProfileResolver.Render(path, ProfileRenderFormat.Json);

        // JSON output must not be prefixed with a `//` comment — JSON has no
        // comment syntax and machine consumers (jq, JsonDocument.Parse, CI
        // tooling) would fail on the very first line. The chain header is
        // YAML-only.
        Assert.False(rendered.TrimStart().StartsWith("//", StringComparison.Ordinal),
            "JSON output must not include a // chain header.");

        // JsonDocument.Parse will throw on malformed JSON — that is the assertion.
        using var doc = JsonDocument.Parse(rendered);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("checks", out _));
    }

    [Fact]
    public void Render_HeaderIncludesAllChainParentsInOrder()
    {
        WriteProfile("base.yml", """
version: 1
checks:
  default:
    failOn: error
""");
        var midPath = WriteProfile("mid.yml", """
version: 1
extends: base.yml
""");
        var leafPath = WriteProfile("leaf.yml", """
version: 1
extends: mid.yml
""");

        var rendered = ProfileResolver.Render(leafPath, ProfileRenderFormat.Yaml);
        var firstLine = rendered.Split('\n').First();

        // Order in the header is oldest-first: base -> mid -> leaf -> effective.
        var baseIdx = firstLine.IndexOf("base.yml", StringComparison.Ordinal);
        var midIdx = firstLine.IndexOf("mid.yml", StringComparison.Ordinal);
        var leafIdx = firstLine.IndexOf("leaf.yml", StringComparison.Ordinal);
        Assert.True(baseIdx >= 0 && midIdx > baseIdx && leafIdx > midIdx,
            $"chain order wrong in header: {firstLine}");
    }
}
