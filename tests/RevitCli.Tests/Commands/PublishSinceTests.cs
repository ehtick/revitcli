using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class PublishSinceTests
{
    private static string WriteFixture(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "revitcli-publish-since-" + System.Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    private static ModelSnapshot SnapWith(params (string number, string metaHash, string contentHash)[] sheets)
    {
        var s = new ModelSnapshot { SchemaVersion = 1, Revit = new SnapshotRevit { DocumentPath = "/a.rvt" } };
        foreach (var (num, mh, ch) in sheets)
            s.Sheets.Add(new SnapshotSheet { Number = num, Name = num, ViewId = 1000 + num.Length,
                MetaHash = mh, ContentHash = ch });
        return s;
    }

    [Fact]
    public async Task Publish_WithoutSince_FullExportAsUsual()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    presets: [dwg-all]
");
            var handler = new FakeHttpHandler(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Ok(new ExportProgress { Status = "completed", Progress = 100 })));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: false,
                since: null, sinceMode: null, updateBaseline: false,
                writer);

            Assert.Equal(0, exit);
            Assert.Contains("1 succeeded", writer.ToString());
            var receiptDir = Path.Combine(dir, ".revitcli", "receipts");
            var receiptPath = Assert.Single(Directory.GetFiles(receiptDir, "default-*.json"));
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            var root = receipt.RootElement;
            Assert.Equal("publish-receipt.v1", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("publish", root.GetProperty("action").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.False(root.GetProperty("dryRun").GetBoolean());
            Assert.Equal("default", root.GetProperty("pipeline").GetString());
            Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
            Assert.Equal(0, root.GetProperty("failed").GetInt32());
            Assert.Contains("revitcli publish", root.GetProperty("command").GetString()!);
            var export = Assert.Single(root.GetProperty("exports").EnumerateArray());
            Assert.Equal("dwg-all", export.GetProperty("preset").GetString());
            Assert.Equal("dwg", export.GetProperty("format").GetString());
            Assert.Equal(Path.GetFullPath(Path.Combine(dir, "out")), export.GetProperty("outputDir").GetString());
            Assert.True(export.GetProperty("success").GetBoolean());
            Assert.Equal("completed", export.GetProperty("status").GetString());

            var manifestPath = Path.Combine(dir, ".revitcli", "deliveries", "manifest.jsonl");
            var manifestLine = Assert.Single(File.ReadAllLines(manifestPath));
            using var manifest = JsonDocument.Parse(manifestLine);
            var manifestRoot = manifest.RootElement;
            Assert.Equal("delivery-manifest.v1", manifestRoot.GetProperty("schemaVersion").GetString());
            Assert.Equal("publish", manifestRoot.GetProperty("kind").GetString());
            Assert.Equal(Path.GetFullPath(receiptPath), manifestRoot.GetProperty("receiptPath").GetString());
            Assert.False(manifestRoot.GetProperty("dryRun").GetBoolean());
            Assert.Equal("default", manifestRoot.GetProperty("pipeline").GetString());
            Assert.Equal(1, manifestRoot.GetProperty("succeeded").GetInt32());
            Assert.Equal(0, manifestRoot.GetProperty("failed").GetInt32());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_DryRunJson_PrintsMachineReadablePlan()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [A-01, A-02]
    outputDir: ./out
publish:
  issue:
    presets: [dwg-all]
");
            var handler = new FakeHttpHandler("{}");
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "issue", profilePath: profilePath,
                dryRun: true,
                since: null, sinceMode: null, updateBaseline: false,
                writer,
                outputFormat: "json");

            Assert.Equal(0, exit);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.Equal("publish.v1", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.True(root.GetProperty("dryRun").GetBoolean());
            Assert.Equal("issue", root.GetProperty("pipeline").GetString());
            var planned = root.GetProperty("plannedExports");
            Assert.Single(planned.EnumerateArray());
            var export = planned[0];
            Assert.Equal("dwg-all", export.GetProperty("preset").GetString());
            Assert.Equal("dwg", export.GetProperty("format").GetString());
            Assert.Equal(2, export.GetProperty("sheets").GetArrayLength());
            Assert.Contains("out", export.GetProperty("outputDir").GetString()!);
            Assert.Equal(0, handler.CallCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_JsonOutputWithoutDryRun_ReturnsFailureBeforeHttp()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    presets: [dwg-all]
");
            var handler = new FakeHttpHandler("{}");
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: false,
                since: null, sinceMode: null, updateBaseline: false,
                writer,
                outputFormat: "json");

            Assert.Equal(1, exit);
            using var json = JsonDocument.Parse(writer.ToString());
            Assert.False(json.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains("--dry-run", json.RootElement.GetProperty("error").GetString()!);
            Assert.Equal(0, handler.CallCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_Since_NoSheetChanges_SkipsExport()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    presets: [dwg-all]
");
            var baseline = SnapWith(("A-01", "m1", "c1"), ("A-02", "m2", "c2"));
            var baselinePath = WriteFixture(dir, "baseline.json", JsonSerializer.Serialize(baseline));

            var handler = new FakeHttpHandler(JsonSerializer.Serialize(
                ApiResponse<ModelSnapshot>.Ok(baseline)));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: false,
                since: baselinePath, sinceMode: "content", updateBaseline: false,
                writer);

            Assert.Equal(0, exit);
            Assert.Contains("no sheets changed", writer.ToString().ToLower());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_Since_ContentChanged_FiltersToChangedSheets()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    presets: [dwg-all]
");
            var baseline = SnapWith(("A-01", "m1", "c1"), ("A-02", "m2", "c2"));
            var current  = SnapWith(("A-01", "m1", "c1"), ("A-02", "m2", "c2-CHANGED"));
            var baselinePath = WriteFixture(dir, "baseline.json", JsonSerializer.Serialize(baseline));

            var handler = new FakeHttpHandler(JsonSerializer.Serialize(
                ApiResponse<ModelSnapshot>.Ok(current)));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: true,
                since: baselinePath, sinceMode: "content", updateBaseline: false,
                writer);

            Assert.Equal(0, exit);
            var output = writer.ToString();
            Assert.Contains("A-02", output);
            Assert.Contains("dry-run", output);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_Since_BaselineMissing_ReturnsError()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    presets: [dwg-all]
");
            var handler = new FakeHttpHandler("{}");
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: true,
                since: Path.Combine(dir, "does-not-exist.json"),
                sinceMode: "content", updateBaseline: false,
                writer);

            Assert.Equal(1, exit);
            Assert.Contains("baseline not found", writer.ToString().ToLower());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_Since_NoSheetChanges_UpdateBaseline_WritesFreshSnapshot()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    presets: [dwg-all]
");
            var baseline = SnapWith(("A-01", "m1", "c1"));
            var current  = SnapWith(("A-01", "m1", "c1"));
            current.TakenAt = "2026-04-23T14:00:00Z"; // differs from baseline default (empty)
            var baselinePath = WriteFixture(dir, "baseline.json", JsonSerializer.Serialize(baseline));

            var handler = new FakeHttpHandler(JsonSerializer.Serialize(
                ApiResponse<ModelSnapshot>.Ok(current)));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: false,
                since: baselinePath, sinceMode: "content", updateBaseline: true,
                writer);

            Assert.Equal(0, exit);
            // Baseline file should now contain the *current* snapshot's TakenAt, not baseline's.
            var reloaded = JsonSerializer.Deserialize<ModelSnapshot>(
                File.ReadAllText(baselinePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal("2026-04-23T14:00:00Z", reloaded.TakenAt);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Publish_IncrementalProfile_UsesDefaultBaselinePath()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteFixture(dir, ".revitcli.yml", @"
version: 1
exports:
  dwg-all:
    format: dwg
    sheets: [all]
    outputDir: ./out
publish:
  default:
    incremental: true
    presets: [dwg-all]
");
            var baseline = SnapWith(("A-01", "m1", "c1"));
            var baselineDir = Path.Combine(dir, ".revitcli");
            Directory.CreateDirectory(baselineDir);
            File.WriteAllText(Path.Combine(baselineDir, "last-publish.json"),
                JsonSerializer.Serialize(baseline));

            var handler = new FakeHttpHandler(JsonSerializer.Serialize(
                ApiResponse<ModelSnapshot>.Ok(baseline)));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exit = await PublishCommand.ExecuteAsync(
                client, name: "default", profilePath: profilePath,
                dryRun: true,
                since: null, sinceMode: null, updateBaseline: false,
                writer);

            Assert.Equal(0, exit);
            Assert.Contains("no sheets changed", writer.ToString().ToLower());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
