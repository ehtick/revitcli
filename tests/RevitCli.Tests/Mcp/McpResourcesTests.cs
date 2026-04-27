using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.History;
using RevitCli.Mcp;
using RevitCli.Mcp.Resources;
using RevitCli.Mcp.Tools;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Mcp;

/// <summary>
/// Phase-2 coverage for the MCP resources surface
/// (<c>resources/list</c> + <c>resources/read</c>).
/// </summary>
public class McpResourcesTests
{
    [Fact]
    public async Task ResourcesList_ReturnsRegisteredResourcesWithSpecShape()
    {
        var (server, _) = BuildServer(out var output);

        await server.HandleLineAsync(Request(1, "resources/list"), CancellationToken.None);

        var response = ReadOneResponse(output);
        var resources = response["result"]!["resources"]!.AsArray();
        Assert.Equal(3, resources.Count);

        var byUri = resources.ToDictionary(r => r!["uri"]!.GetValue<string>(), r => r!);
        Assert.Contains("revitcli://snapshot/latest", byUri.Keys);
        Assert.Contains("revitcli://history/", byUri.Keys);
        Assert.Contains("revitcli://profile/effective", byUri.Keys);

        foreach (var (uri, node) in byUri)
        {
            Assert.False(string.IsNullOrWhiteSpace(node["name"]!.GetValue<string>()), $"{uri} name");
            Assert.False(string.IsNullOrWhiteSpace(node["description"]!.GetValue<string>()), $"{uri} description");
            Assert.False(string.IsNullOrWhiteSpace(node["mimeType"]!.GetValue<string>()), $"{uri} mimeType");
        }
    }

    [Fact]
    public async Task InitializeAdvertisesResourcesCapabilityWhenResourcesRegistered()
    {
        var (server, _) = BuildServer(out var output);

        await server.HandleLineAsync(Request(1, "initialize", new JsonObject()), CancellationToken.None);

        var response = ReadOneResponse(output);
        var resourcesCap = response["result"]!["capabilities"]!["resources"]!;
        Assert.False(resourcesCap["subscribe"]!.GetValue<bool>());
        Assert.False(resourcesCap["listChanged"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ResourcesRead_SnapshotLatest_InvokesClientAndReturnsTextContent()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot
        {
            TakenAt = "2026-04-27T00:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", Document = "Project1.rvt" },
        }));

        var (server, _) = BuildServer(handler, resources: new IMcpResource[]
        {
            new SnapshotLatestResource(MakeClient(handler)),
        }, output: out var output);

