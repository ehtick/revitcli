using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;

namespace RevitCli.Mcp.Tools;

/// <summary>
/// MCP wrapper around <see cref="RevitClient.GetStatusAsync"/>.
/// </summary>
internal sealed class StatusTool : IMcpTool
{
    private readonly RevitClient _client;

    public StatusTool(RevitClient client)
    {
        _client = client;
    }

    public string Name => "status";

    public string Description =>
        "Check whether the Revit add-in is reachable and report the active document, " +
        "Revit version, and add-in capabilities.";

    public JsonNode InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    };

    public async Task<string> ExecuteAsync(JsonNode? arguments, CancellationToken cancellationToken)
    {
        var result = await _client.GetStatusAsync();
        if (!result.Success)
            return $"Error: {result.Error}";

        var status = result.Data!;
        var sb = new StringBuilder();
        sb.AppendLine($"Revit version: {status.RevitVersion}");
        if (!string.IsNullOrEmpty(status.AddinVersion))
            sb.AppendLine($"Add-in: v{status.AddinVersion}");
        if (status.DocumentName != null)
        {
            sb.AppendLine($"Document: {status.DocumentName}");
            if (status.DocumentPath != null)
                sb.AppendLine($"Path: {status.DocumentPath}");
        }
        else
        {
            sb.AppendLine("Document: (none open)");
        }
        if (status.Capabilities.Count > 0)
            sb.AppendLine($"Capabilities: {string.Join(", ", status.Capabilities)}");

        return sb.ToString().TrimEnd();
    }
}
