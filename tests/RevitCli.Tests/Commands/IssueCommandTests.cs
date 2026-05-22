using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class IssueCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public IssueCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-issue-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousDirectory);
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Preflight_JsonReportsHiddenMutation()
    {
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
checks:
  - name: unsafe set
    command: revitcli set doors --param Mark --value A-101
""");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePreflightAsync(
            profilePath,
            outputFormat: "json",
            failOn: "error",
            writer);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Equal("issue-preflight-report.v1", root.GetProperty("schemaVersion").GetString());
        Assert.False(root.GetProperty("noHiddenMutation").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "hidden-model-mutation");
    }

    [Fact]
    public async Task Preflight_AllowsReviewedPlanApplyCommand()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "sheet-issue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath, "{}");
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
mutationPlans:
  - name: sheet issue metadata
    planPath: .revitcli/plans/sheet-issue.json
checks:
  - name: reviewed apply
    command: revitcli plan apply .revitcli/plans/sheet-issue.json --yes
""");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePreflightAsync(
            profilePath,
            outputFormat: "json",
            failOn: "error",
            writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.True(json.RootElement.GetProperty("noHiddenMutation").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Preflight_AllowsReviewedPlanApplyWhenOptionsPrecedePlanPath()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "sheet-issue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath, "{}");
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
mutationPlans:
  - name: sheet issue metadata
    planPath: .revitcli/plans/sheet-issue.json
checks:
  - name: reviewed apply with threshold
    command: revitcli plan apply --yes true --max-changes 250 --confirm-high-impact true .revitcli/plans/sheet-issue.json
""");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePreflightAsync(
            profilePath,
            outputFormat: "json",
            failOn: "error",
            writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.True(json.RootElement.GetProperty("noHiddenMutation").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Preflight_BlocksUnreviewedPlanApplyWithExplicitDryRunFalse()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "rogue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath, "{}");
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
checks:
  - name: rogue apply
    command: revitcli plan apply --dry-run false --yes true .revitcli/plans/rogue.json
""");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePreflightAsync(
            profilePath,
            outputFormat: "json",
            failOn: "error",
            writer);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "hidden-model-mutation" &&
            issue.GetProperty("path").GetString()!.EndsWith(".revitcli/plans/rogue.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preflight_BlocksPlanApplyWithoutDeclaredMutationPlan()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "rogue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath, "{}");
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
checks:
  - name: rogue apply
    command: revitcli plan apply .revitcli/plans/rogue.json --yes
""");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePreflightAsync(
            profilePath,
            outputFormat: "json",
            failOn: "error",
            writer);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("noHiddenMutation").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "hidden-model-mutation" &&
            issue.GetProperty("message").GetString()!.Contains("mutationPlans", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Diff_JsonWrapsSnapshotReviewAsIssueDiffReport()
    {
        var fromPath = Path.Combine(_root, "baseline.json");
        var toPath = Path.Combine(_root, "current.json");
        await File.WriteAllTextAsync(fromPath, JsonSerializer.Serialize(Snapshot(("doors", 10, "A", "h1"))));
        await File.WriteAllTextAsync(toPath, JsonSerializer.Serialize(Snapshot(("doors", 10, "B", "h2"))));
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecuteDiffAsync(
            MakeClient(),
            fromPath,
            toPath,
            review: true,
            outputFormat: "json",
            reportPath: null,
            maxRows: 20,
            writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Equal("issue-diff-report.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, root.GetProperty("totalChanges").GetInt32());
        Assert.True(root.GetProperty("review").GetBoolean());
        Assert.NotEmpty(root.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Package_DryRunJsonPlansDeliverablesAndReceipts()
    {
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
checks:
  - name: issue exports
    command: revitcli publish issue --dry-run --output json
package:
  commands:
    - revitcli schedules batch-export --set issue --output-dir exports/schedules/current --format csv --manifest exports/schedules/current/manifest.json --output json
    - revitcli deliverables bundle --dry-run --output markdown
""");
        WriteDeliveryEvidence();
        var bundlePath = Path.Combine(_root, "deliverables", "issue-package.zip");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePackageAsync(
            profilePath,
            bundlePath,
            dryRun: true,
            signJournal: true,
            includeReceipts: true,
            outputFormat: "json",
            writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Equal("issue-package-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("bundleWritten").GetBoolean());
        Assert.False(root.GetProperty("receiptWritten").GetBoolean());
        Assert.False(File.Exists(bundlePath));
        Assert.Equal(1, root.GetProperty("receiptCount").GetInt32());
        Assert.Contains(root.GetProperty("plannedActions").EnumerateArray(), action => action.GetString() == "preflight-checks");
        Assert.Contains(root.GetProperty("plannedActions").EnumerateArray(), action => action.GetString() == "schedule-export");
        Assert.Contains(root.GetProperty("plannedActions").EnumerateArray(), action => action.GetString() == "export");
        Assert.Contains(root.GetProperty("plannedActions").EnumerateArray(), action => action.GetString() == "bundle");
        Assert.Contains(root.GetProperty("plannedActions").EnumerateArray(), action => action.GetString() == "journal-sign");
        Assert.True(root.GetProperty("preflightCheckCount").GetInt32() >= 1);
        Assert.Contains(root.GetProperty("files").EnumerateArray(), file =>
            file.GetProperty("archivePath").GetString() == "deliverables/pdf/A101.pdf");
    }

    [Fact]
    public async Task Package_BlocksMissingReviewedMutationPlan()
    {
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
mutationPlans:
  - name: sheet issue metadata
    planPath: .revitcli/plans/missing.json
""");
        WriteDeliveryEvidence();
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePackageAsync(
            profilePath,
            Path.Combine(_root, "deliverables", "issue-package.zip"),
            dryRun: true,
            signJournal: false,
            includeReceipts: true,
            outputFormat: "json",
            writer);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("preflightErrorCount").GetInt32());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "mutation-plan-missing");
        Assert.False(File.Exists(Path.Combine(_root, "deliverables", "issue-package.zip")));
    }

    [Fact]
    public async Task Package_BlocksUnreviewedPlanApplyCommand()
    {
        var profilePath = WriteIssueProfile("""
schemaVersion: issue-profile.v1
package:
  commands:
    - revitcli plan apply .revitcli/plans/rogue.json --yes
""");
        WriteDeliveryEvidence();
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePackageAsync(
            profilePath,
            Path.Combine(_root, "deliverables", "issue-package.zip"),
            dryRun: true,
            signJournal: false,
            includeReceipts: true,
            outputFormat: "json",
            writer);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "hidden-model-mutation" &&
            issue.GetProperty("command").GetString()!.Contains("plan apply", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(_root, "deliverables", "issue-package.zip")));
    }

    [Fact]
    public async Task Package_ApplyWritesZipAndReceiptWithBundleHash()
    {
        var profilePath = WriteIssueProfile("schemaVersion: issue-profile.v1\n");
        WriteDeliveryEvidence();
        var bundlePath = Path.Combine(_root, "deliverables", "issue-package.zip");
        var writer = new StringWriter();

        var exitCode = await IssueCommand.ExecutePackageAsync(
            profilePath,
            bundlePath,
            dryRun: false,
            signJournal: false,
            includeReceipts: true,
            outputFormat: "json",
            writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("bundleWritten").GetBoolean());
        Assert.True(root.GetProperty("receiptWritten").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("bundleHash").GetString()));
        var receiptPath = root.GetProperty("receiptPath").GetString()!;
        Assert.True(File.Exists(bundlePath));
        Assert.True(File.Exists(receiptPath));
        using var receiptJson = JsonDocument.Parse(File.ReadAllText(receiptPath));
        Assert.True(receiptJson.RootElement.GetProperty("receiptWritten").GetBoolean());
    }

    private string WriteIssueProfile(string yaml)
    {
        var path = Path.Combine(_root, ".revitcli", "issue.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
        return path;
    }

    private void WriteDeliveryEvidence()
    {
        var outputDir = Path.Combine(_root, "deliverables", "pdf");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
        var receiptPath = Path.Combine(_root, ".revitcli", "receipts", "export.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "export-receipt.v1",
            action = "export",
            success = true,
            dryRun = false,
            outputDir
        }));
        var manifestPath = Path.Combine(_root, ".revitcli", "deliveries", "manifest.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "delivery-manifest.v1",
            kind = "export",
            success = true,
            dryRun = false,
            format = "pdf",
            receiptPath,
            timestamp = "2026-05-21T00:00:00Z"
        }) + Environment.NewLine);
    }

    private static ModelSnapshot Snapshot(params (string Category, long Id, string Mark, string Hash)[] elements)
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-05-21T00:00:00Z",
            Revit = new SnapshotRevit { Version = "2026", Document = "test", DocumentPath = "test.rvt" }
        };
        foreach (var element in elements)
        {
            if (!snapshot.Categories.TryGetValue(element.Category, out var list))
            {
                list = new List<SnapshotElement>();
                snapshot.Categories[element.Category] = list;
            }

            list.Add(new SnapshotElement
            {
                Id = element.Id,
                Name = $"Element {element.Id}",
                Hash = element.Hash,
                Parameters = { ["Mark"] = element.Mark }
            });
        }

        return snapshot;
    }

    private static RevitClient MakeClient() =>
        new(new HttpClient(new NotCalledHandler()) { BaseAddress = new Uri("http://localhost:17839") });

    private sealed class NotCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("not expected", Encoding.UTF8, "text/plain")
            });
    }
}
