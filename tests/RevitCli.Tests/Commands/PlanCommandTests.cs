using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
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
    public async Task Show_Json_PrintsPlanFile()
    {
        var planPath = WriteSamplePlan();
        var writer = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("set", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("affected").GetInt32());
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
        Assert.Contains("Applied plan", writer.ToString());
        Assert.Contains("Receipt saved", writer.ToString());
        Assert.Contains("\"elementIds\":[100,200]", handler.RequestBodies[0]);
        Assert.Contains("\"dryRun\":false", handler.RequestBodies[0]);
        Assert.True(File.Exists(receiptPath));
        Assert.Contains("\"action\":\"plan.apply\"", File.ReadAllText(receiptPath).Replace(" ", "", StringComparison.Ordinal));
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

    private static List<SetPreviewItem> SamplePreview()
    {
        return new List<SetPreviewItem>
        {
            new() { Id = 100, Name = "Door 1", OldValue = "30min", NewValue = "60min" },
            new() { Id = 200, Name = "Door 2", OldValue = "30min", NewValue = "60min" }
        };
    }

    private static RevitClient MakeClient(HttpMessageHandler handler)
    {
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }
}
