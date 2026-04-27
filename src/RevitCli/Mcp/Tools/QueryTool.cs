using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;

namespace RevitCli.Mcp.Tools;

/// <summary>
/// MCP wrapper around <see cref="RevitClient.QueryElementsAsync"/> /
/// <see cref="RevitClient.QueryElementByIdAsync"/>. Read-only.
/// </summary>
internal sealed class QueryTool : IMcpTool
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 500;

    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly RevitClient _client;

    public QueryTool(RevitClient client)
    {
        _client = client;
    }

    public string Name => "query";

    public string Description =>
        "Query elements from the active Revit model. Provide either an `id` to look up a " +
        "single element, or a `category` (optionally with a `filter` expression) to list " +
        "matching elements. Results are returned as JSON, capped by `limit`.";

    public JsonNode InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["category"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Element category, e.g. \"walls\", \"doors\", \"windows\".",
            },
            ["filter"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Filter expression, e.g. \"height > 3000\".",
            },
            ["id"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Look up a single element by Revit ElementId.",
            },
            ["limit"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = $"Max elements returned when listing by category. Default {DefaultLimit}, max {MaxLimit}.",
                ["minimum"] = 1,
                ["maximum"] = MaxLimit,
                ["default"] = DefaultLimit,
            },
        },
        ["additionalProperties"] = false,
    };

    public async Task<string> ExecuteAsync(JsonNode? arguments, CancellationToken cancellationToken)
    {
        var args = arguments as JsonObject ?? new JsonObject();

        long? id = TryGetLong(args, "id");
        string? category = TryGetString(args, "category");
        string? filter = TryGetString(args, "filter");
        int limit = TryGetInt(args, "limit") ?? DefaultLimit;
        if (limit < 1) limit = 1;
        if (limit > MaxLimit) limit = MaxLimit;

        if (id.HasValue)
        {
            var single = await _client.QueryElementByIdAsync(id.Value);
            if (!single.Success)
                return $"Error: {single.Error}";
            return JsonSerializer.Serialize(single.Data, PrettyJson);
        }

        if (string.IsNullOrWhiteSpace(category))
            return "Error: provide a category or id.";

        var listing = await _client.QueryElementsAsync(category, filter);
        if (!listing.Success)
            return $"Error: {listing.Error}";

        var elements = listing.Data ?? Array.Empty<ElementInfo>();
        var truncated = elements.Take(limit).ToArray();
        var payload = new
        {
            count = truncated.Length,
            totalReturnedByServer = elements.Length,
            truncated = elements.Length > truncated.Length,
            elements = truncated,
        };
        return JsonSerializer.Serialize(payload, PrettyJson);
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

    private static int? TryGetInt(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        }
        return null;
    }
}
