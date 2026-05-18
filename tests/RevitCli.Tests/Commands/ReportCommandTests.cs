using System.Text.Json;
using RevitCli.Commands;
using RevitCli.History;
using RevitCli.Shared;

namespace RevitCli.Tests.Commands;

public sealed class ReportCommandTests : IDisposable
{
    private readonly string _root;
    private readonly DateTimeOffset _now = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    public ReportCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-report-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Weekly_Table_IncludesHistoryDiffAndJournalSummary()
    {
        await SeedHistoryAsync();
        SeedJournal();
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "table",
            reportPath: null,
            output,
            _now);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Weekly report", text);
        Assert.Contains("History snapshots: 2", text);
        Assert.Contains("Diff review: highest=routine, changes=1", text);
        Assert.Contains("Journal entries: 2; affected elements: 8", text);
        Assert.Contains("publish: 1", text);
        Assert.Contains("set: 1", text);
    }

    [Fact]
    public async Task Weekly_Json_IncludesStructuredSections()
    {
        await SeedHistoryAsync();
        SeedJournal();
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "json",
            reportPath: null,
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("snapshotCount").GetInt32());
        Assert.Equal(1, root.GetProperty("diffReview").GetProperty("totalChanges").GetInt32());
        Assert.Equal(2, root.GetProperty("journal").GetProperty("entryCount").GetInt32());
    }

    [Fact]
    public async Task Weekly_ReportPath_WritesMarkdown()
    {
        await SeedHistoryAsync();
        SeedJournal();
        var reportPath = Path.Combine(_root, ".revitcli", "reports", "weekly.md");
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "table",
            reportPath,
            output,
            _now);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath));
        var markdown = File.ReadAllText(reportPath);
        Assert.StartsWith("# Weekly RevitCli Report", markdown);
        Assert.Contains("## Diff Review", markdown);
        Assert.Contains("## Journal", markdown);
        Assert.Contains("Report saved to", output.ToString());
    }

    [Fact]
    public async Task Weekly_MissingHistory_ReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteWeeklyAsync(
            "7d",
            _root,
            historyDirectory: null,
            journalPath: null,
            outputFormat: "table",
            reportPath: null,
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("history store not initialised", output.ToString());
    }

    [Fact]
    public async Task Knowledge_Markdown_IncludesLocalArtifactsAndReuseHints()
    {
        await SeedHistoryAsync();
        SeedKnowledgeJournal();
        SeedWorkflowReceipt(success: false);
        SeedDeliveryManifest();
        SeedStandards();
        SeedWeeklyReport();
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteKnowledgeAsync(
            _root,
            historyDirectory: null,
            journalPath: null,
            standardsManifestPath: null,
            outputFormat: "markdown",
            reportPath: null,
            output,
            _now);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Knowledge Report", text);
        Assert.Contains("| History | `present` | 2 snapshots; latest", text);
        Assert.Contains("| Journal | `present` | 4 entries; 1 repeated workflow suggestions |", text);
        Assert.Contains("| Workflow Receipts | `present` | 1 receipts; 1 failed |", text);
        Assert.Contains("| Delivery Receipts | `present` | 1 entries; valid true; 0 missing receipts |", text);
        Assert.Contains("| Standards Validation | `present` | valid true; pack 2026.4.0; 0 errors |", text);
        Assert.Contains("| Weekly Reports | `present` | 1 reports; latest", text);
        Assert.Contains("revitcli workflow suggest --output yaml", text);
        Assert.Contains("revitcli workflow receipts --failed-only --output markdown", text);
    }

    [Fact]
    public async Task Knowledge_Json_IncludesStructuredArtifactCounts()
    {
        await SeedHistoryAsync();
        SeedKnowledgeJournal();
        SeedWorkflowReceipt(success: false);
        SeedDeliveryManifest();
        SeedStandards();
        SeedWeeklyReport();
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteKnowledgeAsync(
            _root,
            historyDirectory: null,
            journalPath: null,
            standardsManifestPath: null,
            outputFormat: "json",
            reportPath: null,
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("knowledge-report.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, root.GetProperty("history").GetProperty("snapshotCount").GetInt32());
        Assert.Equal(1, root.GetProperty("journal").GetProperty("repeatedWorkflowSuggestionCount").GetInt32());
        Assert.Equal(1, root.GetProperty("workflowReceipts").GetProperty("failedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("deliveries").GetProperty("entryCount").GetInt32());
        Assert.True(root.GetProperty("standards").GetProperty("valid").GetBoolean());
        Assert.Equal(1, root.GetProperty("weeklyReports").GetProperty("reportCount").GetInt32());
        Assert.Equal("# Weekly RevitCli Report", root.GetProperty("weeklyReports").GetProperty("latestTitle").GetString());
        Assert.Contains(root.GetProperty("reuseHints").EnumerateArray(), hint =>
            hint.GetProperty("code").GetString() == "workflow.suggest");
    }

    [Fact]
    public async Task Knowledge_Table_MissingArtifactsReturnsBootstrapHints()
    {
        var output = new StringWriter();

        var exitCode = await ReportCommand.ExecuteKnowledgeAsync(
            _root,
            historyDirectory: null,
            journalPath: null,
            standardsManifestPath: null,
            outputFormat: "table",
            reportPath: null,
            output,
            _now);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Knowledge report", text);
        Assert.Contains("History: missing", text);
        Assert.Contains("Journal: missing", text);
        Assert.Contains("history.init", text);
        Assert.Contains("standards.bootstrap", text);
    }

    private async Task SeedHistoryAsync()
    {
        var store = HistoryStore.ForProject(_root);
        await store.InitAsync();
        await store.AppendAsync(
            Snapshot((1, "A", "h1")),
            "weekly-health",
            _now.AddDays(-5));
        await store.AppendAsync(
            Snapshot((1, "A", "h1"), (2, "B", "h2")),
            "weekly-health",
            _now.AddDays(-1));
    }

    private void SeedJournal()
    {
        var dir = Path.Combine(_root, ".revitcli");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(
            Path.Combine(dir, "journal.jsonl"),
            new[]
            {
                $$"""{"timestamp":"{{_now.AddDays(-2).ToString("o")}}","action":"publish","category":"sheets","user":"alice","exported":3}""",
                $$"""{"timestamp":"{{_now.AddDays(-1).ToString("o")}}","action":"set","category":"walls","user":"bob","affected":5}""",
                $$"""{"timestamp":"{{_now.AddDays(-20).ToString("o")}}","action":"publish","category":"old","user":"alice","exported":99}""",
            });
    }

    private void SeedKnowledgeJournal()
    {
        var dir = Path.Combine(_root, ".revitcli");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(
            Path.Combine(dir, "journal.jsonl"),
            new[]
            {
                $$"""{"timestamp":"{{_now.AddDays(-2).ToString("o")}}","action":"check","category":"issue","user":"alice","operator":"alice","command":"revitcli check issue","affected":0}""",
                $$"""{"timestamp":"{{_now.AddDays(-2).AddMinutes(1).ToString("o")}}","action":"publish","category":"issue","user":"alice","operator":"alice","command":"revitcli publish issue --dry-run","affected":0}""",
                $$"""{"timestamp":"{{_now.AddDays(-1).ToString("o")}}","action":"check","category":"issue","user":"alice","operator":"alice","command":"revitcli check issue","affected":0}""",
                $$"""{"timestamp":"{{_now.AddDays(-1).AddMinutes(1).ToString("o")}}","action":"publish","category":"issue","user":"alice","operator":"alice","command":"revitcli publish issue --dry-run","affected":0}""",
            });
    }

    private void SeedWorkflowReceipt(bool success)
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        Directory.CreateDirectory(receiptDir);
        File.WriteAllText(
            Path.Combine(receiptDir, "pre-issue-20260505T120000Z.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = "workflow-run-receipt.v1",
                action = "workflow.run",
                path = Path.Combine(_root, ".revitcli", "workflows", "pre-issue.yml"),
                name = "pre-issue",
                command = "revitcli workflow run .revitcli/workflows/pre-issue.yml --yes",
                startedAtUtc = _now.AddMinutes(-5).ToString("o"),
                completedAtUtc = _now.ToString("o"),
                @operator = "alice",
                machine = "workstation",
                dryRun = false,
                success,
                canRun = true,
                exitCode = success ? 0 : 7,
                issues = Array.Empty<object>(),
                steps = new[]
                {
                    new
                    {
                        index = 1,
                        name = "check",
                        mode = "read-only",
                        run = "revitcli check issue",
                        requiresApproval = false,
                        status = success ? "ok" : "failed",
                        exitCode = success ? 0 : 7,
                    }
                }
            }));
    }

    private void SeedDeliveryManifest()
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, "publish-issue.json");
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "publish-receipt.v1",
                action = "publish",
                success = true,
                dryRun = false,
                command = "revitcli publish issue",
            }));

        var manifestDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllLines(
            Path.Combine(manifestDir, "manifest.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath,
                    timestamp = _now.ToString("o"),
                })
            });
    }

    private void SeedStandards()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".revitcli", "workflows"));
        Directory.CreateDirectory(Path.Combine(_root, "deliverables"));
        File.WriteAllText(Path.Combine(_root, ".revitcli.yml"), """
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  issue:
    precheck: default
    presets: [dwg]
schedules:
  doors:
    category: doors
    fields: [Mark, Fire Rating]
    name: Door Schedule
""");
        File.WriteAllText(Path.Combine(_root, ".revitcli", "workflows", "pre-issue.yml"), """
version: 1
name: pre-issue
steps:
  - run: revitcli check issue --output table
    mode: read-only
  - run: revitcli publish issue --dry-run
    mode: dry-run
""");
        File.WriteAllText(Path.Combine(_root, ".revitcli", "standards.yml"), """
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty]
""");
    }

    private void SeedWeeklyReport()
    {
        var reportDir = Path.Combine(_root, ".revitcli", "reports");
        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, "weekly.md"), "# Weekly RevitCli Report");
    }

    private static ModelSnapshot Snapshot(params (long Id, string Mark, string Hash)[] walls)
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-05-05T00:00:00Z",
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Sample.rvt",
                DocumentPath = "C:/models/Sample.rvt",
            },
            Summary = new SnapshotSummary
            {
                ElementCounts = new Dictionary<string, int> { ["walls"] = walls.Length },
                SheetCount = 1,
                ScheduleCount = 1,
            },
            Sheets =
            {
                new SnapshotSheet
                {
                    Number = "A-101",
                    Name = "Plan",
                    ViewId = 100,
                    MetaHash = "sheet-hash",
                }
            },
            Schedules =
            {
                new SnapshotSchedule
                {
                    Id = 200,
                    Name = "Door Schedule",
                    Category = "doors",
                    RowCount = 1,
                    Hash = "schedule-hash",
                }
            },
        };

        snapshot.Categories["walls"] = walls
            .Select(item => new SnapshotElement
            {
                Id = item.Id,
                Name = $"W{item.Id}",
                Parameters = new Dictionary<string, string> { ["Mark"] = item.Mark },
                Hash = item.Hash,
            })
            .ToList();
        return snapshot;
    }
}
