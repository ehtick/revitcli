using System.Text.Json.Serialization;

namespace RevitCli.History;

/// <summary>
/// Index entry for a captured snapshot stored under <c>.revitcli/history/</c>.
/// One <see cref="SnapshotMetadata"/> corresponds to exactly one <c>.json.gz</c> file
/// on disk; the file name embeds the ISO-8601 capture instant and the short hash so
/// the index can be rebuilt from filesystem listing alone if <c>index.json</c>
/// becomes unreadable.
/// </summary>
public class SnapshotMetadata
{
    /// <summary>
    /// Stable identifier of the snapshot. Equal to the file name (without
    /// the <c>.json.gz</c> suffix), e.g. <c>snapshot-20260427T120000Z-ab12cd34</c>.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// ISO-8601 UTC timestamp (round-trip "o" format) marking when the snapshot
    /// was appended to the history store. May differ from <c>ModelSnapshot.TakenAt</c>
    /// when imported from a fix-baseline written earlier.
    /// </summary>
    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = string.Empty;

    /// <summary>
    /// Origin of the snapshot. Common values: <c>manual</c>, <c>cron</c>,
    /// <c>fix-baseline</c>. Free-form so future capture sources can register
    /// without forcing an enum migration.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "manual";

    /// <summary>Size in bytes of the gzip-compressed payload on disk.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the uncompressed snapshot JSON bytes. Used for
    /// integrity checks and de-duplication when archiving fix-baselines that
    /// already exist as standalone files.
    /// </summary>
    [JsonPropertyName("fileHash")]
    public string FileHash { get; set; } = string.Empty;

    /// <summary>Path to the source RVT document at capture time, if known.</summary>
    [JsonPropertyName("documentPath")]
    public string DocumentPath { get; set; } = string.Empty;

    /// <summary>Total element count summed over all categories in the snapshot.</summary>
    [JsonPropertyName("elementCount")]
    public int ElementCount { get; set; }
}
