using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class LinksCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public LinksCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-links-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Audit_ReportsLoadedAndCoordinateIssues()
    {
        var rules = WriteRules();
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteAuditAsync(
            MakeClient(Links()),
            rules,
            "paths,loaded,coordinates",
            "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("link-audit-report.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("errorCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "link-not-loaded");
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "link-coordinate-drift");
    }

    [Fact]
    public async Task Repair_DryRun_WritesPathOnlyPlanWithEvidence()
    {
        var map = WritePathMap();
        var planPath = Path.Combine(_root, ".revitcli", "plans", "link-repair.json");
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(Links()),
            map,
            planPath,
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("link-repair-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(4101, action.GetProperty("linkId").GetInt64());
        Assert.Equal(4201, action.GetProperty("linkTypeId").GetInt64());
        Assert.Equal(4101, Assert.Single(action.GetProperty("instanceIds").EnumerateArray()).GetInt64());
        Assert.True(action.GetProperty("newPathExists").GetBoolean());
        Assert.True(action.TryGetProperty("newPathLastWriteTimeUtc", out _));
        Assert.False(action.TryGetProperty("transformFingerprint", out _));
        Assert.False(action.TryGetProperty("coordinate", out _));

        var showOutput = new StringWriter();
        var showExitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", showOutput);
        Assert.Equal(0, showExitCode);
        using var showJson = JsonDocument.Parse(showOutput.ToString());
        Assert.Equal("plan-summary.v1", showJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("link-repair", showJson.RootElement.GetProperty("type").GetString());

        var markdownOutput = new StringWriter();
        Assert.Equal(0, await PlanCommand.ExecuteShowAsync(planPath, "markdown", markdownOutput));
        Assert.Contains("[`4101`]", markdownOutput.ToString());
        Assert.DoesNotContain("[`4201`]", markdownOutput.ToString());
    }

    [Fact]
    public async Task Repair_DryRun_AllowsLoadOnlyPlans()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "link-load.json");
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(Links()),
            WriteLoadOnlyMap(),
            planPath,
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(4101, action.GetProperty("linkId").GetInt64());
        Assert.Equal(4201, action.GetProperty("linkTypeId").GetInt64());
        Assert.Equal(action.GetProperty("oldPath").GetString(), action.GetProperty("newPath").GetString());
        Assert.False(action.GetProperty("oldLoaded").GetBoolean());
        Assert.True(action.GetProperty("newLoaded").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("issueCount").GetInt32());
    }

    [Fact]
    public async Task Repair_DryRun_AllowsCloudLoadOnlyWithoutLocalPathEvidence()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "link-cloud-load.json");
        var cloudLink = Links()[0];
        cloudLink.Path = "Autodesk Docs://Hub/Project/Shared/Structural Model.rvt";
        cloudLink.PathExists = false;
        cloudLink.IsCloud = true;
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(new[] { cloudLink }),
            WriteLoadOnlyMap(),
            planPath,
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.False(action.GetProperty("newPathExists").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("issueCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("newPathMissingCount").GetInt32());
    }

    [Fact]
    public async Task Repair_DryRun_AllowsLocalLinkReplacementToCloudPathWithoutLocalEvidence()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "link-local-to-cloud.json");
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(Links()),
            WritePathMapTo("Autodesk Docs://Hub/Project/Shared/Structural Model.rvt"),
            planPath,
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.False(action.GetProperty("newPathExists").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("issueCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("newPathMissingCount").GetInt32());
    }

    [Fact]
    public async Task Repair_DryRun_RejectsCloudLinkReplacementToMissingLocalPath()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "link-cloud-to-missing-local.json");
        var cloudLink = Links()[0];
        cloudLink.Path = "Autodesk Docs://Hub/Project/Shared/Structural Model.rvt";
        cloudLink.PathExists = false;
        cloudLink.IsCloud = true;
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(new[] { cloudLink }),
            WritePathMapTo(Path.Combine(_root, "missing-local.rvt")),
            planPath,
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("issueCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("newPathMissingCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "new-path-missing");
    }

    [Fact]
    public async Task Repair_DryRun_DeduplicatesMultipleInstancesByLinkType()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "link-duplicate-instances.json");
        var duplicate = Links()[0];
        duplicate.Id = 4103;
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(new[] { Links()[0], duplicate }),
            WritePathMap(),
            planPath,
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(4201, action.GetProperty("linkTypeId").GetInt64());
        Assert.Equal(new long[] { 4101, 4103 }, action.GetProperty("instanceIds").EnumerateArray().Select(id => id.GetInt64()).ToArray());
    }

    [Fact]
    public async Task Repair_RejectsRealWritePath()
    {
        var output = new StringWriter();

        var exitCode = await LinksCommand.ExecuteRepairAsync(
            MakeClient(Links()),
            WritePathMap(),
            "link-repair.json",
            dryRun: false,
            maxChanges: 20,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
    }

    private string WriteRules()
    {
        var directory = Path.Combine(_root, ".revitcli", "links");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "rules.yml");
        File.WriteAllText(path, $"""
schemaVersion: link-rules.v1
links:
  - name: "Structural Model"
    path: "{Links()[0].Path.Replace("\\", "\\\\", StringComparison.Ordinal)}"
    required: true
    mustBeLoaded: true
    coordinateFingerprint: "origin=0,0,0"
""");
        return path;
    }

    private string WritePathMap()
    {
        var newPath = Path.Combine(_root, "new-struct.rvt");
        File.WriteAllText(newPath, "placeholder");
        return WritePathMapTo(newPath);
    }

    private string WritePathMapTo(string newPath)
    {
        var directory = Path.Combine(_root, ".revitcli", "links");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "paths.yml");
        File.WriteAllText(path, $"""
schemaVersion: link-path-map.v1
links:
  - name: "Structural Model"
    newPath: "{newPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
    load: true
""");
        return path;
    }

    private string WriteLoadOnlyMap()
    {
        var directory = Path.Combine(_root, ".revitcli", "links");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "load-only.yml");
        File.WriteAllText(path, """
schemaVersion: link-path-map.v1
links:
  - name: "Structural Model"
    load: true
""");
        return path;
    }

    private LinkInfo[] Links()
    {
        return new[]
        {
            new LinkInfo
            {
                Id = 4101,
                LinkTypeId = 4201,
                Name = "Structural Model",
                TypeName = "Structural Model.rvt",
                Path = Path.Combine(_root, "old-struct.rvt"),
                LinkedFileStatus = "Unloaded",
                IsLoaded = false,
                PathExists = true,
                TransformFingerprint = "origin=1,0,0"
            }
        };
    }

    private static RevitClient MakeClient(LinkInfo[] links)
    {
        return new RevitClient(new HttpClient(new LinksHandler(links)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class LinksHandler : HttpMessageHandler
    {
        private readonly LinkInfo[] _links;

        public LinksHandler(LinkInfo[] links)
        {
            _links = links;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/links" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(ApiResponse<LinkInfo[]>.Ok(_links)),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
