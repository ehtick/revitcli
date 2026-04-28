using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// CLI surface tests for <c>revitcli profile simulate &lt;pipeline&gt;</c>.
/// Each test stages a temp profile, runs the command pointing at it,
/// and asserts on captured stdout/stderr + exit code. The pure
/// simulator logic is covered by <c>ProfileSimulatorTests</c>; here
/// we only pin the CLI plumbing (table/json renderers, --fail-on
/// thresholds, error paths).
/// </summary>
public class ProfileSimulateCommandTests : IDisposable
{
    private readonly string _tempDir;

    public ProfileSimulateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"revitcli-simulate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }

    private string WriteProfile(string body)
    {
        var path = Path.Combine(_tempDir, ".revitcli.yml");
        File.WriteAllText(path, body);
        return path;
    }

    [Fact]
    public async Task Simulate_CleanPipeline_ExitsZero_AndRendersFindingsClean()
    {
        var path = WriteProfile("""
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
publish:
  client-A:
    precheck: default
    presets: [pdf]
exports:
  pdf:
    format: pdf
    sheets: [A101]
""");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "client-A", path, "table", "error", stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Empty(stderr.ToString());
        var text = stdout.ToString();
        Assert.Contains("Pipeline 'client-A'", text);
        Assert.Contains("Findings: (clean)", text);
    }

    [Fact]
    public async Task Simulate_UnknownPipeline_ExitsOneWithDiagnostic()
    {
        var path = WriteProfile("""
version: 1
publish:
  only-one:
    presets: [pdf]
exports:
  pdf:
    format: pdf
    sheets: [A101]
""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "missing", path, "table", "error", stdout, stderr);

        Assert.Equal(1, exit);
        Assert.Contains("Pipeline 'missing'", stderr.ToString());
        Assert.Contains("only-one", stderr.ToString()); // available list
    }

    [Fact]
    public async Task Simulate_PipelineWithMissingPreset_ExitsOneAtErrorThreshold()
    {
        var path = WriteProfile("""
version: 1
publish:
  client-A:
    presets: [ghost]
exports: {}
""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "client-A", path, "table", "error", stdout, stderr);

        Assert.Equal(1, exit);
        Assert.Contains("preset-missing", stdout.ToString());
        Assert.Contains("ghost", stdout.ToString());
    }

    [Fact]
    public async Task Simulate_AllSheetsInfoOnly_DefaultFailOnError_ExitsZero()
    {
        // 'sheets: ALL' is severity Info — by default --fail-on=error,
        // so info findings don't break the gate.
        var path = WriteProfile("""
version: 1
publish:
  client-A:
    presets: [dwg-all]
exports:
  dwg-all:
    format: dwg
    sheets: [ALL]
""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "client-A", path, "table", "error", stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("preset-all-sheets", stdout.ToString());
    }

    [Fact]
    public async Task Simulate_AllSheetsInfo_FailOnInfo_ExitsOne()
    {
        // Same finding, --fail-on=info ratchets the gate so any finding
        // (including Info) triggers exit 1. CI authors who want
        // belt-and-braces can set this for the strictest signal.
        var path = WriteProfile("""
version: 1
publish:
  client-A:
    presets: [dwg-all]
exports:
  dwg-all:
    format: dwg
    sheets: [ALL]
""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "client-A", path, "table", "info", stdout, stderr);

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Simulate_JsonOutput_IsParseable_AndCarriesEnumNames()
    {
        var path = WriteProfile("""
version: 1
publish:
  client-A:
    presets: [pdf]
exports:
  pdf:
    format: pdf
    sheets: [A101]
""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "client-A", path, "json", "error", stdout, stderr);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("client-A", doc.RootElement.GetProperty("Name").GetString());
        // The Severity enum is serialized via JsonStringEnumConverter
        // so the JSON shape stays human-readable across machines /
        // languages instead of leaking 0/1/2 integers.
        Assert.Equal("Info", doc.RootElement.GetProperty("WorstSeverity").GetString());
    }

    [Fact]
    public async Task Simulate_ProfileLoadFailure_ExitsOne()
    {
        // Corrupt YAML at the explicit --profile path. The simulator
        // shouldn't try to recover — surface the loader's diagnostic
        // and exit with a clean failure code so CI scripts don't think
        // the simulation passed silently.
        var path = WriteProfile("not: valid:::yaml");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ProfileCommand.ExecuteSimulateAsync(
            "any", path, "table", "error", stdout, stderr);

        Assert.Equal(1, exit);
        Assert.Contains("Error", stderr.ToString());
    }
}
