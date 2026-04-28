using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// Drives <see cref="ProfileCommand"/> via its public Execute*Async entry
/// points so we never go through the real <see cref="System.CommandLine"/>
/// parser (which would touch <see cref="Console.Out"/> globally and serialise
/// poorly under xUnit's parallel runner).
/// </summary>
public class ProfileCommandTests : IDisposable
{
    private readonly string _root;

    public ProfileCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-profile-cmd-" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------
    // validate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Validate_CleanProfile_ExitsZero()
    {
        var path = WriteProfile("clean.yml", """
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  main:
    precheck: default
    presets: [dwg]
""");

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteValidateAsync(path, writer);

        Assert.Equal(0, exit);
        Assert.Contains("OK", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_BrokenPrecheck_ExitsOneWithErrorLine()
    {
        var path = WriteProfile("broken.yml", """
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  main:
    precheck: nope
    presets: [dwg]
""");

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteValidateAsync(path, writer);

        Assert.Equal(1, exit);
        Assert.Contains("[error]", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("nope", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_MissingFile_ExitsOne()
    {
        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteValidateAsync(
            Path.Combine(_root, "does-not-exist.yml"),
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("Error", writer.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // show --resolve
    // ------------------------------------------------------------------

    [Fact]
    public async Task Show_Resolve_Yaml_RoundTripsForNoExtendsProfile()
    {
        var path = WriteProfile("a.yml", """
version: 1
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

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteShowAsync(path, resolve: true, "yaml", writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.StartsWith("# Resolved profile (chain:", output);

        // Strip header and re-parse via the loader.
        var bodyStart = output.IndexOf('\n');
        var body = output.Substring(bodyStart + 1);
        var roundTripPath = WriteProfile("a-rt.yml", body);
        var reloaded = ProfileLoader.Load(roundTripPath);

        Assert.Equal("warning", reloaded.Checks["default"].FailOn);
        Assert.True(reloaded.Exports.ContainsKey("dwg"));
        Assert.Equal("default", reloaded.Publish["main"].Precheck);
    }

    [Fact]
    public async Task Show_Resolve_Json_EmitsParseableJson()
    {
        var path = WriteProfile("a.yml", """
version: 1
checks:
  default:
    failOn: error
""");

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteShowAsync(path, resolve: true, "json", writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        // stdout must be parseable JSON straight off the wire — no `//`
        // chain header that would break `jq` and JsonDocument.Parse.
        Assert.False(output.TrimStart().StartsWith("//", StringComparison.Ordinal),
            "JSON output must not include a // chain header.");
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.TryGetProperty("checks", out _));
        Assert.True(doc.RootElement.TryGetProperty("publish", out _));
    }

    [Fact]
    public async Task Show_WithoutResolve_ExitsOne()
    {
        var path = WriteProfile("a.yml", """
version: 1
checks:
  default:
    failOn: error
""");

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteShowAsync(path, resolve: false, "yaml", writer);

        Assert.Equal(1, exit);
        Assert.Contains("--resolve", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_BadOutputFormat_ExitsOne()
    {
        var path = WriteProfile("a.yml", """
version: 1
checks:
  default:
    failOn: error
""");

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteShowAsync(path, resolve: true, "xml", writer);

        Assert.Equal(1, exit);
        Assert.Contains("yaml", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // diff
    // ------------------------------------------------------------------

    private const string SampleA = """
version: 1
defaults:
  outputDir: out/a
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  main:
    precheck: default
    presets: [dwg]
""";

    private const string SampleB = """
version: 1
defaults:
  outputDir: out/b
checks:
  default:
    failOn: warning
  extra:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  main:
    precheck: default
    presets: [dwg]
""";

    [Fact]
    public async Task Diff_IdenticalProfiles_TableHasNoBodyRows()
    {
        var aPath = WriteProfile("a.yml", SampleA);
        var bPath = WriteProfile("b.yml", SampleA);

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteDiffAsync(aPath, bPath, "table", writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("Path", output, StringComparison.Ordinal);
        Assert.Contains("Change", output, StringComparison.Ordinal);
        Assert.Contains("(no differences)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_DifferingProfiles_TableEmitsRowPerChange()
    {
        var aPath = WriteProfile("a.yml", SampleA);
        var bPath = WriteProfile("b.yml", SampleB);

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteDiffAsync(aPath, bPath, "table", writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        // Defaults outputDir changed, default check changed, extra check added.
        Assert.Contains("defaults.outputDir", output, StringComparison.Ordinal);
        Assert.Contains("checks.default", output, StringComparison.Ordinal);
        Assert.Contains("checks.extra", output, StringComparison.Ordinal);
        Assert.Contains("changed", output, StringComparison.Ordinal);
        Assert.Contains("added", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_Json_ProducesValidJsonWithExpectedTopKeys()
    {
        var aPath = WriteProfile("a.yml", SampleA);
        var bPath = WriteProfile("b.yml", SampleB);

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteDiffAsync(aPath, bPath, "json", writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("added").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("removed").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("changed").ValueKind);

        var addedPaths = root.GetProperty("added")
            .EnumerateArray()
            .Select(e => e.GetProperty("path").GetString())
            .ToList();
        Assert.Contains("checks.extra", addedPaths);
    }

    [Fact]
    public async Task Diff_Markdown_EmitsPipeTable()
    {
        var aPath = WriteProfile("a.yml", SampleA);
        var bPath = WriteProfile("b.yml", SampleB);

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteDiffAsync(aPath, bPath, "markdown", writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("| Path | Change | A | B |", output, StringComparison.Ordinal);
        Assert.Contains("| --- | --- | --- | --- |", output, StringComparison.Ordinal);
        Assert.Contains("| checks.extra |", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_MissingProfileFile_ExitsOne()
    {
        var aPath = WriteProfile("a.yml", SampleA);

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteDiffAsync(
            aPath,
            Path.Combine(_root, "nope.yml"),
            "table",
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("Error", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_UnknownOutputFormat_ExitsOne()
    {
        var aPath = WriteProfile("a.yml", SampleA);
        var bPath = WriteProfile("b.yml", SampleB);

        var writer = new StringWriter();
        var exit = await ProfileCommand.ExecuteDiffAsync(aPath, bPath, "csv", writer);

        Assert.Equal(1, exit);
        Assert.Contains("Error", writer.ToString(), StringComparison.Ordinal);
    }
}
