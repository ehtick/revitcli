using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.History;

namespace RevitCli.Mcp.Resources;

/// <summary>
/// <c>revitcli://history/</c> — lists snapshot metadata from the local
/// <c>.revitcli/history/</c> store (or whichever directory the caller wires
/// in). When the store has not been initialised the resource returns an
/// empty JSON array rather than failing — this is a probe-friendly shape
/// for LLM clients deciding whether to suggest <c>history capture</c>.
/// </summary>
internal sealed class HistoryListResource : IMcpResource
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly HistoryStore _store;

    public HistoryListResource(HistoryStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Convenience constructor that points the resource at the conventional
    /// <c>{cwd}/.revitcli/history</c> directory.
    /// </summary>
    public static HistoryListResource ForCurrentDirectory() =>
        new(HistoryStore.ForProject(Directory.GetCurrentDirectory()));

    public string Uri => "revitcli://history/";

    public string Name => "Snapshot history";

    public string Description =>
        "List recent snapshots stored under .revitcli/history/. " +
        "Returns an empty array when the store is missing.";

    public string MimeType => "application/json";

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_store.RootDirectory))
            return "[]";

        var entries = await _store.ListAsync(includeFixBaselines: false, cancellationToken).ConfigureAwait(false);
        // Project to the documented shape: { id, capturedAt, source, ... }.
        var projection = entries.Select(entry => new
        {
            id = entry.Id,
            capturedAt = entry.CapturedAt,
            source = entry.Source,
            size = entry.Size,
            documentPath = entry.DocumentPath,
            elementCount = entry.ElementCount,
        }).ToArray();

        return JsonSerializer.Serialize(projection, PrettyJson);
    }
}
