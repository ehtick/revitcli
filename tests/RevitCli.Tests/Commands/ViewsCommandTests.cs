using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class ViewsCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public ViewsCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-views-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Audit_ReportsTemplateAndBrowserIssues()
    {
        var rules = WriteRules();
        var output = new StringWriter();

        var exitCode = await ViewsCommand.ExecuteAuditAsync(
            MakeClient(Views()),
            rules,
            checkTemplates: true,
            checkBrowser: true,
            outputFormat: "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("view-standards-report.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("errorCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "template-mismatch");
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "browser-parameter-missing");
    }

    [Fact]
    public async Task TemplateApply_DryRun_WritesFrozenViewTemplatePlan()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "view-template.json");
        var output = new StringWriter();

        var exitCode = await ViewsCommand.ExecuteTemplateApplyAsync(
            MakeClient(Views()),
            "Level*",
            "Architectural Plan",
            planPath,
            dryRun: true,
            exclude: "locked",
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("view-template-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(101, action.GetProperty("viewId").GetInt64());
        Assert.Equal(201, action.GetProperty("oldTemplateId").GetInt64());
        Assert.Equal(200, action.GetProperty("newTemplateId").GetInt64());
        Assert.True(action.GetProperty("isPlacedOnSheet").GetBoolean());
        Assert.Contains("\"schemaVersion\": \"view-template-plan.v1\"", output.ToString());

        var showJsonOutput = new StringWriter();
        var showJsonExitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", showJsonOutput);
        Assert.Equal(0, showJsonExitCode);
        using var showJson = JsonDocument.Parse(showJsonOutput.ToString());
        Assert.Equal("plan-summary.v1", showJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("view-template", showJson.RootElement.GetProperty("type").GetString());

        var showOutput = new StringWriter();
        var showExitCode = await PlanCommand.ExecuteShowAsync(planPath, "markdown", showOutput);
        Assert.Equal(0, showExitCode);
        Assert.Contains("# RevitCli Plan Review", showOutput.ToString());
        Assert.Contains("view-template", showOutput.ToString());
        Assert.Contains("Architectural Plan", showOutput.ToString());
        Assert.Contains("Level 1 Floor Plan", showOutput.ToString());
    }

    [Fact]
    public async Task TemplateApply_DryRun_ExcludesLockedViews()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "view-template-locked.json");
        var output = new StringWriter();

        var exitCode = await ViewsCommand.ExecuteTemplateApplyAsync(
            MakeClient(LockedViews()),
            "Level*",
            "Architectural Plan",
            planPath,
            dryRun: true,
            exclude: "locked",
            outputFormat: "json",
            output);

        Assert.Equal(2, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("actions").EnumerateArray());
    }

    [Fact]
    public async Task TemplateApply_RejectsRealWritePath()
    {
        var output = new StringWriter();

        var exitCode = await ViewsCommand.ExecuteTemplateApplyAsync(
            MakeClient(Views()),
            "Level*",
            "Architectural Plan",
            "view-template.json",
            dryRun: false,
            exclude: "locked",
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
    }

    [Fact]
    public async Task CloneSet_DetectsTargetNameCollisionsBeforeWritingPlan()
    {
        var planPath = Path.Combine(_root, "view-clone.json");
        var output = new StringWriter();

        var exitCode = await ViewsCommand.ExecuteCloneSetAsync(
            MakeClient(Views()),
            "Level*",
            "Tender - ",
            "{prefix}{name}",
            planPath,
            dryRun: true,
            includeSheets: false,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(planPath));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("view-clone-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "target-name-exists");
    }

    [Fact]
    public async Task CloneSet_DryRun_WritesPlanWithRollbackGuard()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "view-clone.json");
        var output = new StringWriter();

        var exitCode = await ViewsCommand.ExecuteCloneSetAsync(
            MakeClient(Views()),
            "Level*",
            "Bid - ",
            "{prefix}{name}",
            planPath,
            dryRun: true,
            includeSheets: false,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("view-clone-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        var action = Assert.Single(json.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(101, action.GetProperty("sourceViewId").GetInt64());
        Assert.Equal("Bid - Level 1 Floor Plan", action.GetProperty("targetName").GetString());
        Assert.Contains("placed on a sheet", action.GetProperty("rollbackGuard").GetString());

        var showOutput = new StringWriter();
        var showExitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", showOutput);
        Assert.Equal(0, showExitCode);
        using var showJson = JsonDocument.Parse(showOutput.ToString());
        Assert.Equal("plan-summary.v1", showJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("view-clone", showJson.RootElement.GetProperty("type").GetString());
    }

    private string WriteRules()
    {
        var directory = Path.Combine(_root, ".revitcli", "views");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "standards.yml");
        File.WriteAllText(path, """
schemaVersion: view-standards.v1
templates:
  - selector: "Level*"
    viewType: FloorPlan
    template: Architectural Plan
browser:
  requiredParameters: [View Group, Sub-Discipline]
naming:
  requiredPrefixes: [A-, Level]
  rejectDefaultNames: true
""");
        return path;
    }

    private static ViewInfo[] Views()
    {
        return new[]
        {
            new ViewInfo
            {
                Id = 101,
                Name = "Level 1 Floor Plan",
                ViewType = "FloorPlan",
                TemplateId = 201,
                TemplateName = "Wrong Template",
                CanBePrinted = true,
                IsPlacedOnSheet = true,
                Parameters =
                {
                    ["View Group"] = "Issue"
                }
            },
            new ViewInfo
            {
                Id = 102,
                Name = "Tender - Level 1 Floor Plan",
                ViewType = "FloorPlan",
                TemplateId = 200,
                TemplateName = "Architectural Plan",
                CanBePrinted = true
            },
            new ViewInfo
            {
                Id = 200,
                Name = "Architectural Plan",
                ViewType = "FloorPlan",
                IsTemplate = true
            },
            new ViewInfo
            {
                Id = 201,
                Name = "Wrong Template",
                ViewType = "FloorPlan",
                IsTemplate = true
            }
        };
    }

    private static ViewInfo[] LockedViews()
    {
        return new[]
        {
            new ViewInfo
            {
                Id = 301,
                Name = "Level 2 Floor Plan",
                ViewType = "FloorPlan",
                TemplateId = 201,
                TemplateName = "Wrong Template",
                IsLocked = true
            },
            new ViewInfo
            {
                Id = 200,
                Name = "Architectural Plan",
                ViewType = "FloorPlan",
                IsTemplate = true
            },
            new ViewInfo
            {
                Id = 201,
                Name = "Wrong Template",
                ViewType = "FloorPlan",
                IsTemplate = true
            }
        };
    }

    private static RevitClient MakeClient(ViewInfo[] views)
    {
        return new RevitClient(new HttpClient(new ViewsHandler(views)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class ViewsHandler : HttpMessageHandler
    {
        private readonly ViewInfo[] _views;

        public ViewsHandler(ViewInfo[] views)
        {
            _views = views;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/views" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(ApiResponse<ViewInfo[]>.Ok(_views)),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
