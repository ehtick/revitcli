using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.History;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.History;

public class HistoryStoreTests : IDisposable
{
    private readonly string _root;
    private readonly HistoryStore _store;

    public HistoryStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-history-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new HistoryStore(Path.Combine(_root, "history"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException) { /* leftover temp dir; harmless */ }
        catch (UnauthorizedAccessException) { /* idem */ }
    }

    private static ModelSnapshot MakeSnapshot(string takenAt, params (string Category, int Count)[] categories)
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = takenAt,
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Sample.rvt",
                DocumentPath = "C:/projects/Sample.rvt",
            },
            Summary = new SnapshotSummary(),
        };
        foreach (var (category, count) in categories)
        {
            var elements = new List<SnapshotElement>();
            for (var i = 0; i < count; i++)
            {
                elements.Add(new SnapshotElement { Id = i + 1, Name = $"{category}-{i + 1}" });
            }
            snapshot.Categories[category] = elements;
            snapshot.Summary.ElementCounts[category] = count;
        }

        return snapshot;
    }

    [Fact]
    public async Task Init_CreatesDirectoryAndIndex()
    {
        var created = await _store.InitAsync();
        Assert.True(created);
        Assert.True(Directory.Exists(_store.RootDirectory));
        Assert.True(File.Exists(_store.IndexPath));

        var second = await _store.InitAsync();
        Assert.False(second);
    }

    [Fact]
    public async Task Append_WritesGzipFileAndIndex()
    {
        await _store.InitAsync();
        var snap = MakeSnapshot("2026-04-01T00:00:00Z", ("walls", 3), ("doors", 2));
        var meta = await _store.AppendAsync(snap, source: "manual",
            capturedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.StartsWith("snapshot-20260401T000000Z-", meta.Id, StringComparison.Ordinal);
        Assert.Equal(5, meta.ElementCount);
        Assert.Equal("manual", meta.Source);

        var path = Path.Combine(_store.RootDirectory, meta.Id + ".json.gz");
        Assert.True(File.Exists(path));

        // Round-trip through gzip to confirm the payload is valid.
        await using var fs = File.OpenRead(path);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var parsed = JsonSerializer.Deserialize<ModelSnapshot>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(parsed);
        Assert.Equal("2026", parsed!.Revit.Version);

        var entries = await _store.ListAsync();
        Assert.Single(entries);
        Assert.Equal(meta.Id, entries[0].Id);
    }

    [Fact]
    public async Task Append_MultipleEntries_ListSortedNewestFirst()
    {
        await _store.InitAsync();
        var jan = await _store.AppendAsync(MakeSnapshot("2026-01-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var feb = await _store.AppendAsync(MakeSnapshot("2026-02-01T00:00:00Z", ("walls", 2)),
            "manual", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var mar = await _store.AppendAsync(MakeSnapshot("2026-03-01T00:00:00Z", ("walls", 3)),
            "manual", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var listed = await _store.ListAsync();
        Assert.Equal(3, listed.Count);
        Assert.Equal(mar.Id, listed[0].Id);
        Assert.Equal(feb.Id, listed[1].Id);
        Assert.Equal(jan.Id, listed[2].Id);
    }

    [Fact]
    public async Task List_FixBaselineHiddenByDefault()
    {
        await _store.InitAsync();
        await _store.AppendAsync(MakeSnapshot("2026-04-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await _store.AppendAsync(MakeSnapshot("2026-04-02T00:00:00Z", ("walls", 1)),
            "fix-baseline", new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero));

        var defaults = await _store.ListAsync();
        Assert.Single(defaults);
        Assert.Equal("manual", defaults[0].Source);

        var all = await _store.ListAsync(includeFixBaselines: true);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task IndexCorruption_RebuildsFromFilesystem()
    {
        await _store.InitAsync();
        var meta = await _store.AppendAsync(MakeSnapshot("2026-04-10T00:00:00Z", ("walls", 4)),
            "manual", new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero));

        // Corrupt the index file.
        await File.WriteAllTextAsync(_store.IndexPath, "{ this is not valid JSON");

        var rebuilt = await _store.ListAsync();
        Assert.Single(rebuilt);
        Assert.Equal(meta.Id, rebuilt[0].Id);
        // Rebuild stub uses source=rebuilt because we lost the original source label.
        Assert.Equal("rebuilt", rebuilt[0].Source);
    }

    [Fact]
    public async Task IndexMissing_RebuildsFromFilesystem()
    {
        await _store.InitAsync();
        var meta = await _store.AppendAsync(MakeSnapshot("2026-04-10T00:00:00Z", ("walls", 4)),
            "manual", new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero));

        File.Delete(_store.IndexPath);

        var rebuilt = await _store.ListAsync();
        Assert.Single(rebuilt);
        Assert.Equal(meta.Id, rebuilt[0].Id);
    }

    [Fact]
    public async Task IndexReferencesDeletedFile_DropsEntry()
    {
        await _store.InitAsync();
        var keep = await _store.AppendAsync(MakeSnapshot("2026-04-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        var ghost = await _store.AppendAsync(MakeSnapshot("2026-04-02T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero));

        // Manually delete the ghost file but leave the index intact.
        File.Delete(Path.Combine(_store.RootDirectory, ghost.Id + ".json.gz"));

        var listed = await _store.ListAsync();
        Assert.Single(listed);
        Assert.Equal(keep.Id, listed[0].Id);
    }

    [Fact]
    public async Task ReadAsync_ReturnsRoundTrippedSnapshot()
    {
        await _store.InitAsync();
        var meta = await _store.AppendAsync(MakeSnapshot("2026-04-15T00:00:00Z", ("walls", 5)),
            "manual", new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero));

        var snapshot = await _store.ReadAsync(meta);
        Assert.NotNull(snapshot);
        Assert.Equal("2026-04-15T00:00:00Z", snapshot!.TakenAt);
        Assert.Equal(5, snapshot.Categories["walls"].Count);
    }

    [Fact]
    public async Task PruneByAge_DryRun_DoesNotDelete()
    {
        await _store.InitAsync();
        var old = await _store.AppendAsync(MakeSnapshot("2026-01-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var young = await _store.AppendAsync(MakeSnapshot("2026-04-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
        var dryResult = await _store.PruneAsync(maxAge: TimeSpan.FromDays(30), apply: false, now: now);

        Assert.Equal(1, dryResult.RemovedCount);
        Assert.Equal(1, dryResult.KeptCount);
        Assert.False(dryResult.Applied);

        // Files still on disk because dry-run.
        Assert.True(File.Exists(Path.Combine(_store.RootDirectory, old.Id + ".json.gz")));
        Assert.True(File.Exists(Path.Combine(_store.RootDirectory, young.Id + ".json.gz")));

        // Index unchanged.
        var listed = await _store.ListAsync();
        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public async Task PruneByAge_Apply_DeletesAndRewritesIndex()
    {
        await _store.InitAsync();
        var old = await _store.AppendAsync(MakeSnapshot("2026-01-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var young = await _store.AppendAsync(MakeSnapshot("2026-04-01T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
        var applyResult = await _store.PruneAsync(maxAge: TimeSpan.FromDays(30), apply: true, now: now);

        Assert.Equal(1, applyResult.RemovedCount);
        Assert.True(applyResult.Applied);
        Assert.False(File.Exists(Path.Combine(_store.RootDirectory, old.Id + ".json.gz")));
        Assert.True(File.Exists(Path.Combine(_store.RootDirectory, young.Id + ".json.gz")));

        var listed = await _store.ListAsync();
        Assert.Single(listed);
        Assert.Equal(young.Id, listed[0].Id);
    }

    [Fact]
    public async Task PruneByCount_KeepsNewest()
    {
        await _store.InitAsync();
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var meta = await _store.AppendAsync(
                MakeSnapshot($"2026-04-0{i + 1}T00:00:00Z", ("walls", 1)),
                "manual",
                new DateTimeOffset(2026, 4, i + 1, 0, 0, 0, TimeSpan.Zero));
            ids.Add(meta.Id);
        }

        var result = await _store.PruneAsync(maxCount: 2, apply: true);
        Assert.Equal(3, result.RemovedCount);
        Assert.Equal(2, result.KeptCount);

        var listed = await _store.ListAsync();
        Assert.Equal(2, listed.Count);
        Assert.Equal(ids[4], listed[0].Id);
        Assert.Equal(ids[3], listed[1].Id);
    }

    [Fact]
    public async Task PruneByAge_FixBaselineProtectedByDefault()
    {
        await _store.InitAsync();
        await _store.AppendAsync(MakeSnapshot("2026-01-01T00:00:00Z", ("walls", 1)),
            "fix-baseline", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await _store.AppendAsync(MakeSnapshot("2026-01-02T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await _store.PruneAsync(maxAge: TimeSpan.FromDays(30), apply: true, now: now);

        // Only the manual entry was eligible for removal; the fix-baseline survives.
        Assert.Equal(1, result.RemovedCount);
        var leftover = await _store.ListAsync(includeFixBaselines: true);
        Assert.Single(leftover);
        Assert.Equal("fix-baseline", leftover[0].Source);
    }

    [Fact]
    public async Task PruneAsync_WithoutCriteria_Throws()
    {
        await _store.InitAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _store.PruneAsync());
    }

    [Fact]
    public async Task RebuildIndexAsync_RegeneratesFromDisk()
    {
        await _store.InitAsync();
        var meta = await _store.AppendAsync(MakeSnapshot("2026-04-20T00:00:00Z", ("walls", 1)),
            "manual", new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));

        // Hand-craft an index missing the entry, then rebuild.
        await File.WriteAllTextAsync(_store.IndexPath, "{\"version\":1,\"entries\":[]}");
        var rebuilt = await _store.RebuildIndexAsync();

        Assert.Single(rebuilt);
        Assert.Equal(meta.Id, rebuilt[0].Id);
    }

    [Fact]
    public async Task ForProject_PointsAtConventionalSubdirectory()
    {
        var project = Path.Combine(_root, "project");
        Directory.CreateDirectory(project);
        var store = HistoryStore.ForProject(project);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(project, ".revitcli", "history")),
            store.RootDirectory);

        await store.InitAsync();
        Assert.True(Directory.Exists(store.RootDirectory));
    }
}
