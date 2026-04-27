using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Shared;

namespace RevitCli.History;

/// <summary>
/// Thread-safe wrapper around the <c>.revitcli/history/</c> directory. The store
/// is responsible for:
/// <list type="bullet">
///   <item>Creating the directory + initial <c>index.json</c> on demand.</item>
///   <item>Writing <see cref="ModelSnapshot"/>s as gzip-compressed JSON files
///         and updating the index atomically (tmp + move).</item>
///   <item>Surfacing the list of stored snapshots through <see cref="ListAsync"/>.</item>
///   <item>Recovering the index from disk-listed filenames if <c>index.json</c>
///         goes missing or unreadable.</item>
///   <item>Pruning by age or count.</item>
/// </list>
/// All methods are safe to call concurrently within a single process; an internal
/// <see cref="SemaphoreSlim"/> serialises index mutations. Cross-process safety
/// is not provided — callers running multiple <c>history capture</c> instances at
/// the exact same wall-clock second should coordinate externally.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>Default directory name relative to the project root.</summary>
    public const string DefaultRelativeDir = ".revitcli/history";

    /// <summary>Index file name inside <see cref="RootDirectory"/>.</summary>
    public const string IndexFileName = "index.json";

    private const string SnapshotPrefix = "snapshot-";
    private const string SnapshotExtension = ".json.gz";

    private static readonly Regex FilenamePattern = new(
        @"^snapshot-(?<ts>\d{8}T\d{6}Z)-(?<hash>[0-9a-f]{8})\.json\.gz$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions IndexReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions SnapshotReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Absolute path of the history directory.</summary>
    public string RootDirectory { get; }

    /// <summary>Absolute path of the index file (regardless of whether it exists).</summary>
    public string IndexPath => Path.Combine(RootDirectory, IndexFileName);

    public HistoryStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("rootDirectory is required", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// Resolve the conventional history directory beneath the supplied project
    /// root (typically the current working directory).
    /// </summary>
    public static HistoryStore ForProject(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("projectRoot is required", nameof(projectRoot));
        }

        return new HistoryStore(Path.Combine(projectRoot, ".revitcli", "history"));
    }

    /// <summary>
    /// Create the history directory (if missing) and write an empty index file
    /// (if missing). Returns <c>true</c> when the directory was newly created.
    /// </summary>
    public async Task<bool> InitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existed = Directory.Exists(RootDirectory);
            Directory.CreateDirectory(RootDirectory);
            if (!File.Exists(IndexPath))
            {
                await WriteIndexAsync(new List<SnapshotMetadata>(), cancellationToken).ConfigureAwait(false);
            }

            return !existed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Append <paramref name="snapshot"/> to the store. The file is written as
    /// gzip-compressed UTF-8 JSON, then the index is updated atomically.
    /// </summary>
    /// <param name="snapshot">Snapshot payload.</param>
    /// <param name="source">Free-form origin tag (manual, cron, fix-baseline...).</param>
    /// <param name="capturedAt">Override capture timestamp; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <returns>The metadata entry that was appended to the index.</returns>
    public async Task<SnapshotMetadata> AppendAsync(
        ModelSnapshot snapshot,
        string source = "manual",
        DateTimeOffset? capturedAt = null,
        CancellationToken cancellationToken = default)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootDirectory);

            var json = JsonSerializer.Serialize(snapshot, IndexJsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var fileHash = ComputeSha256Hex(bytes);
            var shortHash = fileHash.Substring(0, 8);
            var capturedTime = (capturedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var filename = BuildFilename(capturedTime, shortHash);
            var fullPath = Path.Combine(RootDirectory, filename);

            // Avoid silent overwrite if a same-second + same-hash collision occurs.
            // This is extremely rare but easy to defend against deterministically.
            var collisionGuard = 0;
            while (File.Exists(fullPath))
            {
                collisionGuard++;
                if (collisionGuard > 100)
                {
                    throw new IOException(
                        $"Failed to find a unique snapshot filename under '{RootDirectory}'.");
                }

                capturedTime = capturedTime.AddSeconds(1);
                filename = BuildFilename(capturedTime, shortHash);
                fullPath = Path.Combine(RootDirectory, filename);
            }

            await WriteGzipAtomicallyAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);
            var size = new FileInfo(fullPath).Length;

            var metadata = new SnapshotMetadata
            {
                Id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename)),
                CapturedAt = capturedTime.ToString("o", CultureInfo.InvariantCulture),
                Source = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim(),
                Size = size,
                FileHash = fileHash,
                DocumentPath = snapshot.Revit?.DocumentPath ?? string.Empty,
                ElementCount = ComputeElementCount(snapshot),
            };

            var index = await LoadOrRebuildIndexAsync(cancellationToken).ConfigureAwait(false);
            index.RemoveAll(entry => string.Equals(entry.Id, metadata.Id, StringComparison.Ordinal));
            index.Add(metadata);
            await WriteIndexAsync(index, cancellationToken).ConfigureAwait(false);
            return metadata;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Return the index entries sorted by capture instant (newest first).
    /// </summary>
    /// <param name="includeFixBaselines">When <c>false</c> (default) entries with
    /// <c>source == "fix-baseline"</c> are excluded — these are typically managed
    /// by the auto-fix flow and are noisy when reviewing the user-driven timeline.
    /// </param>
    public async Task<IReadOnlyList<SnapshotMetadata>> ListAsync(
        bool includeFixBaselines = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadOrRebuildIndexAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<SnapshotMetadata> filtered = entries;
            if (!includeFixBaselines)
            {
                filtered = filtered.Where(entry =>
                    !string.Equals(entry.Source, "fix-baseline", StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderByDescending(entry => ParseTimestamp(entry.CapturedAt))
                .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Delete history entries that exceed the supplied retention. Only one of
    /// <paramref name="maxAge"/> / <paramref name="maxCount"/> needs to be set;
    /// when both are provided an entry must satisfy <i>both</i> constraints to
    /// be retained.
    /// </summary>
    /// <param name="maxAge">Drop entries with <c>CapturedAt &lt; now - maxAge</c>.</param>
    /// <param name="maxCount">Keep at most this many entries (newest wins).</param>
    /// <param name="apply">When <c>false</c> the report is computed but no files
    /// are removed and the index is left untouched.</param>
    /// <param name="includeFixBaselines">Mirrors <see cref="ListAsync"/>: when
    /// <c>false</c> fix-baselines are protected from automatic removal.</param>
    public async Task<PruneResult> PruneAsync(
        TimeSpan? maxAge = null,
        int? maxCount = null,
        bool apply = false,
        bool includeFixBaselines = false,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAge == null && maxCount == null)
        {
            throw new ArgumentException("Either maxAge or maxCount must be specified.");
        }

        if (maxCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be >= 0.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadOrRebuildIndexAsync(cancellationToken).ConfigureAwait(false);
            var resolvedNow = now ?? DateTimeOffset.UtcNow;
            var ordered = index
                .OrderByDescending(entry => ParseTimestamp(entry.CapturedAt))
                .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
                .ToList();

            var keep = new List<SnapshotMetadata>();
            var remove = new List<SnapshotMetadata>();
            var keptCount = 0;
            foreach (var entry in ordered)
            {
                var protectedEntry = !includeFixBaselines &&
                    string.Equals(entry.Source, "fix-baseline", StringComparison.OrdinalIgnoreCase);
                if (protectedEntry)
                {
                    keep.Add(entry);
                    continue;
                }

                var captured = ParseTimestamp(entry.CapturedAt);
                var ageOk = maxAge == null || (resolvedNow - captured) <= maxAge.Value;
                var countOk = maxCount == null || keptCount < maxCount.Value;
                if (ageOk && countOk)
                {
                    keep.Add(entry);
                    keptCount++;
                }
                else
                {
                    remove.Add(entry);
                }
            }

            if (apply && remove.Count > 0)
            {
                foreach (var entry in remove)
                {
                    var path = Path.Combine(RootDirectory, entry.Id + SnapshotExtension);
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch (IOException)
                    {
                        // Tolerate transient delete failures; the index rebuild on the
                        // next call will clean up dangling references.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Same rationale; we report the entry but cannot guarantee removal.
                    }
                }

                await WriteIndexAsync(keep, cancellationToken).ConfigureAwait(false);
            }

            return new PruneResult(
                keep.Count,
                remove.Count,
                remove.Sum(entry => entry.Size),
                remove,
                apply);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Read a previously stored snapshot back into a <see cref="ModelSnapshot"/>.
    /// </summary>
    public async Task<ModelSnapshot?> ReadAsync(
        SnapshotMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var path = Path.Combine(RootDirectory, metadata.Id + SnapshotExtension);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var fileStream = File.OpenRead(path);
        await using var gz = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ModelSnapshot>(json, SnapshotReadOptions);
    }

    /// <summary>
    /// Force the index to be regenerated from filesystem listing. Useful for
    /// tests and as a recovery hook when external tools have edited the
    /// directory.
    /// </summary>
    public async Task<IReadOnlyList<SnapshotMetadata>> RebuildIndexAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rebuilt = await RebuildFromFilesystemAsync(cancellationToken).ConfigureAwait(false);
            await WriteIndexAsync(rebuilt, cancellationToken).ConfigureAwait(false);
            return rebuilt;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<SnapshotMetadata>> LoadOrRebuildIndexAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(RootDirectory))
        {
            return new List<SnapshotMetadata>();
        }

        if (File.Exists(IndexPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(IndexPath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return await RebuildFromFilesystemAsync(cancellationToken).ConfigureAwait(false);
                }

                var parsed = JsonSerializer.Deserialize<HistoryIndexFile>(json, IndexReadOptions);
                if (parsed?.Entries != null)
                {
                    // Drop entries whose backing file disappeared from disk — keeps
                    // ListAsync honest after manual deletions.
                    var alive = new List<SnapshotMetadata>(parsed.Entries.Count);
                    foreach (var entry in parsed.Entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                        {
                            continue;
                        }

                        var path = Path.Combine(RootDirectory, entry.Id + SnapshotExtension);
                        if (File.Exists(path))
                        {
                            alive.Add(entry);
                        }
                    }

                    return alive;
                }
            }
            catch (JsonException)
            {
                // Fall through to rebuild.
            }
            catch (IOException)
            {
                // Fall through to rebuild.
            }
        }

        return await RebuildFromFilesystemAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<SnapshotMetadata>> RebuildFromFilesystemAsync(CancellationToken cancellationToken)
    {
        var rebuilt = new List<SnapshotMetadata>();
        if (!Directory.Exists(RootDirectory))
        {
            return rebuilt;
        }

        foreach (var path in Directory.EnumerateFiles(RootDirectory, "*" + SnapshotExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            var match = FilenamePattern.Match(name);
            if (!match.Success)
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(name));
            var timestampToken = match.Groups["ts"].Value;
            var capturedAt = ParseFilenameTimestamp(timestampToken);
            long size = 0;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                // Best effort; size is informational only.
            }

            int elementCount = 0;
            string documentPath = string.Empty;
            string fileHash = string.Empty;
            try
            {
                await using var fileStream = File.OpenRead(path);
                await using var gz = new GZipStream(fileStream, CompressionMode.Decompress);
                using var ms = new MemoryStream();
                await gz.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                var raw = ms.ToArray();
                fileHash = ComputeSha256Hex(raw);
                var snapshot = JsonSerializer.Deserialize<ModelSnapshot>(raw, SnapshotReadOptions);
                if (snapshot != null)
                {
                    elementCount = ComputeElementCount(snapshot);
                    documentPath = snapshot.Revit?.DocumentPath ?? string.Empty;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                // Snapshot is unreadable but the file exists — keep a stub so the
                // user can still see the entry and decide how to handle it.
            }

            rebuilt.Add(new SnapshotMetadata
            {
                Id = id,
                CapturedAt = capturedAt.ToString("o", CultureInfo.InvariantCulture),
                Source = "rebuilt",
                Size = size,
                FileHash = fileHash,
                DocumentPath = documentPath,
                ElementCount = elementCount,
            });
        }

        return rebuilt;
    }

    private async Task WriteIndexAsync(List<SnapshotMetadata> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootDirectory);
        var ordered = entries
            .OrderByDescending(entry => ParseTimestamp(entry.CapturedAt))
            .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
            .ToList();
        var payload = new HistoryIndexFile
        {
            Version = 1,
            Entries = ordered,
        };
        var json = JsonSerializer.Serialize(payload, IndexJsonOptions);
        var tempPath = IndexPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // File.Move with overwrite is atomic on the same volume on every supported OS.
        if (File.Exists(IndexPath))
        {
            File.Replace(tempPath, IndexPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, IndexPath);
        }
    }

    private static async Task WriteGzipAtomicallyAsync(
        string targetPath,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var tempPath = targetPath + ".tmp";
        await using (var fileStream = File.Create(tempPath))
        await using (var gz = new GZipStream(fileStream, CompressionLevel.Optimal))
        {
            await gz.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private static string ComputeSha256Hex(byte[] payload)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(payload);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static int ComputeElementCount(ModelSnapshot snapshot)
    {
        if (snapshot.Summary?.ElementCounts != null && snapshot.Summary.ElementCounts.Count > 0)
        {
            return snapshot.Summary.ElementCounts.Values.Sum();
        }

        if (snapshot.Categories == null)
        {
            return 0;
        }

        var total = 0;
        foreach (var entries in snapshot.Categories.Values)
        {
            if (entries != null)
            {
                total += entries.Count;
            }
        }

        return total;
    }

    private static string BuildFilename(DateTimeOffset capturedAt, string shortHash)
    {
        var ts = capturedAt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        return $"{SnapshotPrefix}{ts}-{shortHash}{SnapshotExtension}";
    }

    private static DateTimeOffset ParseFilenameTimestamp(string token)
    {
        if (DateTimeOffset.TryParseExact(
                token,
                "yyyyMMddTHHmmssZ",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.MinValue;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTimeOffset.MinValue;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private sealed class HistoryIndexFile
    {
        public int Version { get; set; } = 1;
        public List<SnapshotMetadata> Entries { get; set; } = new();
    }

    /// <summary>Outcome of a <see cref="PruneAsync"/> invocation.</summary>
    /// <param name="KeptCount">Snapshots that remain after pruning.</param>
    /// <param name="RemovedCount">Snapshots that were (or would be) removed.</param>
    /// <param name="RemovedBytes">Total bytes freed (or planned).</param>
    /// <param name="Removed">Concrete removed entries — useful for reporting.</param>
    /// <param name="Applied">Whether changes were actually written to disk.</param>
    public sealed record PruneResult(
        int KeptCount,
        int RemovedCount,
        long RemovedBytes,
        IReadOnlyList<SnapshotMetadata> Removed,
        bool Applied);
}
