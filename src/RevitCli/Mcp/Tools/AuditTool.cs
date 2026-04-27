using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;

namespace RevitCli.Mcp.Tools;

/// <summary>
/// MCP wrapper around <see cref="RevitClient.AuditAsync"/>. Read-only.
/// </summary>
internal sealed class AuditTool : IMcpTool
{
    private readonly RevitClient _client;

    public AuditTool(RevitClient client)
    {
        _client = client;
    }

    public string Name => "audit";

    public string Description =>
        "Run the built-in audit rules against the active Revit model. " +
        "Optionally pass `rules` as a list of rule names to limit the run. " +
        "Returns a summary plus one line per issue found.";

    public JsonNode InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["rules"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] =
                    "Subset of rules to run. Valid: " + string.Join(", ", AuditCommand.AvailableRules) +
                    ". If omitted, all rules run.",
            },
        },
        ["additionalProperties"] = false,
    };

    public async Task<string> ExecuteAsync(JsonNode? arguments, CancellationToken cancellationToken)
    {
        var args = arguments as JsonObject ?? new JsonObject();
        var rules = ParseRules(args);

        var invalid = rules.Where(r => !AuditCommand.AvailableRules.Contains(r)).ToList();
        if (invalid.Count > 0)
        {
            return $"Error: unknown rule(s): {string.Join(", ", invalid)}. " +
                $"Available: {string.Join(", ", AuditCommand.AvailableRules)}";
        }

        var ruleList = rules.Count > 0 ? rules : AuditCommand.AvailableRules.ToList();
        var request = new AuditRequest { Rules = ruleList };
        var result = await _client.AuditAsync(request);

        if (!result.Success)
            return $"Error: {result.Error}";

        var data = result.Data!;
        var sb = new StringBuilder();
        sb.AppendLine($"Audit complete: {data.Passed} passed, {data.Failed} failed");
        foreach (var issue in data.Issues)
        {
            var prefix = issue.Severity switch
            {
                "error" => "ERROR",
                "warning" => "WARN",
                _ => "INFO",
            };
            var elementRef = issue.ElementId.HasValue ? $" [Element {issue.ElementId}]" : "";
            sb.AppendLine($"  [{prefix}] {issue.Rule}: {issue.Message}{elementRef}");
        }
        return sb.ToString().TrimEnd();
    }

    private static List<string> ParseRules(JsonObject args)
    {
        var list = new List<string>();
        if (!args.TryGetPropertyValue("rules", out var node) || node is null)
            return list;

        if (node is JsonArray arr)
        {
            foreach (var entry in arr)
            {
                if (entry is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    list.Add(s.Trim());
            }
        }
        else if (node is JsonValue val && val.TryGetValue<string>(out var raw))
        {
            // Be lenient: accept comma-separated string for hand-rolled clients.
            list.AddRange(raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries));
        }
        return list;
    }
}
