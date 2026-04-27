using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCli.Mcp.Tools;

/// <summary>
/// One MCP tool. Implementations are stateless and reusable across calls.
/// </summary>
internal interface IMcpTool
{
    /// <summary>
    /// Tool name as exposed via `tools/list`. Convention: lowercase, no spaces,
    /// matches the underlying CLI verb (`status`, `query`, `audit`).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// One-line, human-readable description shown to the LLM client.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema (draft 2020-12 compatible subset) describing the
    /// `arguments` object accepted by `tools/call`.
    /// </summary>
    JsonNode InputSchema { get; }

    /// <summary>
    /// Execute the tool. The returned text is wrapped into a single
    /// `text` content block by the dispatcher. If the operation failed
    /// at the application level (Revit not running, bad args after schema
    /// validation, etc.) implementations should still return text and
    /// throw only for unexpected programmer errors.
    /// </summary>
    Task<string> ExecuteAsync(JsonNode? arguments, CancellationToken cancellationToken);
}
