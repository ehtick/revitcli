using System.Threading;
using System.Threading.Tasks;

namespace RevitCli.Mcp.Resources;

/// <summary>
/// One MCP resource. Resources are read-only, addressable artefacts the
/// server exposes via stable URIs (mirrors the file-like surface in the
/// MCP 2024-11-05 spec).
///
/// Implementations are stateless. They typically wrap a CLI subsystem
/// (history store, profile resolver, snapshot capture) and serialise the
/// current value on demand. Connection failures or other transient errors
/// should bubble up as exceptions; the dispatcher converts them to JSON-RPC
/// internal-error responses.
/// </summary>
internal interface IMcpResource
{
    /// <summary>
    /// Stable URI used in <c>resources/list</c> and as the lookup key for
    /// <c>resources/read</c>. Convention: <c>revitcli://&lt;area&gt;/&lt;name&gt;</c>.
    /// </summary>
    string Uri { get; }

    /// <summary>Human-readable short name, surfaced in pickers.</summary>
    string Name { get; }

    /// <summary>One-line description shown to the LLM client.</summary>
    string Description { get; }

    /// <summary>
    /// IANA media type of the read payload. <c>application/json</c>,
    /// <c>text/yaml</c>, <c>text/plain</c> are the values used today.
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Materialise the current value. The returned text is wrapped into a
    /// single text content block by the dispatcher.
    /// </summary>
    Task<string> ReadAsync(CancellationToken cancellationToken);
}
