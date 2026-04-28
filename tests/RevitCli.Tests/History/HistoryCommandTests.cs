using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.History;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.History;

public class HistoryCommandTests : IDisposable
{
    private readonly string _root;

    public HistoryCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-history-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string HistoryDir => Path.Combine(_root, "history");

    private static ModelSnapshot MakeSnapshot()
    {
        var snap = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-04-27T10:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", Document = "Sample.rvt", DocumentPath = "C:/p/Sample.rvt" },
            Summary = new SnapshotSummary { ElementCounts = new Dictionary<string, int> { ["walls"] = 4 } },
        };
        snap.Categories["walls"] = new List<SnapshotElement>
        {
            new() { Id = 1, Name = "W1" },
            new() { Id = 2, Name = "W2" },
            new() { Id = 3, Name = "W3" },
            new() { Id = 4, Name = "W4" },
        };
        return snap;
    }

    private static RevitClient MakeClient(ModelSnapshot snap)
    {
        var response = ApiResponse<ModelSnapshot>.Ok(snap);
        var handler = new QueueHandler(JsonSerializer.Serialize(response));
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private static RevitClient MakeFailingClient(string error)
    {
        var response = ApiResponse<ModelSnapshot>.Fail(error);
        var handler = new QueueHandler(JsonSerializer.Serialize(response));
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }

    [Fact]
    public async Task Init_CreatesDirectoryAndReportsSuccess()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteInitAsync(HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.True(Directory.Exists(HistoryDir));
        Assert.True(File.Exists(Path.Combine(HistoryDir, "index.json")));
        Assert.Contains("Initialized history store", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Init_AlreadyExists_ReportsExisting()
    {
        Directory.CreateDirectory(HistoryDir);
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteInitAsync(HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.Contains("already exists", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capture_WritesEntryAndUpdatesIndex()
    {
        var writer = new StringWriter();
        using var client = MakeClient(MakeSnapshot());

        var exit = await HistoryCommand.ExecuteCaptureAsync(client, "manual", true, HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.Contains("Captured", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        var store = new HistoryStore(HistoryDir);
        var entries = await store.ListAsync();
        Assert.Single(entries);
        Assert.Equal("manual", entries[0].Source);
        Assert.Equal(4, entries[0].ElementCount);
    }

    [Fact]
    public async Task Capture_RevitFailure_ReturnsOne()
    {
        var writer = new StringWriter();
        using var client = MakeFailingClient("Revit not running");

        var exit = await HistoryCommand.ExecuteCaptureAsync(client, "manual", true, HistoryDir, writer);

        Assert.Equal(1, exit);
        Assert.Contains("error", writer.ToString().ToLowerInvariant());
        Assert.False(Directory.Exists(HistoryDir) && File.Exists(Path.Combine(HistoryDir, "index.json"))
                     && Directory.GetFiles(HistoryDir, "*.json.gz").Length > 0);
    }

    [Fact]
    public async Task Capture_CustomSource_PropagatesToMetadata()
    {
        var writer = new StringWriter();
        using var client = MakeClient(MakeSnapshot());

        var exit = await HistoryCommand.ExecuteCaptureAsync(client, "cron", true, HistoryDir, writer);
        Assert.Equal(0, exit);

        var store = new HistoryStore(HistoryDir);
        var entries = await store.ListAsync();
        Assert.Single(entries);
        Assert.Equal("cron", entries[0].Source);
    }

    [Fact]
    public async Task List_NoStore_PrintsHelpfulMessage()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteListAsync(false, 20, HistoryDir, writer);
        Assert.Equal(0, exit);
        Assert.Contains("not initialised", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_EmptyStore_ReportsNone()
    {
        Directory.CreateDirectory(HistoryDir);
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteListAsync(false, 20, HistoryDir, writer);
        Assert.Equal(0, exit);
        Assert.Contains("No snapshots", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_RendersTableHeaders()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(), "manual",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteListAsync(false, 20, HistoryDir, writer);

        Assert.Equal(0, exit);
        var text = writer.ToString();
        Assert.Contains("id", text);
        Assert.Contains("capturedAt", text);
        Assert.Contains("source", text);
        Assert.Contains("elements", text);
        Assert.Contains("size", text);
        Assert.Contains("manual", text);
    }

    [Fact]
    public async Task List_LimitTruncatesAndAnnouncesHidden()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        for (var i = 0; i < 5; i++)
        {
            await store.AppendAsync(MakeSnapshot(), "manual",
                new DateTimeOffset(2026, 4, i + 1, 0, 0, 0, TimeSpan.Zero));
        }

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteListAsync(false, 2, HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.Contains("hidden", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_InvalidLimit_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecuteListAsync(false, 0, HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("limit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_FixBaselineHiddenByDefault_VisibleWithFlag()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        await store.AppendAsync(MakeSnapshot(), "fix-baseline",
            new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(MakeSnapshot(), "manual",
            new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero));

        var hiddenWriter = new StringWriter();
        await HistoryCommand.ExecuteListAsync(false, 20, HistoryDir, hiddenWriter);
        Assert.DoesNotContain("fix-baseline", hiddenWriter.ToString());
        Assert.Contains("manual", hiddenWriter.ToString());

        var includeWriter = new StringWriter();
        await HistoryCommand.ExecuteListAsync(true, 20, HistoryDir, includeWriter);
        Assert.Contains("fix-baseline", includeWriter.ToString());
    }

    [Fact]
    public async Task Prune_KeepRequired_ReturnsOne()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecutePruneAsync(null, true, false, false, HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("--keep", writer.ToString());
    }

    [Fact]
    public async Task Prune_DryRun_DoesNotRemoveFiles()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var meta = await store.AppendAsync(MakeSnapshot(), "manual",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecutePruneAsync("count:0", true, false, false, HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.Contains("Would prune", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(HistoryDir, meta.Id + ".json.gz")));
    }

    [Fact]
    public async Task Prune_Apply_RemovesFiles()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var meta = await store.AppendAsync(MakeSnapshot(), "manual",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecutePruneAsync("count:0", false, true, false, HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.Contains("Pruned", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(HistoryDir, meta.Id + ".json.gz")));
    }

    [Fact]
    public async Task Prune_DurationKeep_KeepsRecentEntry()
    {
        var store = new HistoryStore(HistoryDir);
        await store.InitAsync();
        var oldMeta = await store.AppendAsync(MakeSnapshot(), "manual",
            DateTimeOffset.UtcNow - TimeSpan.FromDays(120));
        var newMeta = await store.AppendAsync(MakeSnapshot(), "manual",
            DateTimeOffset.UtcNow - TimeSpan.FromDays(1));

        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecutePruneAsync("30d", false, true, false, HistoryDir, writer);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Path.Combine(HistoryDir, oldMeta.Id + ".json.gz")));
        Assert.True(File.Exists(Path.Combine(HistoryDir, newMeta.Id + ".json.gz")));
    }

    [Fact]
    public async Task Prune_InvalidKeep_ReturnsOne()
    {
        Directory.CreateDirectory(HistoryDir);
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecutePruneAsync("garbage", false, true, false, HistoryDir, writer);
        Assert.Equal(1, exit);
        Assert.Contains("Invalid retention", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Prune_NoStore_ReturnsZeroAndExplains()
    {
        var writer = new StringWriter();
        var exit = await HistoryCommand.ExecutePruneAsync("30d", false, true, false, HistoryDir, writer);
        Assert.Equal(0, exit);
        Assert.Contains("not initialised", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly string _payload;
        public QueueHandler(string payload) { _payload = payload; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
