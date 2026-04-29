using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Plans;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class SetCommandTests
{
    [Fact]
    public async Task Execute_DryRun_PrintsPreview()
    {
        var setResult = new SetResult
        {
            Affected = 2,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 100, Name = "Door 1", OldValue = "30min", NewValue = "60min" },
                new() { Id = 200, Name = "Door 2", OldValue = "30min", NewValue = "60min" }
            }
        };
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, "Fire Rating", "60min", true, false, null, writer);

        var output = writer.ToString();
        Assert.Contains("2 element(s)", output);
        Assert.Contains("Door 1", output);
        Assert.Contains("30min", output);
        Assert.Contains("60min", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Execute_PlanOutput_WritesFrozenSetPlan()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"set_plan_{Guid.NewGuid():N}.json");
        try
        {
            var setResult = new SetResult
            {
                Affected = 2,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 200, Name = "Door 2", OldValue = "30min", NewValue = "60min" },
                    new() { Id = 100, Name = "Door 1", OldValue = "30min", NewValue = "60min" }
                }
            };
            var response = ApiResponse<SetResult>.Ok(setResult);
            var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exitCode = await SetCommand.ExecuteAsync(
                client,
                "doors",
                "name contains Fire",
                null,
                "Fire Rating",
                "60min",
                dryRun: false,
                fromStdin: false,
                idsFromFile: null,
                writer,
                planOutputPath: tmpFile);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(tmpFile));
            Assert.Contains("\"dryRun\":true", handler.LastRequestBody);
            Assert.Contains("Plan written", writer.ToString());
            Assert.Contains("revitcli plan apply", writer.ToString());

            var plan = SetPlanFileStore.Load(tmpFile);
            Assert.Equal("set", plan.Type);
            Assert.Equal(2, plan.Summary.Affected);
            Assert.Equal("category=doors, filter=name contains Fire", plan.Summary.OriginalTarget);
            Assert.Equal(new[] { 100L, 200L }, plan.ApplyRequest.ElementIds);
            Assert.Null(plan.ApplyRequest.Category);
            Assert.Null(plan.ApplyRequest.Filter);
            Assert.Equal("Fire Rating", plan.ApplyRequest.Param);
            Assert.False(plan.ApplyRequest.DryRun);
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task Execute_MissingParam_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, null!, "60min", false, false, null, writer);

        Assert.Contains("--param", writer.ToString().ToLower());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void SetRequest_ElementIds_SerializesCorrectly()
    {
        var request = new SetRequest
        {
            Param = "Mark",
            Value = "TEST",
            ElementIds = new List<long> { 337596, 337601 }
        };

        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"elementIds\":[337596,337601]", json);

        var deserialized = JsonSerializer.Deserialize<SetRequest>(json);
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.ElementIds);
        Assert.Equal(2, deserialized.ElementIds!.Count);
        Assert.Equal(337596, deserialized.ElementIds[0]);
        Assert.Equal(337601, deserialized.ElementIds[1]);

        // Category and ElementId should be null
        Assert.Null(deserialized.Category);
        Assert.Null(deserialized.ElementId);
    }

    [Fact]
    public async Task IdsFrom_JsonArray_WorksEndToEnd()
    {
        // Write query-style JSON to temp file
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(tmpFile, @"[{""id"": 337596, ""name"": ""Wall 1""}, {""id"": 337601, ""name"": ""Wall 2""}]");

        var setResult = new SetResult { Affected = 2, Preview = new List<SetPreviewItem>
        {
            new() { Id = 337596, Name = "Wall 1", OldValue = "", NewValue = "TEST" },
            new() { Id = 337601, Name = "Wall 2", OldValue = "", NewValue = "TEST" }
        }};
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "TEST", true, false, tmpFile, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("2 element(s)", writer.ToString());
        // Verify the request body contains elementIds
        Assert.Contains("337596", handler.LastRequestBody);
        Assert.Contains("337601", handler.LastRequestBody);
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task IdsFrom_PlainText_WorksEndToEnd()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, "100\n200\n300\n");

        var setResult = new SetResult { Affected = 3 };
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "X", false, false, tmpFile, writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("100", handler.LastRequestBody);
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task IdsFrom_MalformedItem_ReturnsError()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, "100\nnot_a_number\n300\n");

        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "X", false, false, tmpFile, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not_a_number", writer.ToString());
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task IdsFrom_MixedWithCategory_ReturnsError()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, "100\n");

        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        // --ids-from + category should be rejected
        var exitCode = await SetCommand.ExecuteAsync(client, "walls", null, null, "Mark", "X", false, false, tmpFile, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be combined", writer.ToString().ToLower());
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task Execute_NoTarget_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "W-01", false, false, null, writer);

        Assert.Contains("category", writer.ToString().ToLower());
        Assert.Equal(1, exitCode);
    }
}
