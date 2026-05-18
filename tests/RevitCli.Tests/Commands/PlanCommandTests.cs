using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Fix;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class PlanCommandTests : IDisposable
{
    private readonly string _tempDir;

    public PlanCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"revitcli_plan_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Show_Table_PrintsPlanSummary()
    {
        var planPath = WriteSamplePlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Plan: set 2 element(s)", output);
        Assert.Contains("Fire Rating", output);
        Assert.Contains("Door 1", output);
        Assert.Contains("revitcli plan apply", output);
    }

    [Fact]
    public async Task Show_Table_PrintsImportPlanSummary()
    {
        var planPath = WriteSampleImportPlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Plan: import 1 group(s), 2 element-write(s)", output);
        Assert.Contains("[Lock] = \"YALE-500\"", output);
        Assert.Contains("revitcli plan apply", output);
    }

    [Fact]
    public async Task Show_Table_PrintsFixPlanSummary()
    {
        var planPath = WriteSampleFixPlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Plan: fix 1 action(s)", output);
        Assert.Contains("required-parameter", output);
        Assert.Contains("Fire Rating", output);
        Assert.Contains("revitcli plan apply", output);
    }

    [Fact]
    public async Task Show_Json_PrintsSetPlanSummaryEnvelope()
    {
        var planPath = WriteSamplePlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("plan-summary.v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("set", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("affected").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("risk").GetProperty("changeCount").GetInt32());
        Assert.Equal("set", document.RootElement.GetProperty("plan").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Show_Json_PrintsImportPlanSummaryEnvelope()
    {
        var planPath = WriteSampleImportPlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("plan-summary.v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("import", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("elementWrites").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("risk").GetProperty("changeCount").GetInt32());
        Assert.Equal("import", document.RootElement.GetProperty("plan").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Show_Json_PrintsFixPlanSummaryEnvelope()
    {
        var planPath = WriteSampleFixPlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("plan-summary.v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("fix", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        var risk = document.RootElement.GetProperty("risk");
        Assert.Equal(1, risk.GetProperty("changeCount").GetInt32());
        Assert.True(risk.GetProperty("writesBaseline").GetBoolean());
        Assert.Equal("fix", document.RootElement.GetProperty("plan").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Show_Markdown_PrintsApprovalReview()
    {
        var planPath = WriteSamplePlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "markdown", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Plan Review", output);
        Assert.Contains("## Risk", output);
        Assert.Contains("Fire Rating", output);
        Assert.Contains("Door 1", output);
        Assert.Contains("Dry-run apply", output);
        Assert.Contains("Review the dry-run output", output);
    }

    [Fact]
    public async Task Apply_RequiresYesForWrites()
    {
        var planPath = WriteSamplePlan();
        var handler = new RecordingQueueHttpHandler();
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: false,
            dryRun: false,
            maxChanges: 50,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--yes", writer.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Apply_DryRun_PreviewsFrozenIdsWithoutReceipt()
    {
        var planPath = WriteSamplePlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 2,
            Preview = SamplePreview()
        }));
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: false,
            dryRun: true,
            maxChanges: 50,
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run: 2 element(s)", writer.ToString());
        Assert.Contains("\"elementIds\":[100,200]", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":true", handler.RequestBodies[0]);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_WritesReceiptAfterSuccessfulSet()
    {
        var planPath = WriteSamplePlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 2,
            Preview = SamplePreview()
        }));
        EnqueueStatus(handler);
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 50,
            writer,
            highImpactThreshold: 2,
            confirmHighImpact: true);

        var receiptPath = planPath + ".receipt.json";
        Assert.Equal(0, exitCode);
        Assert.Contains("Applied plan", writer.ToString());
        Assert.Contains("Receipt saved", writer.ToString());
        Assert.Contains("\"elementIds\":[100,200]", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":false", handler.RequestBodies[0]);
        Assert.True(File.Exists(receiptPath));
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = receipt.RootElement;
        Assert.Equal("plan-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("plan.apply", root.GetProperty("action").GetString());
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.Equal("set", root.GetProperty("operation").GetString());
        Assert.Equal(Environment.UserName, root.GetProperty("operator").GetString());
        Assert.Equal(Environment.MachineName, root.GetProperty("machine").GetString());
        Assert.Equal(@"C:\models\Demo.rvt", root.GetProperty("modelPath").GetString());
        Assert.Equal("2026", root.GetProperty("documentVersion").GetString());
        var command = root.GetProperty("command").GetString() ?? "";
        Assert.Contains("revitcli plan apply", command);
        Assert.Contains("--max-changes 50", command);
        Assert.Contains("--high-impact-threshold 2", command);
        Assert.Contains("--confirm-high-impact", command);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timestamp").GetString()));
        Assert.Equal(new long[] { 100, 200 }, ReadLongArray(root.GetProperty("affectedElementIds")));
        var rollbackActions = root.GetProperty("rollbackActions").EnumerateArray().ToArray();
        Assert.Equal(2, rollbackActions.Length);
        Assert.Equal(100, rollbackActions[0].GetProperty("elementId").GetInt64());
        Assert.Equal("Fire Rating", rollbackActions[0].GetProperty("param").GetString());
        Assert.Equal("30min", rollbackActions[0].GetProperty("oldValue").GetString());
        Assert.Equal("60min", rollbackActions[0].GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task Apply_HighImpactThresholdRequiresSecondConfirmation()
    {
        var planPath = WriteSamplePlan();
        var handler = new RecordingQueueHttpHandler();
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 50,
            writer,
            highImpactThreshold: 2);

        Assert.Equal(1, exitCode);
        Assert.Contains("high-impact threshold 2", writer.ToString());
        Assert.Contains("--confirm-high-impact", writer.ToString());
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_RespectsMaxChanges()
    {
        var planPath = WriteSamplePlan();
        var handler = new RecordingQueueHttpHandler();
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 1,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--max-changes", writer.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Apply_ImportPlan_DryRunPreviewsFrozenGroups()
    {
        var planPath = WriteSampleImportPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 2,
            Preview = SampleImportPreview()
        }));
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: false,
            dryRun: true,
            maxChanges: 50,
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run: 2 element-parameter pair(s)", writer.ToString());
        Assert.Contains("\"elementIds\":[101,102]", handler.RequestBodies[0]);
        Assert.Contains("\"param\":\"Lock\"", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":true", handler.RequestBodies[0]);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_ImportPlan_WritesReceipt()
    {
        var planPath = WriteSampleImportPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 2,
            Preview = SampleImportPreview()
        }));
        EnqueueStatus(handler);
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 50,
            writer);

        var receiptPath = planPath + ".receipt.json";
        Assert.Equal(0, exitCode);
        Assert.Contains("Applied import plan", writer.ToString());
        Assert.Contains("\"dryRun\":false", handler.RequestBodies[0]);
        Assert.True(File.Exists(receiptPath));
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = receipt.RootElement;
        Assert.Equal("plan-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("import", root.GetProperty("operation").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(@"C:\models\Demo.rvt", root.GetProperty("modelPath").GetString());
        Assert.Equal("2026", root.GetProperty("documentVersion").GetString());
        Assert.Equal(new long[] { 101, 102 }, ReadLongArray(root.GetProperty("affectedElementIds")));
        var rollbackActions = root.GetProperty("rollbackActions").EnumerateArray().ToArray();
        Assert.Equal(2, rollbackActions.Length);
        Assert.Equal("Lock", rollbackActions[0].GetProperty("param").GetString());
        Assert.Equal("OLD", rollbackActions[0].GetProperty("oldValue").GetString());
        Assert.Equal("YALE-500", rollbackActions[0].GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task Apply_FixPlan_DryRunPreviewsActions()
    {
        var planPath = WriteSampleFixPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 501, Name = "Door 501", OldValue = "", NewValue = "60min" }
            }
        }));
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: false,
            dryRun: true,
            maxChanges: 50,
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run: 1 element parameter(s)", writer.ToString());
        Assert.Contains("\"elementId\":501", handler.RequestBodies[0]);
        Assert.Contains("\"param\":\"Fire Rating\"", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":true", handler.RequestBodies[0]);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_FixPlan_WritesBaselineJournalAndReceipt()
    {
        var planPath = WriteSampleFixPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Demo.rvt",
                DocumentPath = @"C:\models\Demo.rvt"
            }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 501, Name = "Door 501", OldValue = "", NewValue = "60min" }
            }
        }));
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 50,
            writer);

        var receiptPath = planPath + ".receipt.json";
        Assert.Equal(0, exitCode);
        Assert.Contains("Applied fix plan", writer.ToString());
        Assert.Contains("Rollback: revitcli rollback", writer.ToString());
        Assert.Contains("\"dryRun\":false", handler.RequestBodies[1]);
        Assert.True(File.Exists(receiptPath));

        var baselinePath = Assert.Single(
            Directory.GetFiles(_tempDir, "fix-baseline-*.json"),
            path => !path.EndsWith(".fixjournal.json", StringComparison.OrdinalIgnoreCase));
        var journalPath = Path.Combine(
            _tempDir,
            Path.GetFileNameWithoutExtension(baselinePath) + ".fixjournal.json");
        Assert.True(File.Exists(journalPath));
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = receipt.RootElement;
        Assert.Equal("plan-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("fix", root.GetProperty("operation").GetString());
        Assert.True(root.GetProperty("requiresRollback").GetBoolean());
        Assert.Equal(Path.GetFullPath(baselinePath), root.GetProperty("baselinePath").GetString());
        Assert.Equal(journalPath, root.GetProperty("journalPath").GetString());
        Assert.Equal(@"C:\models\Demo.rvt", root.GetProperty("modelPath").GetString());
        Assert.Equal("2026", root.GetProperty("documentVersion").GetString());
        Assert.Equal(new long[] { 501 }, ReadLongArray(root.GetProperty("affectedElementIds")));
    }

    [Fact]
    public async Task Apply_ImportPlan_RespectsMaxChanges()
    {
        var planPath = WriteSampleImportPlan();
        var handler = new RecordingQueueHttpHandler();
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 1,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--max-changes", writer.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ResolveApplySafety_UsesProfileDefaults()
    {
        var profilePath = Path.Combine(_tempDir, ".revitcli.yml");
        File.WriteAllText(profilePath, """
version: 1
defaults:
  planMaxChanges: 7
  highImpactChanges: 3
""");

        var safety = PlanCommand.ResolveApplySafety(
            profilePath,
            maxChanges: null,
            highImpactThreshold: null);

        Assert.True(safety.Success);
        Assert.Equal(7, safety.MaxChanges);
        Assert.Equal(3, safety.HighImpactThreshold);
    }

    [Fact]
    public void ResolveApplySafety_CliValuesOverrideProfileDefaults()
    {
        var profilePath = Path.Combine(_tempDir, ".revitcli.yml");
        File.WriteAllText(profilePath, """
version: 1
defaults:
  planMaxChanges: 7
  highImpactChanges: 3
""");

        var safety = PlanCommand.ResolveApplySafety(
            profilePath,
            maxChanges: 12,
            highImpactThreshold: 10);

        Assert.True(safety.Success);
        Assert.Equal(12, safety.MaxChanges);
        Assert.Equal(10, safety.HighImpactThreshold);
    }

    [Fact]
    public void ResolveApplySafety_RejectsInvalidProfileDefaults()
    {
        var profilePath = Path.Combine(_tempDir, ".revitcli.yml");
        File.WriteAllText(profilePath, """
version: 1
defaults:
  planMaxChanges: 0
""");

        var safety = PlanCommand.ResolveApplySafety(
            profilePath,
            maxChanges: null,
            highImpactThreshold: null);

        Assert.False(safety.Success);
        Assert.Contains("planMaxChanges", safety.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteSamplePlan()
    {
        var planPath = Path.Combine(_tempDir, "set.plan.json");
        var plan = SetPlanFile.Create(
            new SetRequest
            {
                Category = "doors",
                Filter = "name contains Fire",
                Param = "Fire Rating",
                Value = "60min",
                DryRun = true
            },
            new SetResult
            {
                Affected = 2,
                Preview = SamplePreview()
            },
            planPath);
        SetPlanFileStore.Save(planPath, plan);
        return planPath;
    }

    private string WriteSampleImportPlan()
    {
        var planPath = Path.Combine(_tempDir, "import.plan.json");
        var plan = new ImportPlan
        {
            Groups =
            {
                new ImportGroup
                {
                    Param = "Lock",
                    Value = "YALE-500",
                    ElementIds = new List<long> { 101, 102 }
                }
            }
        };
        var csv = new CsvData
        {
            EncodingName = "utf-8",
            Headers = new List<string> { "Mark", "Lock" },
            Rows =
            {
                new List<string> { "W01", "YALE-500" },
                new List<string> { "W02", "YALE-500" }
            }
        };
        var planFile = ImportPlanFile.Create(
            "doors.csv",
            csv,
            new Dictionary<string, string> { ["Lock"] = "Lock" },
            "doors",
            "Mark",
            "warn",
            "error",
            batchSize: 100,
            plan,
            new List<ImportPlanPreviewGroup>
            {
                new()
                {
                    Param = "Lock",
                    Value = "YALE-500",
                    ElementIds = new List<long> { 101, 102 },
                    Preview = SampleImportPreview()
                }
            },
            planPath);
        SetPlanFileStore.SaveImport(planPath, planFile);
        return planPath;
    }

    private string WriteSampleFixPlan()
    {
        var planPath = Path.Combine(_tempDir, "fix.plan.json");
        var plan = new FixPlan
        {
            CheckName = "default",
            Actions =
            {
                new FixAction
                {
                    Rule = "required-parameter",
                    Strategy = "setParam",
                    ElementId = 501,
                    Category = "doors",
                    Parameter = "Fire Rating",
                    OldValue = "",
                    NewValue = "60min",
                    Confidence = "high",
                    Reason = "required parameter"
                }
            }
        };
        var planFile = FixPlanFile.Create(
            plan,
            profilePath: ".revitcli.yml",
            rules: Array.Empty<string>(),
            severity: null,
            planPath);
        SetPlanFileStore.SaveFix(planPath, planFile);
        return planPath;
    }

    private static List<SetPreviewItem> SamplePreview()
    {
        return new List<SetPreviewItem>
        {
            new() { Id = 100, Name = "Door 1", OldValue = "30min", NewValue = "60min" },
            new() { Id = 200, Name = "Door 2", OldValue = "30min", NewValue = "60min" }
        };
    }

    private static List<SetPreviewItem> SampleImportPreview()
    {
        return new List<SetPreviewItem>
        {
            new() { Id = 101, Name = "Door 1", OldValue = "OLD", NewValue = "YALE-500" },
            new() { Id = 102, Name = "Door 2", OldValue = "OLD", NewValue = "YALE-500" }
        };
    }

    private static RevitClient MakeClient(HttpMessageHandler handler)
    {
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private static void EnqueueStatus(RecordingQueueHttpHandler handler)
    {
        handler.Enqueue("/api/status", ApiResponse<StatusInfo>.Ok(new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            DocumentName = "Demo.rvt",
            DocumentPath = @"C:\models\Demo.rvt"
        }));
    }

    private static long[] ReadLongArray(JsonElement array)
    {
        return array.EnumerateArray()
            .Select(item => item.GetInt64())
            .ToArray();
    }
}