        await server.HandleLineAsync(Request(2, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://snapshot/latest",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var contents = response["result"]!["contents"]!.AsArray();
        Assert.Single(contents);
        var entry = contents[0]!;
        Assert.Equal("revitcli://snapshot/latest", entry["uri"]!.GetValue<string>());
        Assert.Equal("application/json", entry["mimeType"]!.GetValue<string>());
        var text = entry["text"]!.GetValue<string>();
        Assert.Contains("2026", text);
        Assert.Contains("Project1.rvt", text);
        Assert.Contains("/api/snapshot", handler.Requests);
    }

    [Fact]
    public async Task ResourcesRead_HistoryList_ReturnsArrayProjection()
    {
        using var temp = new TempDirectory();
        var store = new HistoryStore(temp.Path);
        await store.AppendAsync(new ModelSnapshot
        {
            TakenAt = "2026-04-27T01:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", DocumentPath = "C:/x.rvt" },
            Summary = new SnapshotSummary { ElementCounts = new Dictionary<string, int> { ["Walls"] = 7 } },
        }, source: "manual");

        var (server, _) = BuildServer(new QueueHttpHandler(), resources: new IMcpResource[]
        {
            new HistoryListResource(store),
        }, output: out var output);

        await server.HandleLineAsync(Request(3, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://history/",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["contents"]!.AsArray()[0]!["text"]!.GetValue<string>();
        var parsed = JsonNode.Parse(text)!.AsArray();
        Assert.Single(parsed);
        Assert.Equal("manual", parsed[0]!["source"]!.GetValue<string>());
        Assert.Equal(7, parsed[0]!["elementCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task ResourcesRead_HistoryList_EmptyArrayWhenStoreMissing()
    {
        using var temp = new TempDirectory();
        // Point at a subdirectory that does NOT exist on disk.
        var missing = Path.Combine(temp.Path, "never-created");
        var store = new HistoryStore(missing);

        var (server, _) = BuildServer(new QueueHttpHandler(), resources: new IMcpResource[]
        {
            new HistoryListResource(store),
        }, output: out var output);

        await server.HandleLineAsync(Request(4, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://history/",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["contents"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Equal("[]", text.Trim());
    }

    [Fact]
    public async Task ResourcesRead_ProfileEffective_ReturnsYamlText()
    {
        using var temp = new TempDirectory();
        var profilePath = Path.Combine(temp.Path, ".revitcli.yml");
        // Minimal valid profile — schema fields are optional but the loader
        // still has to round-trip without errors and emit the YAML body
        // (so the chain header + version line should both appear).
        File.WriteAllText(profilePath, "version: 1\n");

        var (server, _) = BuildServer(new QueueHttpHandler(), resources: new IMcpResource[]
        {
            new ProfileResource(() => profilePath),
        }, output: out var output);

        await server.HandleLineAsync(Request(5, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://profile/effective",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var entry = response["result"]!["contents"]!.AsArray()[0]!;
        Assert.Equal("text/yaml", entry["mimeType"]!.GetValue<string>());
        var text = entry["text"]!.GetValue<string>();
        Assert.Contains("Resolved profile", text);
        // ProfileResolver renders the merged ProjectProfile — `version` is
        // always emitted because it has a non-null default of 1.
        Assert.Contains("version: 1", text);
    }

    [Fact]
    public async Task ResourcesRead_ProfileEffective_NoProfile_ReturnsCommentStub()
    {
        var (server, _) = BuildServer(new QueueHttpHandler(), resources: new IMcpResource[]
        {
            new ProfileResource(() => null),
        }, output: out var output);

        await server.HandleLineAsync(Request(5, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://profile/effective",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        var text = response["result"]!["contents"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Contains("No .revitcli.yml found", text);
    }

    [Fact]
    public async Task ResourcesRead_UnknownUri_ReturnsInvalidParams()
    {
        var (server, _) = BuildServer(out var output);

        await server.HandleLineAsync(Request(6, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://nope",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.Null(response["result"]);
        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("revitcli://nope", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ResourcesRead_SnapshotFailure_PropagatesAsInternalError()
    {
        var handler = new QueueHttpHandler();
        // No enqueue → the handler will throw, surfaced through the resource as
        // an InvalidOperationException -> -32603.
        handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Fail("Revit is not running"));

        var (server, _) = BuildServer(handler, resources: new IMcpResource[]
        {
            new SnapshotLatestResource(MakeClient(handler)),
        }, output: out var output);

        await server.HandleLineAsync(Request(7, "resources/read", new JsonObject
        {
            ["uri"] = "revitcli://snapshot/latest",
        }), CancellationToken.None);

        var response = ReadOneResponse(output);
        Assert.Null(response["result"]);
        Assert.Equal(-32603, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("Revit is not running", response["error"]!["message"]!.GetValue<string>());
    }

    // -- helpers ------------------------------------------------------------

    private static (McpServer Server, RevitClient Client) BuildServer(out StringWriter output)
    {
        var handler = new QueueHttpHandler();
        return BuildServer(handler, resources: DefaultResources(handler), output: out output);
    }

    private static (McpServer Server, RevitClient Client) BuildServer(
        QueueHttpHandler handler,
        IEnumerable<IMcpResource> resources,
        out StringWriter output)
    {
        var client = MakeClient(handler);
        var tools = Array.Empty<IMcpTool>();
        var input = new StringReader(string.Empty);
        output = new StringWriter();
        var server = new McpServer(tools, resources, input, output, TextWriter.Null);
        return (server, client);
    }

    private static IEnumerable<IMcpResource> DefaultResources(QueueHttpHandler handler)
    {
        yield return new SnapshotLatestResource(MakeClient(handler));
        yield return new HistoryListResource(new HistoryStore(Path.Combine(Path.GetTempPath(), "revitcli-mcp-empty-" + Guid.NewGuid().ToString("N"))));
        yield return new ProfileResource(() => null);
    }

    private static RevitClient MakeClient(QueueHttpHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });

    private static string Request(int id, string method, JsonNode? @params = null)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (@params is not null) obj["params"] = @params;
        return obj.ToJsonString();
    }

    private static JsonNode ReadOneResponse(StringWriter output)
    {
        var raw = output.ToString().Trim();
        Assert.False(string.IsNullOrEmpty(raw), "expected one response on the writer");
        var line = raw.Split('\n', 2)[0].TrimEnd('\r');
        return JsonNode.Parse(line)!;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "revitcli-mcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
