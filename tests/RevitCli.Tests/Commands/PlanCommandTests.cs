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
using RevitCli.Numbering;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using RevitCli.Sheets;
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
    public async Task Apply_SheetIssuePlan_DryRunPreviewsGroupedActions()
    {
        var planPath = WriteSampleSheetIssuePlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 10, Name = "A-101", OldValue = "R02", NewValue = "R03" }
            }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 10, Name = "A-101", OldValue = "2026-05-01", NewValue = "2026-05-20" }
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
        Assert.Contains("Dry run: 2 sheet issue metadata value(s)", writer.ToString());
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("\"elementIds\":[10]", handler.RequestBodies[0]);
        Assert.Contains("\"param\":\"Sheet Issue Code\"", handler.RequestBodies[0]);
        Assert.Contains("\"value\":\"R03\"", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":true", handler.RequestBodies[0]);
        Assert.Contains("\"param\":\"Sheet Issue Date\"", handler.RequestBodies[1]);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_SheetIssuePlan_WritesReceiptWithPerParameterRollbackActions()
    {
        var planPath = WriteSampleSheetIssuePlan();
        var handler = new RecordingQueueHttpHandler();
        EnqueueSheetIssueSnapshot(handler);
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 10, Name = "A-101", OldValue = "R02", NewValue = "R03" }
            }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 10, Name = "A-101", OldValue = "2026-05-01", NewValue = "2026-05-20" }
            }
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
        Assert.Contains("Applied sheet issue plan", writer.ToString());
        Assert.Contains("Receipt saved", writer.ToString());
        Assert.True(File.Exists(receiptPath));
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = receipt.RootElement;
        Assert.Equal("plan-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("sheet-issue", root.GetProperty("operation").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(@"C:\models\Demo.rvt", root.GetProperty("modelPath").GetString());
        Assert.Equal("Demo.rvt", root.GetProperty("documentName").GetString());
        Assert.Equal("2026", root.GetProperty("documentVersion").GetString());
        Assert.Equal(2, root.GetProperty("elementWrites").GetInt32());
        Assert.Equal(new long[] { 10 }, ReadLongArray(root.GetProperty("affectedElementIds")));
        var rollbackActions = root.GetProperty("rollbackActions").EnumerateArray().ToArray();
        Assert.Equal(2, rollbackActions.Length);
        Assert.Contains(rollbackActions, action =>
            action.GetProperty("param").GetString() == "Sheet Issue Code" &&
            action.GetProperty("oldValue").GetString() == "R02" &&
            action.GetProperty("newValue").GetString() == "R03" &&
            action.GetProperty("source").GetString() == "sheet-issue");
        Assert.Contains(rollbackActions, action =>
            action.GetProperty("param").GetString() == "Sheet Issue Date" &&
            action.GetProperty("oldValue").GetString() == "2026-05-01" &&
            action.GetProperty("newValue").GetString() == "2026-05-20");
    }

    [Fact]
    public async Task Apply_SheetIssuePlan_RejectsWrongCurrentModelBeforeWrites()
    {
        var planPath = WriteSampleSheetIssuePlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Other.rvt",
                DocumentPath = @"C:\models\Other.rvt"
            },
            Model = new SnapshotModel { FileHash = "different" }
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

        Assert.Equal(1, exitCode);
        Assert.Contains("current model hash", writer.ToString());
        Assert.Equal(new[] { "/api/snapshot" }, handler.Requests);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_SheetIssuePlan_RejectsStaleOldValuesBeforeWrites()
    {
        var planPath = WriteSampleSheetIssuePlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Demo.rvt",
                DocumentPath = @"C:\models\Demo.rvt"
            },
            Model = new SnapshotModel { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters =
                    {
                        ["Sheet Issue Code"] = "R02B",
                        ["Sheet Issue Date"] = "2026-05-01"
                    }
                }
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

        Assert.Equal(1, exitCode);
        Assert.Contains("sheet issue plan is stale", writer.ToString());
        Assert.Contains("expected \"R02\"", writer.ToString());
        Assert.Equal(new[] { "/api/snapshot" }, handler.Requests);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_SheetRenumberPlan_DryRunPreviewsGroupedActions()
    {
        var planPath = WriteSampleSheetRenumberPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 10, Name = "Level 1", OldValue = "TMP-001", NewValue = "A-101" }
            }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 11, Name = "Level 2", OldValue = "TMP-002", NewValue = "A-102" }
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
        Assert.Contains("Dry run: 2 sheet number(s)", writer.ToString());
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("\"elementIds\":[10]", handler.RequestBodies[0]);
        Assert.Contains("\"param\":\"Sheet Number\"", handler.RequestBodies[0]);
        Assert.Contains("\"value\":\"A-101\"", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":true", handler.RequestBodies[0]);
        Assert.Contains("\"elementIds\":[11]", handler.RequestBodies[1]);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_SheetRenumberPlan_WritesReceiptWithRollbackActions()
    {
        var planPath = WriteSampleSheetRenumberPlan();
        var handler = new RecordingQueueHttpHandler();
        EnqueueSheetRenumberSnapshot(handler);
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 10, Name = "Level 1", OldValue = "TMP-001", NewValue = "A-101" }
            }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 11, Name = "Level 2", OldValue = "TMP-002", NewValue = "A-102" }
            }
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
        Assert.Contains("Applied sheet renumber plan", writer.ToString());
        Assert.True(File.Exists(receiptPath));
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = receipt.RootElement;
        Assert.Equal("plan-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("sheet-renumber", root.GetProperty("operation").GetString());
        Assert.Equal(@"C:\models\Demo.rvt", root.GetProperty("modelPath").GetString());
        Assert.Equal("Demo.rvt", root.GetProperty("documentName").GetString());
        Assert.Equal("2026", root.GetProperty("documentVersion").GetString());
        Assert.Equal("Sheet Number", root.GetProperty("param").GetString());
        Assert.Equal(2, root.GetProperty("elementWrites").GetInt32());
        Assert.Equal(new long[] { 10, 11 }, ReadLongArray(root.GetProperty("affectedElementIds")));
        var rollbackActions = root.GetProperty("rollbackActions").EnumerateArray().ToArray();
        Assert.Equal(2, rollbackActions.Length);
        Assert.Contains(rollbackActions, action =>
            action.GetProperty("param").GetString() == "Sheet Number" &&
            action.GetProperty("oldValue").GetString() == "TMP-001" &&
            action.GetProperty("newValue").GetString() == "A-101" &&
            action.GetProperty("source").GetString() == "sheet-renumber");
    }

    [Fact]
    public async Task Apply_SheetRenumberPlan_RejectsStaleOldNumbersBeforeWrites()
    {
        var planPath = WriteSampleSheetRenumberPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Demo.rvt",
                DocumentPath = @"C:\models\Demo.rvt"
            },
            Model = new SnapshotModel { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "TMP-009", Name = "Level 1" },
                new SnapshotSheet { ViewId = 11, Number = "TMP-002", Name = "Level 2" },
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

        Assert.Equal(1, exitCode);
        Assert.Contains("sheet renumber plan is stale", writer.ToString());
        Assert.Contains("expected number \"TMP-001\"", writer.ToString());
        Assert.Equal(new[] { "/api/snapshot" }, handler.Requests);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_SheetRenumberPlan_RejectsSelectedNumberReuseBeforeWrites()
    {
        var planPath = WriteSampleSheetRenumberPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Demo.rvt",
                DocumentPath = @"C:\models\Demo.rvt"
            },
            Model = new SnapshotModel { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "TMP-001", Name = "Level 1" },
                new SnapshotSheet { ViewId = 11, Number = "A-101", Name = "Level 2" },
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

        Assert.Equal(1, exitCode);
        Assert.Contains("still used by selected sheet", writer.ToString());
        Assert.Equal(new[] { "/api/snapshot" }, handler.Requests);
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Show_RoomNumberingPlan_PrintsActions()
    {
        var planPath = WriteSampleRoomNumberingPlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("room-numbering-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("room-numbering", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task Apply_RoomNumberingPlan_DryRunPreviewsFrozenGroups()
    {
        var planPath = WriteSampleRoomNumberingPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = { new SetPreviewItem { Id = 10, Name = "Office", OldValue = "101", NewValue = "L1-001" } }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = { new SetPreviewItem { Id = 11, Name = "Lobby", OldValue = "102", NewValue = "L1-002" } }
        }));
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client, planPath, yes: false, dryRun: true, maxChanges: 50, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run: 2 room number(s)", writer.ToString());
        Assert.All(handler.RequestBodies, body => Assert.Contains("\"dryRun\":true", body));
    }

    [Fact]
    public async Task Apply_RoomNumberingPlan_WritesReceiptWithRollbackActions()
    {
        var planPath = WriteSampleRoomNumberingPlan();
        var handler = new RecordingQueueHttpHandler();
        EnqueueRooms(handler, "101", "102");
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = { new SetPreviewItem { Id = 10, Name = "Office", OldValue = "101", NewValue = "L1-001" } }
        }));
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = { new SetPreviewItem { Id = 11, Name = "Lobby", OldValue = "102", NewValue = "L1-002" } }
        }));
        EnqueueStatus(handler);
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client, planPath, yes: true, dryRun: false, maxChanges: 50, writer);

        Assert.Equal(0, exitCode);
        var receiptPath = planPath + ".receipt.json";
        Assert.True(File.Exists(receiptPath));
        using var json = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = json.RootElement;
        Assert.Equal("room-numbering", root.GetProperty("operation").GetString());
        Assert.Equal("Number", root.GetProperty("param").GetString());
        Assert.Equal(2, root.GetProperty("rollbackActions").GetArrayLength());
        Assert.Contains(root.GetProperty("rollbackActions").EnumerateArray(), action =>
            action.GetProperty("elementId").GetInt64() == 10 &&
            action.GetProperty("oldValue").GetString() == "101" &&
            action.GetProperty("newValue").GetString() == "L1-001");
    }

    [Fact]
    public async Task Apply_RoomNumberingPlan_RejectsStaleOldNumbersBeforeWrites()
    {
        var planPath = WriteSampleRoomNumberingPlan();
        var handler = new RecordingQueueHttpHandler();
        EnqueueRooms(handler, "999", "102");
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client, planPath, yes: true, dryRun: false, maxChanges: 50, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("room numbering plan is stale", writer.ToString());
        Assert.DoesNotContain("/api/elements/set", handler.Requests);
    }

    [Fact]
    public async Task Apply_LinkRepairPlan_DryRunUsesRepairEndpoint()
    {
        var planPath = WriteSampleLinkRepairPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/links/repair", ApiResponse<LinkRepairResult>.Ok(new LinkRepairResult
        {
            Affected = 1,
            Preview = { SampleLinkRepairOperation() }
        }));
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client, planPath, yes: false, dryRun: true, maxChanges: 20, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run: 1 link repair action", writer.ToString());
        Assert.Equal("/api/links/repair", Assert.Single(handler.Requests));
        Assert.Contains("\"dryRun\":true", Assert.Single(handler.RequestBodies));
        Assert.False(File.Exists(planPath + ".receipt.json"));
    }

    [Fact]
    public async Task Apply_LinkRepairPlan_WritesReceiptWithLinkRollbackActions()
    {
        var planPath = WriteSampleLinkRepairPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/links/repair", ApiResponse<LinkRepairResult>.Ok(new LinkRepairResult
        {
            Affected = 1,
            Preview = { SampleLinkRepairOperation() }
        }));
        EnqueueStatus(handler);
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client, planPath, yes: true, dryRun: false, maxChanges: 20, writer);

        Assert.Equal(0, exitCode);
        var receiptPath = planPath + ".receipt.json";
        Assert.True(File.Exists(receiptPath));
        using var json = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = json.RootElement;
        Assert.Equal("link-repair", root.GetProperty("operation").GetString());
        Assert.Equal(new long[] { 4201 }, ReadLongArray(root.GetProperty("affectedElementIds")));
        var action = Assert.Single(root.GetProperty("linkRepairActions").EnumerateArray());
        Assert.Equal(4101, action.GetProperty("linkId").GetInt64());
        Assert.Equal(4201, action.GetProperty("linkTypeId").GetInt64());
        Assert.Equal(@"D:\coordination\old-struct.rvt", action.GetProperty("oldPath").GetString());
        Assert.Equal(@"D:\coordination\new-struct.rvt", action.GetProperty("newPath").GetString());
    }

    [Fact]
    public async Task Apply_ModelMapFixPlan_WritesReceiptWithModelMapRollbackActions()
    {
        var planPath = WriteSampleModelMapFixPlan();
        var handler = new RecordingQueueHttpHandler();
        handler.Enqueue("/api/model/map/fix", ApiResponse<ModelMapFixResult>.Ok(new ModelMapFixResult
        {
            Affected = 1,
            Preview =
            {
                new ModelMapFixOperation
                {
                    ElementId = 5101,
                    ElementName = "Room 101",
                    Category = "Rooms",
                    Field = "workset",
                    OldValue = "Interior",
                    NewValue = "Architecture"
                }
            }
        }));
        EnqueueStatus(handler);
        var client = MakeClient(handler);
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client, planPath, yes: true, dryRun: false, maxChanges: 20, writer);

        Assert.Equal(0, exitCode);
        var receiptPath = planPath + ".receipt.json";
        Assert.True(File.Exists(receiptPath));
        using var json = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = json.RootElement;
        Assert.Equal("model-map-fix", root.GetProperty("operation").GetString());
        Assert.Equal(new long[] { 5101 }, ReadLongArray(root.GetProperty("affectedElementIds")));
        var action = Assert.Single(root.GetProperty("modelMapActions").EnumerateArray());
        Assert.Equal("workset", action.GetProperty("field").GetString());
        Assert.Equal("Interior", action.GetProperty("oldValue").GetString());
        Assert.Equal("Architecture", action.GetProperty("newValue").GetString());
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

    private string WriteSampleSheetIssuePlan()
    {
        var planPath = Path.Combine(_tempDir, "sheet-issue.plan.json");
        var plan = new SheetIssuePlan(
            "sheet-issue-plan.v1",
            "sheet-issue",
            "sheets issue-meta",
            "2026-05-20T10:00:00Z",
            "tester",
            true,
            "all",
            "R03",
            "2026-05-20",
            "(builtin defaults)",
            new[]
            {
                new SheetIssueTargetParameter("issueCode", new[] { "Sheet Issue Code" }),
                new SheetIssueTargetParameter("issueDate", new[] { "Sheet Issue Date" })
            },
            new SheetIssueModelFingerprint("Demo.rvt", @"C:\models\Demo.rvt", "abc123"),
            new SheetIssuePlanSummary(1, 1, 2, 0),
            new[]
            {
                new SheetIssuePlanAction(10, "A-101", "Level 1", null, "issueCode", "Sheet Issue Code", "R02", "R03"),
                new SheetIssuePlanAction(10, "A-101", "Level 1", null, "issueDate", "Sheet Issue Date", "2026-05-01", "2026-05-20")
            },
            Array.Empty<SheetIssuePlanSkipped>(),
            new SheetIssuePlanCommands(
                $"revitcli plan show \"{planPath}\" --output markdown",
                $"revitcli sheets issue-meta --selector all --issue-code R03 --issue-date 2026-05-20 --plan-output \"{planPath}\" --dry-run --output markdown"));
        SheetIssuePlanStore.Save(planPath, plan);
        return planPath;
    }

    private string WriteSampleSheetRenumberPlan()
    {
        var planPath = Path.Combine(_tempDir, "sheet-renumber.plan.json");
        var plan = new SheetRenumberPlan(
            "sheet-renumber-plan.v1",
            "sheet-renumber",
            "sheets renumber",
            "2026-05-20T10:00:00Z",
            "tester",
            true,
            "all",
            @"C:\models\numbering.yml",
            "Sheet Number",
            new SheetIssueModelFingerprint("Demo.rvt", @"C:\models\Demo.rvt", "abc123"),
            new SheetRenumberPlanSummary(2, 2, 2, 2, 0),
            new[]
            {
                new SheetRenumberPlanAction(10, "TMP-001", "Level 1", "Sheet Number", "TMP-001", "A-101", 1, 1),
                new SheetRenumberPlanAction(11, "TMP-002", "Level 2", "Sheet Number", "TMP-002", "A-102", 1, 2)
            },
            Array.Empty<SheetRenumberPlanSkipped>(),
            new SheetRenumberPlanCommands(
                $"revitcli plan show \"{planPath}\" --output markdown",
                $"revitcli sheets renumber --rule \"C:\\models\\numbering.yml\" --selector all --plan-output \"{planPath}\" --dry-run --output markdown",
                $"revitcli plan apply \"{planPath}\" --dry-run",
                $"revitcli plan apply \"{planPath}\" --yes"));
        SheetRenumberPlanStore.Save(planPath, plan);
        return planPath;
    }

    private string WriteSampleRoomNumberingPlan()
    {
        var planPath = Path.Combine(_tempDir, "room-numbering.plan.json");
        var plan = new RoomNumberingPlan(
            "room-numbering-plan.v1",
            "room-numbering",
            "rooms renumber",
            "2026-05-20T10:00:00Z",
            "tester",
            true,
            "all",
            @"C:\models\rooms.yml",
            "Number",
            new RoomNumberingPlanSummary(2, 2, 2, 0),
            new[]
            {
                new RoomNumberingPlanAction(10, "Office", "rooms", "Number", "101", "L1-001", "L1", "L1|Office"),
                new RoomNumberingPlanAction(11, "Lobby", "rooms", "Number", "102", "L1-002", "L1", "L1|Lobby")
            },
            Array.Empty<RoomNumberingPlanSkipped>(),
            new SetPlanCommands
            {
                Show = $"revitcli plan show \"{planPath}\" --output markdown",
                DryRunApply = $"revitcli plan apply \"{planPath}\" --dry-run",
                Apply = $"revitcli plan apply \"{planPath}\" --yes"
            });
        RoomNumberingPlanStore.Save(planPath, plan);
        return planPath;
    }

    private string WriteSampleLinkRepairPlan()
    {
        var planPath = Path.Combine(_tempDir, "link-repair.plan.json");
        var action = SampleLinkRepairOperation();
        var plan = new LinksCommand.LinkRepairPlan(
            "link-repair-plan.v1",
            "2026-05-21T10:00:00Z",
            Path.Combine(_tempDir, "links.yml"),
            planPath,
            true,
            20,
            new LinksCommand.LinkRepairSummary(1, 1, 0, 0),
            new[] { ToLinkRepairAction(action) },
            Array.Empty<LinksCommand.LinkRepairIssue>(),
            new[]
            {
                $"revitcli plan show \"{planPath}\" --output markdown",
                $"revitcli plan apply \"{planPath}\" --dry-run",
                $"revitcli plan apply \"{planPath}\" --yes --max-changes 20"
            });
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, TerminalJsonOptions.PrettyCamel));
        return planPath;
    }

    private string WriteSampleModelMapFixPlan()
    {
        var planPath = Path.Combine(_tempDir, "model-map-fix.plan.json");
        var plan = new ModelCommand.ModelMapFixPlan(
            "model-map-fix-plan.v1",
            "2026-05-21T10:00:00Z",
            Path.Combine(_tempDir, "model-mapping.yml"),
            planPath,
            true,
            "rooms",
            20,
            new ModelCommand.ModelMapFixSummary(1, 1, 0, 0),
            new[]
            {
                new ModelCommand.ModelMapFixAction(
                    5101,
                    "Room 101",
                    "Rooms",
                    "workset",
                    "Interior",
                    "Architecture",
                    true,
                    false,
                    null)
            },
            Array.Empty<ModelCommand.ModelMapIssue>(),
            new[]
            {
                $"revitcli plan show \"{planPath}\" --output markdown",
                $"revitcli plan apply \"{planPath}\" --dry-run",
                $"revitcli plan apply \"{planPath}\" --yes --max-changes 20"
            });
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, TerminalJsonOptions.PrettyCamel));
        return planPath;
    }

    private static LinkRepairOperation SampleLinkRepairOperation() =>
        new()
        {
            LinkId = 4101,
            LinkTypeId = 4201,
            LinkName = "Structural Model",
            TypeName = "Structural Model.rvt",
            OldPath = @"D:\coordination\old-struct.rvt",
            NewPath = @"D:\coordination\new-struct.rvt",
            OldLoaded = false,
            NewLoaded = true,
            OldPathExists = true,
            NewPathExists = true,
            OldPathLastWriteTimeUtc = "2026-05-20T00:00:00Z",
            NewPathLastWriteTimeUtc = "2026-05-21T00:00:00Z",
            OldPathSizeBytes = 1024,
            NewPathSizeBytes = 2048
        };

    private static LinksCommand.LinkRepairAction ToLinkRepairAction(LinkRepairOperation action) =>
        new(
            action.LinkId,
            action.LinkTypeId,
            new[] { action.LinkId },
            action.LinkName,
            action.TypeName,
            action.OldPath,
            action.NewPath,
            action.OldLoaded,
            action.NewLoaded,
            action.OldPathExists,
            action.NewPathExists,
            action.OldPathLastWriteTimeUtc,
            action.NewPathLastWriteTimeUtc,
            action.OldPathSizeBytes,
            action.NewPathSizeBytes);

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

    private static void EnqueueSheetIssueSnapshot(RecordingQueueHttpHandler handler)
    {
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Demo.rvt",
                DocumentPath = @"C:\models\Demo.rvt"
            },
            Model = new SnapshotModel { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters =
                    {
                        ["Sheet Issue Code"] = "R02",
                        ["Sheet Issue Date"] = "2026-05-01"
                    }
                }
            }
        }));
    }

    private static void EnqueueSheetRenumberSnapshot(RecordingQueueHttpHandler handler)
    {
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Demo.rvt",
                DocumentPath = @"C:\models\Demo.rvt"
            },
            Model = new SnapshotModel { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "TMP-001", Name = "Level 1" },
                new SnapshotSheet { ViewId = 11, Number = "TMP-002", Name = "Level 2" },
            }
        }));
    }

    private static void EnqueueRooms(RecordingQueueHttpHandler handler, string firstNumber, string secondNumber)
    {
        handler.Enqueue("/api/elements", ApiResponse<ElementInfo[]>.Ok(new[]
        {
            new ElementInfo
            {
                Id = 10,
                Name = "Office",
                Category = "rooms",
                TypeName = "Room",
                Parameters =
                {
                    ["Number"] = firstNumber,
                    ["Level"] = "L1"
                }
            },
            new ElementInfo
            {
                Id = 11,
                Name = "Lobby",
                Category = "rooms",
                TypeName = "Room",
                Parameters =
                {
                    ["Number"] = secondNumber,
                    ["Level"] = "L1"
                }
            }
        }));
    }

    private static long[] ReadLongArray(JsonElement array)
    {
        return array.EnumerateArray()
            .Select(item => item.GetInt64())
            .ToArray();
    }
}
