using System.Net.Http;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using RevitCli.Tests.Client;

namespace RevitCli.Tests.Commands;

public sealed class SheetsCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public SheetsCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-sheets-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousDirectory);
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Verify_BuiltinDefaults_DetectsDuplicateNumbers()
    {
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Plan", PlacedViewIds = { 100 } },
                new SnapshotSheet { ViewId = 11, Number = "A-101", Name = "Duplicate", PlacedViewIds = { 101 } },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, againstPath: null, rule: null, issuesOnly: false, outputFormat: "json", output);

        Assert.Equal(3, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("sheets verify", json.RootElement.GetProperty("command").GetString());
        Assert.Equal("(builtin defaults)", json.RootElement.GetProperty("configSource").GetString());
        var issue = Assert.Single(json.RootElement.GetProperty("issues").EnumerateArray());
        Assert.Equal("numbering.duplicate", issue.GetProperty("rule").GetString());
        Assert.Equal("error", issue.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Verify_AgainstIndex_DetectsGapsRequiredAndViewCounts()
    {
        var indexPath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
numbering:
  scheme: "A-{floor:01}{seq:02}"
  ranges:
    - floors: [1]
      seqMin: 1
      seqMax: 3
required:
  - pattern: "A-101"
    description: "Level 1 plan"
    needsViews:
      - minCount: 2
  - pattern: "A-104"
    description: "Missing sheet"
""");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Level 1", PlacedViewIds = { 100 } },
                new SnapshotSheet { ViewId = 11, Number = "A-103", Name = "Level 1 Reflected", PlacedViewIds = { 101 } },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, indexPath, rule: null, issuesOnly: false, outputFormat: "json", output);

        Assert.Equal(3, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var rules = json.RootElement.GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("rule").GetString())
            .ToArray();
        Assert.Contains("numbering.gap", rules);
        Assert.Contains("required.missing", rules);
        Assert.Contains("required.viewMissing", rules);
        Assert.Equal(3, json.RootElement.GetProperty("summary").GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task Verify_IssuesOnly_HidesInfoIssuesFromOutputButKeepsExitCode()
    {
        var indexPath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
""");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Empty" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, indexPath, rule: "linkage.emptySheet", issuesOnly: true, outputFormat: "json", output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Empty(json.RootElement.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task Verify_UnknownRule_ReturnsCommandErrorBeforeHttp()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, againstPath: null, rule: "numbering.nope", issuesOnly: false, outputFormat: "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown sheet rule", output.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Verify_ModelUnavailable_ReturnsFour()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, againstPath: null, rule: null, issuesOnly: false, outputFormat: "table", output);

        Assert.Equal(4, exitCode);
        Assert.Contains("Error:", output.ToString());
    }

    [Fact]
    public async Task IndexInit_WritesLocalYamlAndShowReadsIt()
    {
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Level 1", PlacedViewIds = { 100 } },
                new SnapshotSheet { ViewId = 11, Number = "A-102", Name = "Level 1 RCP" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIndexInitAsync(
            client, path: null, force: false, outputFormat: "table", output);

        Assert.Equal(0, exitCode);
        var indexPath = Path.Combine(_root, ".revitcli", "sheets", "index.yml");
        Assert.True(File.Exists(indexPath));
        Assert.Contains("Writing sheet index:", output.ToString());

        var showOutput = new StringWriter();
        var showExit = await SheetsCommand.ExecuteIndexShowAsync(null, "table", showOutput);

        Assert.Equal(0, showExit);
        Assert.Contains("A-101", showOutput.ToString());
        Assert.Contains("Required sheets: 2", showOutput.ToString());
    }

    [Fact]
    public async Task IndexInit_ExistingFileRequiresForce()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".revitcli", "sheets"));
        File.WriteAllText(Path.Combine(_root, ".revitcli", "sheets", "index.yml"), "schemaVersion: 1\n");
        var client = MakeClient(new ModelSnapshot());
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIndexInitAsync(
            client, path: null, force: false, outputFormat: "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("--force", output.ToString());
    }

    private string WriteIndex(string yaml)
    {
        var path = Path.Combine(_root, "index.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static RevitClient MakeClient(ModelSnapshot snapshot)
    {
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }
}
