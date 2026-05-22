using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class ModelCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public ModelCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-model-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task MapCheck_ReportsWorksetAndPhaseIssues()
    {
        var rules = WriteRules();
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapCheckAsync(
            MakeClient(Elements()),
            rules,
            worksets: true,
            phases: true,
            outputFormat: "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("model-map-report.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "workset-mismatch");
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "phase-created-mismatch");
    }

    [Fact]
    public async Task MapCheck_UnicodeCategoryMatchesOnlySelectedCategory()
    {
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapCheckAsync(
            MakeClient(UnicodeElements()),
            WriteUnicodeLevelRules(),
            worksets: true,
            phases: false,
            outputFormat: "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(3, json.RootElement.GetProperty("elementCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("checkedElementCount").GetInt32());
        var issue = Assert.Single(json.RootElement.GetProperty("issues").EnumerateArray());
        Assert.Equal(6101, issue.GetProperty("elementId").GetInt64());
        Assert.Equal("标高", issue.GetProperty("category").GetString());
        Assert.DoesNotContain(json.RootElement.GetProperty("issues").EnumerateArray(), item =>
            item.GetProperty("category").GetString() is "墙" or "门");
    }

    [Fact]
    public async Task MapCheck_CategoryMatchingPreservesEnglishNormalizationPluralsAndWildcards()
    {
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapCheckAsync(
            MakeClient(EnglishMatchingElements()),
            WriteEnglishMatchingRules(),
            worksets: true,
            phases: false,
            outputFormat: "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(4, json.RootElement.GetProperty("elementCount").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("checkedElementCount").GetInt32());
        var issueCategories = json.RootElement.GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("category").GetString())
            .ToArray();
        Assert.Contains("Rooms", issueCategories);
        Assert.Contains("Model Lines", issueCategories);
        Assert.Contains("Curtain Panels", issueCategories);
        Assert.DoesNotContain("Walls", issueCategories);
    }

    [Fact]
    public async Task MapFix_DryRun_WritesWritePrecheckPlan()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "model-map-fix.json");
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapFixAsync(
            MakeClient(Elements().Where(element => element.Category == "Rooms").ToArray()),
            WriteRules(),
            planPath,
            scope: "rooms",
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("model-map-fix-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.All(json.RootElement.GetProperty("actions").EnumerateArray(), action =>
        {
            Assert.True(action.GetProperty("canWrite").GetBoolean());
            Assert.True(action.GetProperty("writableProbe").GetBoolean());
        });

        var showOutput = new StringWriter();
        var showExitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", showOutput);
        Assert.Equal(0, showExitCode);
        using var showJson = JsonDocument.Parse(showOutput.ToString());
        Assert.Equal("plan-summary.v1", showJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("model-map-fix", showJson.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task MapFix_UnicodeScopePlansOnlySelectedCategory()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "model-map-fix-unicode.json");
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapFixAsync(
            MakeClient(UnicodeElements()),
            WriteUnicodeLevelRules(),
            planPath,
            scope: "标高",
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(6101, action.GetProperty("elementId").GetInt64());
        Assert.Equal("标高", action.GetProperty("category").GetString());
    }

    [Fact]
    public async Task MapFix_BlocksUnwritableTargets()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "model-map-fix-blocked.json");
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapFixAsync(
            MakeClient(Elements().Where(element => element.Category == "Doors").ToArray()),
            WriteRules(),
            planPath,
            scope: "doors",
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "target-not-writable");
    }

    [Fact]
    public async Task MapFix_BlocksMissingTargetWorksets()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "model-map-fix-missing-target.json");
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapFixAsync(
            MakeClient(Elements().Where(element => element.Category == "Rooms").ToArray()),
            WriteMissingTargetRules(),
            planPath,
            scope: "rooms",
            dryRun: true,
            maxChanges: 20,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.False(action.GetProperty("writableProbe").GetBoolean());
        Assert.False(action.GetProperty("canWrite").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "target-not-found");
    }


    [Fact]
    public async Task MapFix_RejectsRealWritePath()
    {
        var output = new StringWriter();

        var exitCode = await ModelCommand.ExecuteMapFixAsync(
            MakeClient(Elements()),
            WriteRules(),
            "model-map-fix.json",
            scope: "rooms",
            dryRun: false,
            maxChanges: 20,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
    }

    private string WriteRules()
    {
        var path = Path.Combine(_root, ".revitcli", "model-mapping.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
schemaVersion: model-mapping.v1
rules:
  - category: Rooms
    workset: Architecture
    phaseCreated: New Construction
  - category: Doors
    workset: Architecture
    phaseCreated: New Construction
""");
        return path;
    }

    private string WriteUnicodeLevelRules()
    {
        var path = Path.Combine(_root, ".revitcli", "model-mapping-unicode.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
schemaVersion: model-mapping.v1
rules:
  - category: 标高
    workset: 建筑
""");
        return path;
    }

    private string WriteEnglishMatchingRules()
    {
        var path = Path.Combine(_root, ".revitcli", "model-mapping-english.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
schemaVersion: model-mapping.v1
rules:
  - category: rOoM
    workset: Architecture
  - category: model-lines
    workset: Annotation
  - category: Curtain*
    workset: Envelope
""");
        return path;
    }

    private string WriteMissingTargetRules()
    {
        var path = Path.Combine(_root, ".revitcli", "model-mapping-missing-target.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
schemaVersion: model-mapping.v1
rules:
  - category: Rooms
    workset: Missing Workset
""");
        return path;
    }

    private static ModelMapElementInfo[] UnicodeElements()
    {
        return new[]
        {
            new ModelMapElementInfo
            {
                Id = 6101,
                Name = "标高 1",
                Category = "标高",
                TypeName = "Level",
                WorksetName = "共享标高轴网",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true,
                AvailableWorksets = { "建筑", "共享标高轴网" },
                AvailablePhases = { "现有", "新建" }
            },
            new ModelMapElementInfo
            {
                Id = 6102,
                Name = "基础墙",
                Category = "墙",
                TypeName = "Basic Wall",
                WorksetName = "结构",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true,
                AvailableWorksets = { "建筑", "结构" },
                AvailablePhases = { "现有", "新建" }
            },
            new ModelMapElementInfo
            {
                Id = 6103,
                Name = "办公室门",
                Category = "门",
                TypeName = "Single-Flush",
                WorksetName = "建筑",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true,
                AvailableWorksets = { "建筑" },
                AvailablePhases = { "现有", "新建" }
            }
        };
    }

    private static ModelMapElementInfo[] EnglishMatchingElements()
    {
        return new[]
        {
            new ModelMapElementInfo
            {
                Id = 6201,
                Name = "Room 201",
                Category = "Rooms",
                TypeName = "Room",
                WorksetName = "Interior",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true
            },
            new ModelMapElementInfo
            {
                Id = 6202,
                Name = "Grid note",
                Category = "Model Lines",
                TypeName = "Lines",
                WorksetName = "Drafting",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true
            },
            new ModelMapElementInfo
            {
                Id = 6203,
                Name = "Curtain panel A",
                Category = "Curtain Panels",
                TypeName = "Panel",
                WorksetName = "Facade",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true
            },
            new ModelMapElementInfo
            {
                Id = 6204,
                Name = "Exterior Wall",
                Category = "Walls",
                TypeName = "Basic Wall",
                WorksetName = "Architecture",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true
            }
        };
    }

    private static ModelMapElementInfo[] Elements()
    {
        return new[]
        {
            new ModelMapElementInfo
            {
                Id = 5101,
                Name = "Room 101",
                Category = "Rooms",
                TypeName = "Room",
                WorksetName = "Interior",
                PhaseCreated = "Existing",
                CanWriteWorkset = true,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true,
                AvailableWorksets = { "Architecture", "Interior" },
                AvailablePhases = { "Existing", "New Construction" }
            },
            new ModelMapElementInfo
            {
                Id = 5102,
                Name = "Door 101A",
                Category = "Doors",
                TypeName = "Single-Flush",
                WorksetName = "Interiors",
                PhaseCreated = "New Construction",
                CanWriteWorkset = false,
                CanWritePhaseCreated = true,
                CanWritePhaseDemolished = true,
                AvailableWorksets = { "Architecture", "Interiors" },
                AvailablePhases = { "Existing", "New Construction" }
            }
        };
    }

    private static RevitClient MakeClient(ModelMapElementInfo[] elements)
    {
        return new RevitClient(new HttpClient(new ModelMapHandler(elements)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class ModelMapHandler : HttpMessageHandler
    {
        private readonly ModelMapElementInfo[] _elements;

        public ModelMapHandler(ModelMapElementInfo[] elements)
        {
            _elements = elements;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/model/map" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(ApiResponse<ModelMapElementInfo[]>.Ok(_elements)),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
