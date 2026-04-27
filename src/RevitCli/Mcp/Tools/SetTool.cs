using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;

namespace RevitCli.Mcp.Tools;

/// <summary>
/// MCP wrapper around <see cref="RevitClient.SetParameterAsync"/>. Mutating —
/// gated behind two layers of consent:
///
/// <list type="number">
///   <item>The CLI must have been started with <c>mcp serve --allow-writes</c>.
///         Without that flag the tool returns a refusal text.</item>
///   <item>Each invocation must include <c>confirm: true</c> in its arguments.
///         Without that the tool refuses.</item>
/// </list>
///
/// Both checks are performed by this class — the dispatcher does not need
/// to know about the gate. Defense in depth for LLM-driven model writes.
/// </summary>
internal sealed class SetTool : IMcpTool
{
    /// <summary>Refusal text when the server is in read-only mode.</summary>
    public const string DisabledMessage =
        "Write tools are disabled. Restart `mcp serve` with --allow-writes.";

    /// <summary>Refusal text when the caller forgot to pass <c>confirm: true</c>.</summary>
    public const string ConfirmMessage =
        "This is a write operation. Re-issue with confirm: true to proceed.";

    private readonly RevitClient _client;
    private readonly bool _allowWrites;

    public SetTool(RevitClient client, bool allowWrites)
    {
        _client = client;
        _allowWrites = allowWrites;
    }

    public string Name => "set";

    public string Description =>
        "Write a parameter value on one or more elements. Requires the server " +
        "to have been started with --allow-writes AND the request to include " +
        "`confirm: true`. Supports `dryRun` for preview.";

    public JsonNode InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["category"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Element category, e.g. \"walls\".",
            },
            ["elementId"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Single element to update.",
            },
            ["elementIds"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "integer" },
                ["description"] = "Multiple elements to update.",
            },
            ["filter"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Filter expression evaluated against the category.",
            },
            ["param"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Parameter name to set.",
            },
            ["value"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "New value (string-coerced server-side).",
            },
            ["dryRun"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Preview the change without committing.",
                ["default"] = false,
            },
            ["confirm"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Required safety gate — must be true to execute.",
                ["default"] = false,
            },
        },
        ["required"] = new JsonArray("param", "value", "confirm"),
        ["additionalProperties"] = false,
    };

    public async Task<string> ExecuteAsync(JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!_allowWrites)
            return DisabledMessage;

        var args = arguments as JsonObject ?? new JsonObject();
        if (TryGetBool(args, "confirm") != true)
            return ConfirmMessage;

        var request = new SetRequest
        {
            Category = TryGetString(args, "category"),
            ElementId = TryGetLong(args, "elementId"),
            ElementIds = TryGetLongArray(args, "elementIds"),
            Filter = TryGetString(args, "filter"),
            Param = TryGetString(args, "param") ?? "",
            Value = TryGetString(args, "value") ?? "",
            DryRun = TryGetBool(args, "dryRun") ?? false,
        };

        if (string.IsNullOrEmpty(request.Param))
            return "Error: 'param' is required.";

        var result = await _client.SetParameterAsync(request);
        if (!result.Success)
            return $"Error: {result.Error}";

        var data = result.Data!;
        var sb = new StringBuilder();
        sb.AppendLine(request.DryRun
            ? $"Dry run — would update {data.Affected} element(s)."
            : $"Updated {data.Affected} element(s).");
        foreach (var item in data.Preview)
        {
            sb.AppendLine($"  [{item.Id}] {item.Name}: {item.OldValue ?? "(null)"} -> {item.NewValue}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string? TryGetString(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
            return s;
        return null;
    }

    private static long? TryGetLong(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<string>(out var s) && long.TryParse(s, out var parsed)) return parsed;
        }
        return null;
    }

    private static bool? TryGetBool(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        return null;
    }

    private static List<long>? TryGetLongArray(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is not JsonArray arr) return null;
        var list = new List<long>();
        foreach (var entry in arr)
        {
            if (entry is JsonValue v)
            {
                if (v.TryGetValue<long>(out var l)) list.Add(l);
                else if (v.TryGetValue<int>(out var i)) list.Add(i);
            }
        }
        return list.Count == 0 ? null : list;
    }
}
