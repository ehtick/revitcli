using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Mcp;
using RevitCli.Mcp.Tools;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Mcp;

public class McpServerTests
{
    [Fact]
    public async Task Initialize_ReturnsServerInfoAndProtocolVersion()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        await server.HandleLineAsync(Request(1, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0.0" },
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, response["id"]!.GetValue<int>());

        var result = response["result"]!;
        Assert.Equal("2024-11-05", result["protocolVersion"]!.GetValue<string>());
        Assert.Equal("revitcli", result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(result["serverInfo"]!["version"]!.GetValue<string>()));
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task ToolsList_ReturnsAllThreeReadOnlyTools()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        await server.HandleLineAsync(Request(2, "tools/list"), CancellationToken.None);

        var response = ReadOneResponse(output);
        var tools = response["result"]!["tools"]!.AsArray();
        Assert.Equal(3, tools.Count);

        var byName = new Dictionary<string, JsonNode>();
        foreach (var t in tools)
            byName[t!["name"]!.GetValue<string>()] = t!;

        Assert.Contains("status", byName.Keys);
        Assert.Contains("query", byName.Keys);
        Assert.Contains("audit", byName.Keys);

        foreach (var (name, tool) in byName)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool["description"]!.GetValue<string>()), $"{name} description empty");
            var schema = tool["inputSchema"]!;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.NotNull(schema["properties"]);
        }
    }

    [Fact]
    public async Task ToolsCall_Status_InvokesClientAndReturnsTextContent()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/status", ApiResponse<StatusInfo>.Ok(new StatusInfo
        {
            RevitVersion = "2026",
            DocumentName = "Project1.rvt",
            DocumentPath = @"C:\models\Project1.rvt",
        }));
        var server = BuildServerWithStubClient(handler, out _, out var output);

        await server.HandleLineAsync(Request(3, "tools/call", new JsonObject
        {
            ["name"] = "status",
            ["arguments"] = new JsonObject(),
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var result = response["result"]!;
        var content = result["content"]!.AsArray();
        Assert.Single(content);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        var text = content[0]!["text"]!.GetValue<string>();
        Assert.Contains("2026", text);
        Assert.Contains("Project1.rvt", text);
        // Successful call must not set isError true (we omit-on-default).
        var isErr = result["isError"];
        Assert.True(isErr is null || isErr.GetValue<bool>() == false);
        Assert.Contains("/api/status", handler.Requests);
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        await server.HandleLineAsync(Request(4, "totally/made_up"), CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.Null(response["result"]);
        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("Method not found", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task MalformedJson_ReturnsParseError()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        await server.HandleLineAsync("{not valid json", CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.Equal(-32700, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("Parse error", response["error"]!["message"]!.GetValue<string>());
        // Per JSON-RPC §5.1: id MUST be null for parse errors.
        var id = response["id"];
        Assert.True(id is null || id is JsonValue v && v.GetValueKind() == JsonValueKind.Null);
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsInvalidParams()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        await server.HandleLineAsync(Request(5, "tools/call", new JsonObject
        {
            ["name"] = "does-not-exist",
            ["arguments"] = new JsonObject(),
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("does-not-exist", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializedNotification_DoesNotProduceResponse()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        // No id => notification.
        var line = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { },
        });
        await server.HandleLineAsync(line, CancellationToken.None);

        Assert.Equal(string.Empty, output.ToString().Trim());
    }

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        var server = BuildServerWithStubClient(new QueueHttpHandler(), out _, out var output);

        await server.HandleLineAsync(Request(6, "ping"), CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.NotNull(response["result"]);
        Assert.Null(response["error"]);
    }

    [Fact]
    public async Task ToolsCall_Audit_FormatsIssueList()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/audit", ApiResponse<AuditResult>.Ok(new AuditResult
        {
            Passed = 5,
            Failed = 2,
            Issues = new List<AuditIssue>
            {
                new() { Rule = "naming", Severity = "error", Message = "Bad name", ElementId = 42 },
                new() { Rule = "room-bounds", Severity = "warning", Message = "Unbounded room" },
            },
        }));
        var server = BuildServerWithStubClient(handler, out _, out var output);

        await server.HandleLineAsync(Request(7, "tools/call", new JsonObject
        {
            ["name"] = "audit",
            ["arguments"] = new JsonObject { ["rules"] = new JsonArray("naming", "room-bounds") },
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Contains("5 passed", text);
        Assert.Contains("2 failed", text);
        Assert.Contains("ERROR", text);
        Assert.Contains("Element 42", text);
    }

    [Fact]
    public async Task ToolsCall_Query_ById_ReturnsJsonText()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/elements/123", ApiResponse<ElementInfo>.Ok(new ElementInfo
        {
            Id = 123,
            Name = "Door-A",
            Category = "Doors",
            TypeName = "Single-Flush",
        }));
        var server = BuildServerWithStubClient(handler, out _, out var output);

        await server.HandleLineAsync(Request(8, "tools/call", new JsonObject
        {
            ["name"] = "query",
            ["arguments"] = new JsonObject { ["id"] = 123 },
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Contains("\"id\": 123", text);
        Assert.Contains("Door-A", text);
    }

    // -- helpers ------------------------------------------------------------

    private static McpServer BuildServerWithStubClient(QueueHttpHandler handler, out StringReader input, out StringWriter output)
    {
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        input = new StringReader(string.Empty);
        output = new StringWriter();
        return McpServer.CreateDefault(client, input, output, TextWriter.Null);
    }

    private static string Request(int id, string method, JsonNode? @params = null)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (@params is not null) obj["params"] = @params;
        return obj.ToJsonString();
    }

    private static JsonNode ReadOneResponse(StringWriter output)
    {
        var raw = output.ToString().Trim();
        Assert.False(string.IsNullOrEmpty(raw), "expected one response on the writer");
        // Only one newline-delimited message expected per HandleLineAsync call.
        var line = raw.Split('\n', 2)[0].TrimEnd('\r');
        return JsonNode.Parse(line)!;
    }
}

internal sealed class QueueHttpHandler : HttpMessageHandler
{
    private readonly Queue<(string Path, string Json)> _responses = new();
    public List<string> Requests { get; } = new();

    public void Enqueue<T>(string path, ApiResponse<T> response)
    {
        _responses.Enqueue((path, JsonSerializer.Serialize(response)));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsolutePath);
        var next = _responses.Dequeue();
        Assert.Equal(next.Path, request.RequestUri.AbsolutePath);
        if (request.Content != null)
            _ = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(next.Json, Encoding.UTF8, "application/json")
        };
    }
}
