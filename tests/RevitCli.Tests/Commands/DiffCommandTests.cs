using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class DiffCommandTests
{
    private static string WriteSnapshot(string path, ModelSnapshot s)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(s));
        return path;
    }

    private static ModelSnapshot Fixture(params (long id, string mark, string hash)[] walls)
    {
        var s = new ModelSnapshot { SchemaVersion = 1,
            Revit = new SnapshotRevit { DocumentPath = "/a.rvt" } };
        if (walls.Length > 0)
        {
            var list = new List<SnapshotElement>();
            foreach (var (id, mark, hash) in walls)
                list.Add(new SnapshotElement { Id = id, Name = $"E{id}",
                    Parameters = new() { ["Mark"] = mark }, Hash = hash });
            s.Categories["walls"] = list;
        }
        return s;
    }

    [Fact]
    public async Task Diff_TwoSnapshots_PrintsSummary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-test-diff-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var a = WriteSnapshot(Path.Combine(dir, "a.json"), Fixture((1, "A", "h1")));
        var b = WriteSnapshot(Path.Combine(dir, "b.json"), Fixture((1, "A", "h1"), (2, "B", "h2")));
        var writer = new StringWriter();

        var exitCode = await DiffCommand.ExecuteAsync(a, b, "table", null, null, 20, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("walls", writer.ToString());
        Assert.Contains("+1", writer.ToString());

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Diff_JsonOutput_IsValidJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-test-diff-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var a = WriteSnapshot(Path.Combine(dir, "a.json"), Fixture((1, "A", "h1")));
        var b = WriteSnapshot(Path.Combine(dir, "b.json"), Fixture((1, "A", "h1")));
        var writer = new StringWriter();

        var exitCode = await DiffCommand.ExecuteAsync(a, b, "json", null, null, 20, writer);

        Assert.Equal(0, exitCode);
        var parsed = JsonSerializer.Deserialize<SnapshotDiff>(
            writer.ToString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(1, parsed.SchemaVersion);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Diff_FileNotExist_ReturnsOne()
    {
        var writer = new StringWriter();
        var exitCode = await DiffCommand.ExecuteAsync(
            "/does/not/exist/a.json", "/does/not/exist/b.json", "table", null, null, 20, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not found", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Diff_SchemaMismatch_ReturnsOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-test-diff-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var a = new ModelSnapshot { SchemaVersion = 1 };
        var b = new ModelSnapshot { SchemaVersion = 2 };
        var aPath = WriteSnapshot(Path.Combine(dir, "a.json"), a);
        var bPath = WriteSnapshot(Path.Combine(dir, "b.json"), b);
        var writer = new StringWriter();

        var exitCode = await DiffCommand.ExecuteAsync(aPath, bPath, "table", null, null, 20, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("schema mismatch", writer.ToString().ToLower());

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Diff_WithReportPath_WritesFileAndReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-test-diff-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var a = WriteSnapshot(Path.Combine(dir, "a.json"), Fixture((1, "A", "h1")));
        var b = WriteSnapshot(Path.Combine(dir, "b.json"), Fixture((1, "B", "h2")));
        var reportPath = Path.Combine(dir, "out.md");
        var writer = new StringWriter();

        var exitCode = await DiffCommand.ExecuteAsync(a, b, "table", reportPath, null, 20, writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath));
        var content = File.ReadAllText(reportPath);
        Assert.StartsWith("## Model changes", content); // format inferred from .md extension

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Diff_Review_PrintsAnomalySummary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-test-diff-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var a = WriteSnapshot(Path.Combine(dir, "a.json"), Fixture((1, "A", "h1"), (2, "B", "h2")));
        var b = WriteSnapshot(Path.Combine(dir, "b.json"), Fixture((1, "A", "h1")));
        var writer = new StringWriter();

        var exitCode = await DiffCommand.ExecuteAsync(a, b, "table", null, null, 20, writer, review: true);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Highest severity: anomaly", output);
        Assert.Contains("walls: 1 removed", output);
        Assert.Contains("Recommended actions", output);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Diff_ReviewJson_ContainsReviewGroups()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-test-diff-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var aSnap = Fixture((1, "90min", "h1"));
        aSnap.Categories["walls"][0].Parameters["Fire Rating"] = "90min";
        aSnap.Categories["walls"][0].Parameters.Remove("Mark");
        var bSnap = Fixture((1, "60min", "h2"));
        bSnap.Categories["walls"][0].Parameters["Fire Rating"] = "60min";
        bSnap.Categories["walls"][0].Parameters.Remove("Mark");
        var a = WriteSnapshot(Path.Combine(dir, "a.json"), aSnap);
        var bPath = WriteSnapshot(Path.Combine(dir, "b.json"), bSnap);
        var writer = new StringWriter();

        var exitCode = await DiffCommand.ExecuteAsync(a, bPath, "json", null, null, 20, writer, review: true);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("notable", json.RootElement.GetProperty("highestSeverity").GetString());
        var groups = json.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Contains(groups, group => group.GetProperty("parameter").GetString() == "Fire Rating");

        Directory.Delete(dir, recursive: true);
    }
}
