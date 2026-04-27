using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RevitCli.Mcp;

/// <summary>
/// JSON-RPC 2.0 + MCP 2024-11-05 wire types.
///
/// Spec: https://spec.modelcontextprotocol.io/specification/2024-11-05/
///
/// Only the subset we actually serve is modelled here. Tools/resources/etc.
/// not in scope for this round are intentionally omitted.
///
/// Notes for implementers:
///  - JSON-RPC ids may be string, number, or null. We keep them as JsonNode
///    to round-trip whatever the client sent without lossy conversion.
///  - Notifications have no id and never get a response.
/// </summary>
internal static class McpProtocol
{
    public const string JsonRpcVersion = "2.0";
    public const string ProtocolVersion = "2024-11-05";

    // Standard JSON-RPC error codes.
    public const int ErrorParseError = -32700;
    public const int ErrorInvalidRequest = -32600;
    public const int ErrorMethodNotFound = -32601;
    public const int ErrorInvalidParams = -32602;
    public const int ErrorInternalError = -32603;
}

internal sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = McpProtocol.JsonRpcVersion;

    [JsonPropertyName("id")]
    public JsonNode? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonNode? Params { get; set; }

    [JsonIgnore]
    public bool IsNotification => Id is null;
}

internal sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = McpProtocol.JsonRpcVersion;

    [JsonPropertyName("id")]
    public JsonNode? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse Success(JsonNode? id, JsonNode? result) =>
        new() { Id = id, Result = result ?? new JsonObject() };

    public static JsonRpcResponse Failure(JsonNode? id, int code, string message, JsonNode? data = null) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
}

internal sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Data { get; set; }
}

/// <summary>
/// MCP `initialize` result payload.
/// </summary>
internal sealed class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = McpProtocol.ProtocolVersion;

    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public McpServerInfo ServerInfo { get; set; } = new();
}

internal sealed class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public McpToolsCapability Tools { get; set; } = new();

    /// <summary>
    /// Resources capability. Null when the server does not expose any
    /// resources. Serialised when present so clients see the right
    /// surface from the very first `initialize` round-trip.
    /// </summary>
    [JsonPropertyName("resources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpResourcesCapability? Resources { get; set; }
}

internal sealed class McpToolsCapability
{
    /// <summary>
    /// Whether the server emits `notifications/tools/list_changed`.
    /// We don't, so this is always false.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

internal sealed class McpResourcesCapability
{
    /// <summary>
    /// Whether the server supports `resources/subscribe`. We do not — every
    /// resource is read on demand.
    /// </summary>
    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; }

    /// <summary>
    /// Whether the server emits `notifications/resources/list_changed`. We
    /// don't (the URI set is fixed for the lifetime of one process).
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

internal sealed class McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "revitcli";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.0";
}

/// <summary>
/// One entry in the `tools/list` response.
/// </summary>
internal sealed class McpToolDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputSchema")]
    public JsonNode InputSchema { get; set; } = new JsonObject();
}

/// <summary>
/// `tools/call` request params.
/// </summary>
internal sealed class McpToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public JsonNode? Arguments { get; set; }
}

/// <summary>
/// `tools/call` result. `content` is required, `isError` is optional and
/// signals tool-level (not protocol-level) failure to the LLM client.
/// </summary>
internal sealed class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpContentBlock> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; set; }
}

internal sealed class McpContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    public static McpContentBlock TextBlock(string text) => new() { Type = "text", Text = text };
}

/// <summary>
/// One entry in the `resources/list` response. Mirrors the spec shape:
/// `uri` is the canonical identifier the LLM uses with `resources/read`,
/// `mimeType` lets clients pick a renderer.
/// </summary>
internal sealed class McpResourceDescriptor
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "text/plain";
}

/// <summary>
/// `resources/read` response wrapper. The spec allows multiple content
/// chunks per URI; we emit exactly one today (text or blob, never both).
/// </summary>
internal sealed class McpResourceReadResult
{
    [JsonPropertyName("contents")]
    public List<McpResourceContents> Contents { get; set; } = new();
}

internal sealed class McpResourceContents
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "text/plain";

    /// <summary>
    /// Inline text payload. Mutually exclusive with <see cref="Blob"/> per spec.
    /// </summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>
    /// Base64-encoded binary payload. Reserved for future use; resources
    /// in this round only emit text.
    /// </summary>
    [JsonPropertyName("blob")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Blob { get; set; }
}
