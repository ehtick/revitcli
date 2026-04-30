using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Plans;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class ImportCommandTests : IDisposable
{
    private readonly string _tempDir;

    public ImportCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"revitcli-import-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteCsv(string name, string contents, Encoding? enc = null)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, (enc ?? new UTF8Encoding(false)).GetBytes(contents));
        return path;
    }

    private (RevitClient client, FakeHandler handler) MakeClient(
        ElementInfo[] queryElements,
        Func<SetRequest, SetResult>? setHandler = null)
    {
        var handler = new FakeHandler(queryElements, setHandler);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return (new RevitClient(http), handler);
    }

    [Fact]
    public async Task Execute_FileMissing_Returns1()
    {
        var (client, _) = MakeClient(Array.Empty<ElementInfo>());
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, file: Path.Combine(_tempDir, "nope.csv"),
            category: "doors", matchBy: "Mark",
            rawMap: null, dryRun: false,
            onMissing: "warn", onDuplicate: "error",
            encodingHint: "auto", batchSize: 100,
            output: sw);
        Assert.Equal(1, code);
        Assert.Contains("not found", sw.ToString());
    }

    [Fact]
    public async Task Execute_InvalidOnMissing_Returns1()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,X\n");
        var (client, _) = MakeClient(Array.Empty<ElementInfo>());
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "wrong", "error", "auto", 100, sw);
        Assert.Equal(1, code);
        Assert.Contains("--on-missing", sw.ToString());
    }

    [Fact]
    public async Task Execute_InvalidBatchSize_Returns1()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,X\n");
        var (client, _) = MakeClient(Array.Empty<ElementInfo>());
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", 0, sw);
        Assert.Equal(1, code);
        Assert.Contains("--batch-size", sw.ToString());
    }

    [Fact]
    public async Task Execute_MatchByMissingFromHeaders_Returns1()
    {
        var path = WriteCsv("a.csv", "Tag,Lock\nW01,X\n");
        var (client, _) = MakeClient(Array.Empty<ElementInfo>());
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", 100, sw);
        Assert.Equal(1, code);
        Assert.Contains("Mark", sw.ToString());
    }

    [Fact]
    public async Task Execute_OnlyMatchByColumn_Returns1_NoWritableColumns()
    {
        var path = WriteCsv("a.csv", "Mark\nW01\n");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } }
        };
        var (client, _) = MakeClient(elements);
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", 100, sw);
        Assert.Equal(1, code);
        Assert.Contains("no writable columns", sw.ToString());
    }

    [Fact]
    public async Task Execute_DryRun_NoSetCalls_Returns0_WithPreview()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,YALE-500\nW02,YALE-500\n");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } },
            new ElementInfo { Id = 102, Parameters = new() { ["Mark"] = "W02" } }
        };
        var (client, handler) = MakeClient(elements);
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, dryRun: true,
            "warn", "error", "auto", 100, sw);

        Assert.Equal(0, code);
        Assert.Equal(0, handler.SetCalls);
        Assert.Contains("Dry run:", sw.ToString());
        Assert.Contains("[Lock] = 'YALE-500'", sw.ToString());
    }

    [Fact]
    public async Task Execute_PlanOutput_WritesPreviewedImportPlan()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,YALE-500\nW02,YALE-500\n");
        var planPath = Path.Combine(_tempDir, "import.plan.json");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Name = "Door 1", Parameters = new() { ["Mark"] = "W01" } },
            new ElementInfo { Id = 102, Name = "Door 2", Parameters = new() { ["Mark"] = "W02" } }
        };
        var captured = new List<SetRequest>();
        var (client, handler) = MakeClient(elements, req =>
        {
            captured.Add(req);
            return new SetResult
            {
                Affected = req.ElementIds!.Count,
                Preview = req.ElementIds.Select(id => new SetPreviewItem
                {
                    Id = id,
                    Name = $"Door {id}",
                    OldValue = "OLD",
                    NewValue = req.Value
                }).ToList()
            };
        });
        var sw = new StringWriter();

        var code = await ImportCommand.ExecuteAsync(
            client,
            path,
            "doors",
            "Mark",
            null,
            dryRun: false,
            onMissing: "warn",
            onDuplicate: "error",
            encodingHint: "auto",
            batchSize: 100,
            output: sw,
            planOutputPath: planPath);

        Assert.Equal(0, code);
        Assert.Equal(1, handler.SetCalls);
        Assert.True(captured[0].DryRun);
        Assert.True(File.Exists(planPath));
        Assert.Contains("Plan written", sw.ToString());
        Assert.Contains("revitcli plan show", sw.ToString());

        var plan = SetPlanFileStore.LoadImport(planPath);
        Assert.Equal("import", plan.Type);
        Assert.Equal(1, plan.Summary.GroupCount);
        Assert.Equal(2, plan.Summary.ElementWrites);
        Assert.Equal("doors", plan.Summary.Category);
        Assert.Equal("Mark", plan.Summary.MatchBy);
        Assert.Equal("Lock", plan.Groups[0].Param);
        Assert.Equal("YALE-500", plan.Groups[0].Value);
        Assert.Equal(new List<long> { 101, 102 }, plan.Groups[0].ElementIds);
        Assert.Equal(2, plan.PreviewGroups[0].Preview.Count);
    }

    [Fact]
    public async Task Execute_PlanOutput_PreviewFailure_Returns1WithoutPlan()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,YALE-500\n");
        var planPath = Path.Combine(_tempDir, "import.plan.json");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Name = "Door 1", Parameters = new() { ["Mark"] = "W01" } }
        };
        var (client, _) = MakeClient(elements, _ => throw new InvalidOperationException("parameter is read-only"));
        var sw = new StringWriter();

        var code = await ImportCommand.ExecuteAsync(
            client,
            path,
            "doors",
            "Mark",
            null,
            dryRun: false,
            onMissing: "warn",
            onDuplicate: "error",
            encodingHint: "auto",
            batchSize: 100,
            output: sw,
            planOutputPath: planPath);

        Assert.Equal(1, code);
        Assert.False(File.Exists(planPath));
        Assert.Contains("preview failed", sw.ToString());
        Assert.Contains("parameter is read-only", sw.ToString());
    }

    [Fact]
    public async Task Execute_RealRun_GroupsAndIssuesBatchSetRequests()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,A\nW02,A\nW03,B\n");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } },
            new ElementInfo { Id = 102, Parameters = new() { ["Mark"] = "W02" } },
            new ElementInfo { Id = 103, Parameters = new() { ["Mark"] = "W03" } }
        };
        var captured = new List<SetRequest>();
        var (client, handler) = MakeClient(elements, req =>
        {
            captured.Add(req);
            return new SetResult { Affected = req.ElementIds!.Count };
        });
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", 100, sw);

        Assert.Equal(0, code);
        Assert.Equal(2, captured.Count);
        var groupA = captured.Find(r => r.Value == "A")!;
        Assert.Equal(new List<long> { 101, 102 }, groupA.ElementIds);
        var groupB = captured.Find(r => r.Value == "B")!;
        Assert.Equal(new List<long> { 103 }, groupB.ElementIds);
    }

    [Fact]
    public async Task Execute_BatchSizeChunksLargeGroup()
    {
        var sb = new StringBuilder("Mark,Lock\n");
        for (var i = 0; i < 5; i++) sb.Append($"W{i:D2},A\n");
        var path = WriteCsv("a.csv", sb.ToString());

        var elements = new ElementInfo[5];
        for (var i = 0; i < 5; i++)
            elements[i] = new ElementInfo { Id = 100 + i, Parameters = new() { ["Mark"] = $"W{i:D2}" } };

        var captured = new List<SetRequest>();
        var (client, _) = MakeClient(elements, req =>
        {
            captured.Add(req);
            return new SetResult { Affected = req.ElementIds!.Count };
        });
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", batchSize: 2, sw);

        Assert.Equal(0, code);
        Assert.Equal(3, captured.Count); // 5 ids / batch=2 → 2,2,1
        Assert.Equal(2, captured[0].ElementIds!.Count);
        Assert.Equal(2, captured[1].ElementIds!.Count);
        Assert.Single(captured[2].ElementIds!);
    }

    [Fact]
    public async Task Execute_OnMissingError_WithMiss_Returns1_NoSetCalls()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW99,X\n");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } }
        };
        var (client, handler) = MakeClient(elements);
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "error", "error", "auto", 100, sw);

        Assert.Equal(1, code);
        Assert.Equal(0, handler.SetCalls);
        Assert.Contains("Misses", sw.ToString());
    }

    [Fact]
    public async Task Execute_OnDuplicateFirst_PicksLowestId_Succeeds()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,X\n");
        var elements = new[]
        {
            new ElementInfo { Id = 102, Parameters = new() { ["Mark"] = "W01" } },
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } }
        };
        var captured = new List<SetRequest>();
        var (client, _) = MakeClient(elements, req =>
        {
            captured.Add(req);
            return new SetResult { Affected = req.ElementIds!.Count };
        });
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "first", "auto", 100, sw);

        Assert.Equal(0, code);
        var sole = Assert.Single(captured);
        Assert.Equal(new List<long> { 101 }, sole.ElementIds);
    }

    [Fact]
    public async Task Execute_PartialFailure_Returns2_AggregatesFailedGroups()
    {
        var path = WriteCsv("a.csv", "Mark,Lock\nW01,A\nW02,B\n");
        var elements = new[]
        {
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } },
            new ElementInfo { Id = 102, Parameters = new() { ["Mark"] = "W02" } }
        };
        var (client, _) = MakeClient(elements, req =>
        {
            if (req.Value == "B") throw new InvalidOperationException("locked");
            return new SetResult { Affected = req.ElementIds!.Count };
        });
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", 100, sw);

        Assert.Equal(2, code);
        var text = sw.ToString();
        Assert.Contains("Failed:", text);
        Assert.Contains("Lock=B", text);
    }

    [Fact]
    public async Task Execute_GbkChineseFile_ParsedAndApplied()
    {
        var gbk = Encoding.GetEncoding("gbk");
        var path = WriteCsv("gbk.csv", "Mark,锁具型号\nW01,YALE-500\n", gbk);

        var elements = new[]
        {
            new ElementInfo { Id = 101, Parameters = new() { ["Mark"] = "W01" } }
        };
        var captured = new List<SetRequest>();
        var (client, _) = MakeClient(elements, req =>
        {
            captured.Add(req);
            return new SetResult { Affected = req.ElementIds!.Count };
        });
        var sw = new StringWriter();
        var code = await ImportCommand.ExecuteAsync(
            client, path, "doors", "Mark", null, false, "warn", "error", "auto", 100, sw);

        Assert.Equal(0, code);
        var soleCapture = Assert.Single(captured);
        Assert.Equal("锁具型号", soleCapture.Param);
        Assert.Equal("YALE-500", soleCapture.Value);
        Assert.Contains("encoding=gbk", sw.ToString());
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly ElementInfo[] _elements;
        private readonly Func<SetRequest, SetResult>? _setHandler;
        public int SetCalls { get; private set; }

        public FakeHandler(ElementInfo[] elements, Func<SetRequest, SetResult>? setHandler)
        {
            _elements = elements;
            _setHandler = setHandler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (path == "/api/elements" && request.Method == HttpMethod.Get)
            {
                var resp = new ApiResponse<ElementInfo[]> { Success = true, Data = _elements };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp))
                };
            }
            if (path == "/api/elements/set" && request.Method == HttpMethod.Post)
            {
                SetCalls++;
                var body = await request.Content!.ReadAsStringAsync(ct);
                var req = JsonSerializer.Deserialize<SetRequest>(body, jsonOpts)!;
                try
                {
                    var data = _setHandler != null ? _setHandler(req) : new SetResult();
                    var resp = new ApiResponse<SetResult> { Success = true, Data = data };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(resp))
                    };
                }
                catch (Exception ex)
                {
                    var resp = new ApiResponse<SetResult> { Success = false, Error = ex.Message };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(resp))
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
