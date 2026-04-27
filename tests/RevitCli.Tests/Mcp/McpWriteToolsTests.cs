using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Mcp;
using RevitCli.Mcp.Tools;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Mcp;

/// <summary>
/// Two-layer safety gate for the MCP <c>set</c> tool:
///  - Server must be started with <c>--allow-writes</c>.
///  - Each request must include <c>confirm: true</c>.
///
/// Design choice: the <c>set</c> tool is ALWAYS registered (so the schema is
/// discoverable via <c>tools/list</c> regardless of the gate). When the gate
/// is closed, the tool returns a refusal text rather than 404'ing — that way
/// the LLM gets a contextual hint about how to enable it.
/// </summary>
public class McpWriteToolsTests
{
    [Fact]
    public async Task SetTool_AllowWritesOff_ReturnsDisabledMessage()
    {
        var handler = new QueueHttpHandler();
        var tool = new SetTool(MakeClient(handler), allowWrites: false);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["param"] = "Comments",
            ["value"] = "x",
            ["confirm"] = true,
            ["elementId"] = 1,
        }, CancellationToken.None);

        Assert.Equal(SetTool.DisabledMessage, text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SetTool_AllowWritesOn_NoConfirm_ReturnsConfirmMessage()
    {
        var handler = new QueueHttpHandler();
        var tool = new SetTool(MakeClient(handler), allowWrites: true);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["param"] = "Comments",
            ["value"] = "x",
            ["elementId"] = 1,
        }, CancellationToken.None);

        Assert.Equal(SetTool.ConfirmMessage, text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SetTool_AllowWritesOn_ConfirmFalse_ReturnsConfirmMessage()
    {
        var handler = new QueueHttpHandler();
        var tool = new SetTool(MakeClient(handler), allowWrites: true);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["param"] = "Comments",
            ["value"] = "x",
            ["confirm"] = false,
            ["elementId"] = 1,
        }, CancellationToken.None);

        Assert.Equal(SetTool.ConfirmMessage, text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SetTool_AllowWritesOn_WithConfirm_InvokesClient()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 42, Name = "Wall-A", OldValue = "old", NewValue = "new-val" },
            },
        }));
        var tool = new SetTool(MakeClient(handler), allowWrites: true);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["param"] = "Comments",
            ["value"] = "new-val",
            ["confirm"] = true,
            ["elementId"] = 42,
        }, CancellationToken.None);

        Assert.Contains("Updated 1 element", text);
        Assert.Contains("Wall-A", text);
        Assert.Contains("old -> new-val", text);
        Assert.Contains("/api/elements/set", handler.Requests);
    }

    [Fact]
    public async Task SetTool_DryRunWithConfirm_InvokesClientAndReportsPreview()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 3,
            Preview = new List<SetPreviewItem>(),
        }));
        var tool = new SetTool(MakeClient(handler), allowWrites: true);

        var text = await tool.ExecuteAsync(new JsonObject
        {
            ["param"] = "Mark",
            ["value"] = "M-001",
            ["confirm"] = true,
            ["dryRun"] = true,
            ["category"] = "walls",
        }, CancellationToken.None);

        Assert.Contains("Dry run", text);
        Assert.Contains("3 element", text);
    }

    [Fact]
    public void SetTool_SchemaAdvertisesConfirmAsRequired()
    {
        var tool = new SetTool(MakeClient(new QueueHttpHandler()), allowWrites: true);

        var schema = tool.InputSchema.AsObject();
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        var required = schema["required"]!.AsArray();
        var names = new HashSet<string>();
        foreach (var n in required) names.Add(n!.GetValue<string>());
        Assert.Contains("confirm", names);
        Assert.Contains("param", names);
        Assert.Contains("value", names);
    }

    [Fact]
    public async Task ToolsList_AlwaysIncludesSetEvenWhenWritesDisabled()
    {
        // Design choice (documented in this test): the `set` tool is registered
        // regardless of --allow-writes so its schema is always discoverable.
        // The gate is enforced inside the tool, not by the registry.
        var (server, _) = BuildServer(allowWrites: false, out var output);

        await server.HandleLineAsync(Request(1, "tools/list"), CancellationToken.None);

        var response = ReadOneResponse(output);
        var tools = response["result"]!["tools"]!.AsArray();
        var names = new HashSet<string>();
        foreach (var t in tools) names.Add(t!["name"]!.GetValue<string>());
        Assert.Contains("set", names);
        Assert.Contains("snapshot", names);
    }

    [Fact]
    public async Task ToolsCall_Set_AllowWritesOff_ReturnsRefusalAsContent()
    {
        var (server, handler) = BuildServer(allowWrites: false, out var output);

        await server.HandleLineAsync(Request(2, "tools/call", new JsonObject
        {
            ["name"] = "set",
            ["arguments"] = new JsonObject
            {
                ["param"] = "Comments",
                ["value"] = "x",
                ["confirm"] = true,
                ["elementId"] = 1,
            },
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Equal(SetTool.DisabledMessage, text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ToolsCall_Set_AllowWritesOn_WithConfirm_RoundTripsThroughDispatcher()
    {
        var (server, handler) = BuildServer(allowWrites: true, out var output);
        handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 99, Name = "Door-Z", OldValue = "a", NewValue = "b" },
            },
        }));

        await server.HandleLineAsync(Request(3, "tools/call", new JsonObject
        {
            ["name"] = "set",
            ["arguments"] = new JsonObject
            {
                ["param"] = "Mark",
                ["value"] = "b",
                ["confirm"] = true,
                ["elementId"] = 99,
            },
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Contains("Updated 1 element", text);
        Assert.Contains("Door-Z", text);
        Assert.Contains("/api/elements/set", handler.Requests);
    }

    // -- helpers ------------------------------------------------------------

    private static (McpServer Server, QueueHttpHandler Handler) BuildServer(bool allowWrites, out StringWriter output)
    {
        var handler = new QueueHttpHandler();
        var client = MakeClient(handler);
        var input = new StringReader(string.Empty);
        output = new StringWriter();
        // Use the production CreateDefault so we exercise the real wiring.
        var server = McpServer.CreateDefault(client, input, output, TextWriter.Null, allowWrites);
        return (server, handler);
    }

    private static RevitClient MakeClient(QueueHttpHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });

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
        var line = raw.Split('\n', 2)[0].TrimEnd('\r');
        return JsonNode.Parse(line)!;
    }
}
