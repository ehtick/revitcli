using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Mcp.Tools;

namespace RevitCli.Mcp;

/// <summary>
/// stdio JSON-RPC 2.0 loop implementing the MCP 2024-11-05 server protocol.
///
/// Wire format: newline-delimited JSON, one message per line. This is the
/// transport baseline used by Claude Desktop / Cursor / Continue.
///
/// Scope of this skeleton: `initialize`, `initialized`, `tools/list`,
/// `tools/call`, `ping`. Resources, prompts, sampling and write tools are
/// deliberately out of scope (see roadmap §8 and §9.6).
/// </summary>
internal sealed class McpServer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, IMcpTool> _tools;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _logger;
    private readonly string _serverVersion;

    public McpServer(IEnumerable<IMcpTool> tools, TextReader input, TextWriter output, TextWriter? logger = null, string? serverVersion = null)
    {
        _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);
        foreach (var tool in tools)
            _tools[tool.Name] = tool;
        _input = input;
        _output = output;
        _logger = logger ?? TextWriter.Null;
        _serverVersion = serverVersion ?? GetCliVersion();
    }

    /// <summary>
    /// Convenience wrapper that pulls the same RevitClient the rest of the
    /// CLI uses and binds the three read-only tools.
    /// </summary>
    public static McpServer CreateDefault(RevitClient client, TextReader input, TextWriter output, TextWriter? logger = null)
    {
        var tools = new IMcpTool[]
        {
            new StatusTool(client),
            new QueryTool(client),
            new AuditTool(client),
        };
        return new McpServer(tools, input, output, logger);
    }

    /// <summary>
    /// Run the loop until EOF on the input stream or cancellation.
    /// Always returns 0 — fatal errors should be logged, not crash the host
    /// (Claude Desktop will respawn us anyway).
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            await HandleLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        return 0;
    }

    /// <summary>
    /// Handle a single inbound message. Exposed for tests so they can drive
    /// the dispatcher without standing up a background loop.
    /// </summary>
    internal async Task HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;
        JsonNode? rawId = null;
        try
        {
            // Pull the id out first so even a partially-malformed request can
            // reply with the right id (per JSON-RPC §5.1).
            var raw = JsonNode.Parse(line);
            if (raw is JsonObject obj && obj.TryGetPropertyValue("id", out var idNode))
                rawId = idNode?.DeepClone();
            request = JsonSerializer.Deserialize<JsonRpcRequest>(line);
        }
        catch (JsonException ex)
        {
            await WriteResponseAsync(JsonRpcResponse.Failure(null, McpProtocol.ErrorParseError, $"Parse error: {ex.Message}")).ConfigureAwait(false);
            return;
        }

        if (request is null || string.IsNullOrEmpty(request.Method))
        {
            await WriteResponseAsync(JsonRpcResponse.Failure(rawId, McpProtocol.ErrorInvalidRequest, "Invalid Request")).ConfigureAwait(false);
            return;
        }

        // Notifications (no id) get no response, by spec.
        if (request.IsNotification)
        {
            await HandleNotificationAsync(request).ConfigureAwait(false);
            return;
        }

        try
        {
            var response = await DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            await WriteResponseAsync(response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[mcp] internal error handling {request.Method}: {ex}");
            await WriteResponseAsync(JsonRpcResponse.Failure(request.Id, McpProtocol.ErrorInternalError, ex.Message)).ConfigureAwait(false);
        }
    }

    private Task HandleNotificationAsync(JsonRpcRequest request)
    {
        // We don't act on any notifications today. `notifications/initialized`
        // is the canonical one and a silent ack is correct per spec.
        _logger.WriteLine($"[mcp] notification: {request.Method}");
        return Task.CompletedTask;
    }

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case "initialize":
                return JsonRpcResponse.Success(request.Id, BuildInitializeResult());

            case "initialized": // some clients send as a request rather than notification
            case "ping":
                return JsonRpcResponse.Success(request.Id, new JsonObject());

            case "tools/list":
                return JsonRpcResponse.Success(request.Id, BuildToolsList());

            case "tools/call":
                return await HandleToolCallAsync(request, cancellationToken).ConfigureAwait(false);

            default:
                return JsonRpcResponse.Failure(request.Id, McpProtocol.ErrorMethodNotFound, $"Method not found: {request.Method}");
        }
    }

    private JsonNode BuildInitializeResult()
    {
        var payload = new McpInitializeResult
        {
            ProtocolVersion = McpProtocol.ProtocolVersion,
            Capabilities = new McpServerCapabilities { Tools = new McpToolsCapability { ListChanged = false } },
            ServerInfo = new McpServerInfo { Name = "revitcli", Version = _serverVersion },
        };
        return JsonSerializer.SerializeToNode(payload, WriteOptions)!;
    }

    private JsonNode BuildToolsList()
    {
        var arr = new JsonArray();
        foreach (var tool in _tools.Values)
        {
            arr.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema.DeepClone(),
            });
        }
        return new JsonObject { ["tools"] = arr };
    }

    private async Task<JsonRpcResponse> HandleToolCallAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (request.Params is not JsonObject paramsObj)
            return JsonRpcResponse.Failure(request.Id, McpProtocol.ErrorInvalidParams, "tools/call: missing params object");

        var toolName = paramsObj["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(toolName))
            return JsonRpcResponse.Failure(request.Id, McpProtocol.ErrorInvalidParams, "tools/call: missing 'name'");

        if (!_tools.TryGetValue(toolName, out var tool))
            return JsonRpcResponse.Failure(request.Id, McpProtocol.ErrorInvalidParams, $"tools/call: unknown tool '{toolName}'");

        var arguments = paramsObj["arguments"];

        string text;
        bool isError = false;
        try
        {
            text = await tool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Tool-level failure: surface to LLM as content with isError, not
            // as JSON-RPC error (per MCP spec — RPC errors mean protocol bugs).
            _logger.WriteLine($"[mcp] tool '{toolName}' threw: {ex}");
            text = $"Tool '{toolName}' failed: {ex.Message}";
            isError = true;
        }

        var result = new McpToolCallResult
        {
            Content = new List<McpContentBlock> { McpContentBlock.TextBlock(text) },
            IsError = isError,
        };
        return JsonRpcResponse.Success(request.Id, JsonSerializer.SerializeToNode(result, WriteOptions)!);
    }

    private async Task WriteResponseAsync(JsonRpcResponse response)
    {
        var json = JsonSerializer.Serialize(response, WriteOptions);
        await _output.WriteLineAsync(json).ConfigureAwait(false);
        await _output.FlushAsync().ConfigureAwait(false);
    }

    private static string GetCliVersion()
    {
        var attr = typeof(McpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? "0.0.0";
    }
}
