using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;

namespace RevitCli.Mcp.Tools;

/// <summary>
/// MCP wrapper around <see cref="RevitClient.CaptureSnapshotAsync"/>. Read-only.
///
/// Exposed as a tool (in addition to the snapshot/latest resource) so LLMs
/// can opt in to a category-filtered, full-fidelity capture without taking
/// the summary-only shortcut.
/// </summary>
internal sealed class SnapshotTool : IMcpTool
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly RevitClient _client;

    public SnapshotTool(RevitClient client)
    {
        _client = client;
    }

    public string Name => "snapshot";

    public string Description =>
        "Capture a snapshot of the active Revit model. Set `summaryOnly` for " +
        "lightweight metrics, or pass `categories` to limit the capture to " +
        "specific element categories.";

    public JsonNode InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["summaryOnly"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Skip per-element data; only return summary metrics.",
                ["default"] = false,
            },
            ["categories"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Limit the capture to these categories (e.g. [\"Walls\", \"Doors\"]).",
            },
        },
        ["additionalProperties"] = false,
    };

    public async Task<string> ExecuteAsync(JsonNode? arguments, CancellationToken cancellationToken)
    {
        var args = arguments as JsonObject ?? new JsonObject();
        var request = new SnapshotRequest
        {
            SummaryOnly = TryGetBool(args, "summaryOnly") ?? false,
            IncludeCategories = TryGetStringArray(args, "categories"),
        };

        var result = await _client.CaptureSnapshotAsync(request);
        if (!result.Success)
            return $"Error: {result.Error}";

        return JsonSerializer.Serialize(result.Data, PrettyJson);
    }

    private static bool? TryGetBool(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        return null;
    }

    private static List<string>? TryGetStringArray(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is not JsonArray arr) return null;
        var list = arr
            .OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var s) ? s : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();
        return list.Count == 0 ? null : list;
    }
}
