using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Mcp.Tools;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Mcp;

public class McpToolsTests
{
    [Fact]
    public async Task QueryTool_NoCategoryNoId_ReturnsError()
    {
        var client = new RevitClient(new HttpClient(new QueueHttpHandler()) { BaseAddress = new Uri("http://localhost:17839") });
        var tool = new QueryTool(client);

        var text = await tool.ExecuteAsync(new JsonObject(), CancellationToken.None);

        Assert.Contains("provide a category or id", text);
    }

    [Fact]
    public async Task QueryTool_RespectsLimit()
    {
        var handler = new QueueHttpHandler();
        var elements = new List<ElementInfo>();
        for (int i = 0; i < 50; i++)
            elements.Add(new ElementInfo { Id = i, Name = $"Wall-{i}", Category = "Walls" });
        handler.Enqueue("/api/elements", ApiResponse<ElementInfo[]>.Ok(elements.ToArray()));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var tool = new QueryTool(client);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["category"] = "walls",
            ["limit"] = 5,
        }, CancellationToken.None);

        Assert.Contains("\"count\": 5", text);
        Assert.Contains("\"truncated\": true", text);
    }

    [Fact]
    public async Task AuditTool_UnknownRule_ReturnsErrorWithoutCallingClient()
    {
        var handler = new QueueHttpHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var tool = new AuditTool(client);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["rules"] = new JsonArray("not-a-real-rule"),
        }, CancellationToken.None);

        Assert.Contains("unknown rule", text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuditTool_AcceptsCommaSeparatedString()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/audit", ApiResponse<AuditResult>.Ok(new AuditResult { Passed = 1, Failed = 0 }));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var tool = new AuditTool(client);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["rules"] = "naming, room-bounds",
        }, CancellationToken.None);

        Assert.Contains("1 passed", text);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void StatusTool_SchemaIsObjectWithNoRequiredArgs()
    {
        var client = new RevitClient(new HttpClient(new QueueHttpHandler()) { BaseAddress = new Uri("http://localhost:17839") });
        var tool = new StatusTool(client);

        var schema = tool.InputSchema.AsObject();
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.NotNull(schema["properties"]);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }
}
