using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;

namespace RevitCli.Mcp.Resources;

/// <summary>
/// <c>revitcli://snapshot/latest</c> — captures a fresh summary-only model
/// snapshot from the running add-in on every read. Returns indented JSON so
/// the LLM client can lift specific fields without an extra tool round-trip.
///
/// Connection failures are propagated as an <see cref="InvalidOperationException"/>
/// — the dispatcher maps that to JSON-RPC internal-error (-32603).
/// </summary>
internal sealed class SnapshotLatestResource : IMcpResource
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly RevitClient _client;

    public SnapshotLatestResource(RevitClient client)
    {
        _client = client;
    }

    public string Uri => "revitcli://snapshot/latest";

    public string Name => "Latest model snapshot";

    public string Description =>
        "Capture the current Revit model and return a summary-only snapshot " +
        "(element counts, sheets, schedules). Read on demand.";

    public string MimeType => "application/json";

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await _client.CaptureSnapshotAsync(new SnapshotRequest { SummaryOnly = true });
        if (!result.Success)
            throw new InvalidOperationException(
                $"Failed to capture snapshot: {result.Error ?? "unknown error"}");

        return JsonSerializer.Serialize(result.Data, PrettyJson);
    }
}
