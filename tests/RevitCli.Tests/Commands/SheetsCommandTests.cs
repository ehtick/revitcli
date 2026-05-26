using System.Net.Http;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using RevitCli.Tests.Client;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class SheetsCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public SheetsCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-sheets-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Verify_BuiltinDefaults_DetectsDuplicateNumbers()
    {
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Plan", PlacedViewIds = { 100 } },
                new SnapshotSheet { ViewId = 11, Number = "A-101", Name = "Duplicate", PlacedViewIds = { 101 } },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, againstPath: null, rule: null, issuesOnly: false, outputFormat: "json", output);

        Assert.Equal(3, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("sheets verify", json.RootElement.GetProperty("command").GetString());
        Assert.Equal("(builtin defaults)", json.RootElement.GetProperty("configSource").GetString());
        var issue = Assert.Single(json.RootElement.GetProperty("issues").EnumerateArray());
        Assert.Equal("numbering.duplicate", issue.GetProperty("rule").GetString());
        Assert.Equal("error", issue.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Verify_AgainstIndex_DetectsGapsRequiredAndViewCounts()
    {
        var indexPath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
numbering:
  scheme: "A-{floor:1}{seq:02}"
  ranges:
    - floors: [1]
      seqMin: 1
      seqMax: 3
required:
  - pattern: "A-101"
    description: "Level 1 plan"
    needsViews:
      - minCount: 2
  - pattern: "A-104"
    description: "Missing sheet"
""");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Level 1", PlacedViewIds = { 100 } },
                new SnapshotSheet { ViewId = 11, Number = "A-103", Name = "Level 1 Reflected", PlacedViewIds = { 101 } },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, indexPath, rule: null, issuesOnly: false, outputFormat: "json", output);

        Assert.Equal(3, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var rules = json.RootElement.GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("rule").GetString())
            .ToArray();
        Assert.Contains("numbering.gap", rules);
        Assert.Contains("required.missing", rules);
        Assert.Contains("required.viewMissing", rules);
        Assert.Equal(3, json.RootElement.GetProperty("summary").GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task Verify_IssuesOnly_HidesInfoIssuesFromOutputButKeepsExitCode()
    {
        var indexPath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
""");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Empty" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, indexPath, rule: "linkage.emptySheet", issuesOnly: true, outputFormat: "json", output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Empty(json.RootElement.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task Verify_UnknownRule_ReturnsCommandErrorBeforeHttp()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, againstPath: null, rule: "numbering.nope", issuesOnly: false, outputFormat: "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown sheet rule", output.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Verify_ModelUnavailable_ReturnsFour()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteVerifyAsync(
            client, againstPath: null, rule: null, issuesOnly: false, outputFormat: "table", output);

        Assert.Equal(4, exitCode);
        Assert.Contains("Error:", output.ToString());
    }

    [Fact]
    public async Task IssueMeta_DryRun_WritesFrozenPlan()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "sheet-issue.json");
        var client = MakeClient(new ModelSnapshot
        {
            Revit = { Document = "tower.rvt", DocumentPath = "D:\\models\\tower.rvt" },
            Model = { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters =
                    {
                        ["Sheet Issue Code"] = "R02",
                        ["Sheet Issue Date"] = "2026-05-01",
                    }
                },
                new SnapshotSheet
                {
                    ViewId = 11,
                    Number = "A-201",
                    Name = "Level 2",
                    Parameters =
                    {
                        ["Sheet Issue Code"] = "R03",
                        ["Sheet Issue Date"] = "2026-05-20",
                    }
                },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "A-101",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: planPath,
            paramMapPath: null,
            dryRun: true,
            outputFormat: "json",
            output: output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("sheet-issue-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("sheet-issue", json.RootElement.GetProperty("type").GetString());
        Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal("abc123", json.RootElement.GetProperty("modelFingerprint").GetProperty("fileHash").GetString());
        var actions = json.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Equal(2, actions.Length);
        Assert.All(actions, action => Assert.Equal(10, action.GetProperty("sheetId").GetInt64()));
        Assert.Contains(actions, action =>
            action.GetProperty("parameter").GetString() == "Sheet Issue Code" &&
            action.GetProperty("oldValue").GetString() == "R02" &&
            action.GetProperty("newValue").GetString() == "R03");

        using var rendered = JsonDocument.Parse(output.ToString());
        Assert.Equal("sheets issue-meta", rendered.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task IssueMeta_MissingMappedParameter_IsSkipped()
    {
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters = { ["Sheet Issue Date"] = "2026-05-01" }
                },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: Path.Combine(_root, "issue.json"),
            paramMapPath: null,
            dryRun: true,
            outputFormat: "json",
            output: output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        var skipped = Assert.Single(json.RootElement.GetProperty("skipped").EnumerateArray());
        Assert.Equal("issueCode", skipped.GetProperty("key").GetString());
        Assert.Equal("parameter-missing", skipped.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task IssueMeta_CustomParamMap_RegenerateCommandPreservesMapPath()
    {
        var mapPath = Path.Combine(_root, "titleblock-map.yml");
        File.WriteAllText(mapPath, """
issueCode:
  - Custom Revision
issueDate:
  - Custom Date
""");
        var planPath = Path.Combine(_root, "issue.json");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters =
                    {
                        ["Custom Revision"] = "R02",
                        ["Custom Date"] = "2026-05-01",
                    }
                },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: planPath,
            paramMapPath: mapPath,
            dryRun: true,
            outputFormat: "json",
            output: output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var command = json.RootElement.GetProperty("commands").GetProperty("regenerateDryRun").GetString();
        Assert.Contains("--param-map", command);
        Assert.Contains(Path.GetFullPath(mapPath), command);
    }

    [Fact]
    public async Task IssueMeta_CustomParamMap_TrimsAndDeduplicatesCandidates()
    {
        var mapPath = Path.Combine(_root, "titleblock-map.yml");
        File.WriteAllText(mapPath, """
issueCode:
  - " Custom Revision "
  - Custom Revision
  - ""
issueDate:
  - " Custom Date "
""");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters =
                    {
                        ["Custom Revision"] = "R02",
                        ["Custom Date"] = "2026-05-01",
                    }
                },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: Path.Combine(_root, "issue.json"),
            paramMapPath: mapPath,
            dryRun: true,
            outputFormat: "json",
            output: output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var targets = json.RootElement.GetProperty("targetParameters").EnumerateArray().ToArray();
        var issueCode = Assert.Single(targets, target => target.GetProperty("key").GetString() == "issueCode");
        var issueDate = Assert.Single(targets, target => target.GetProperty("key").GetString() == "issueDate");
        var issueCodeCandidates = issueCode.GetProperty("candidates").EnumerateArray().Select(item => item.GetString()).ToArray();
        var issueDateCandidates = issueDate.GetProperty("candidates").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(new[] { "Custom Revision" }, issueCodeCandidates);
        Assert.Equal(new[] { "Custom Date" }, issueDateCandidates);
    }

    [Fact]
    public async Task IssueMeta_AmbiguousMappedParameter_IsSkippedWithoutActions()
    {
        var mapPath = Path.Combine(_root, "ambiguous-titleblock-map.yml");
        File.WriteAllText(mapPath, """
issueCode:
  - Release Field
issueDate:
  - Release Field
""");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters = { ["Release Field"] = "R02" }
                },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: Path.Combine(_root, "issue.json"),
            paramMapPath: mapPath,
            dryRun: true,
            outputFormat: "json",
            output: output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("skippedCount").GetInt32());
        Assert.All(json.RootElement.GetProperty("skipped").EnumerateArray(), skipped =>
        {
            Assert.Equal("parameter-ambiguous", skipped.GetProperty("reason").GetString());
            Assert.Contains("Release Field", skipped.GetProperty("message").GetString());
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(1000)]
    public async Task IssueMeta_LargePlans_PreserveDeterministicActionOrdering(int sheetCount)
    {
        var firstPlanPath = Path.Combine(_root, $"issue-{sheetCount}-first.json");
        var secondPlanPath = Path.Combine(_root, $"issue-{sheetCount}-second.json");
        var firstOutput = new StringWriter();
        var secondOutput = new StringWriter();

        var firstExit = await SheetsCommand.ExecuteIssueMetaAsync(
            MakeClient(MakeIssueSheetSnapshot(sheetCount)),
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: firstPlanPath,
            paramMapPath: null,
            dryRun: true,
            outputFormat: "json",
            output: firstOutput);
        var secondExit = await SheetsCommand.ExecuteIssueMetaAsync(
            MakeClient(MakeIssueSheetSnapshot(sheetCount)),
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: secondPlanPath,
            paramMapPath: null,
            dryRun: true,
            outputFormat: "json",
            output: secondOutput);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        var firstActions = ReadSheetIssueActionKeys(firstPlanPath);
        var secondActions = ReadSheetIssueActionKeys(secondPlanPath);
        Assert.Equal(sheetCount * 2, firstActions.Length);
        Assert.Equal(firstActions, secondActions);
        Assert.Equal("A-0001|issueCode|Sheet Issue Code|R02|R03", firstActions[0]);
        Assert.Equal($"A-{sheetCount:D4}|issueDate|Sheet Issue Date|2026-05-01|2026-05-20", firstActions[^1]);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(1000)]
    public async Task Renumber_LargePlans_PreserveDeterministicActionOrdering(int sheetCount)
    {
        var rulePath = WriteLargeRenumberRule(sheetCount);
        var firstPlanPath = Path.Combine(_root, $"renumber-{sheetCount}-first.json");
        var secondPlanPath = Path.Combine(_root, $"renumber-{sheetCount}-second.json");
        var firstOutput = new StringWriter();
        var secondOutput = new StringWriter();

        var firstExit = await SheetsCommand.ExecuteRenumberAsync(
            MakeClient(MakeRenumberSheetSnapshot(sheetCount)),
            rulePath,
            firstPlanPath,
            selector: "all",
            maxChanges: null,
            dryRun: true,
            outputFormat: "json",
            output: firstOutput);
        var secondExit = await SheetsCommand.ExecuteRenumberAsync(
            MakeClient(MakeRenumberSheetSnapshot(sheetCount)),
            rulePath,
            secondPlanPath,
            selector: "all",
            maxChanges: null,
            dryRun: true,
            outputFormat: "json",
            output: secondOutput);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        var firstActions = ReadSheetRenumberActionKeys(firstPlanPath);
        var secondActions = ReadSheetRenumberActionKeys(secondPlanPath);
        Assert.Equal(sheetCount, firstActions.Length);
        Assert.Equal(firstActions, secondActions);
        Assert.Equal("TMP-0001|Sheet Number|TMP-0001|A-101", firstActions[0]);
        Assert.Equal($"TMP-{sheetCount:D4}|Sheet Number|TMP-{sheetCount:D4}|A-1{sheetCount:D2}", firstActions[^1]);
    }

    [Fact]
    public async Task IssueMeta_RejectsRealWritePath()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: Path.Combine(_root, "issue.json"),
            paramMapPath: null,
            dryRun: false,
            outputFormat: "table",
            output: output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Renumber_DryRun_WritesFrozenPlan()
    {
        var rulePath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
numbering:
  scheme: "A-{floor:1}{seq:02}"
  ranges:
    - floors: [1]
      seqMin: 1
      seqMax: 2
""");
        var planPath = Path.Combine(_root, ".revitcli", "plans", "sheet-renumber.json");
        var client = MakeClient(new ModelSnapshot
        {
            Revit = { Document = "tower.rvt", DocumentPath = "D:\\models\\tower.rvt" },
            Model = { FileHash = "abc123" },
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "TMP-001", Name = "Level 1" },
                new SnapshotSheet { ViewId = 11, Number = "TMP-002", Name = "Level 2" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            planPath,
            selector: "all",
            maxChanges: null,
            dryRun: true,
            outputFormat: "json",
            output: output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("sheet-renumber-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("sheet-renumber", json.RootElement.GetProperty("type").GetString());
        Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal("abc123", json.RootElement.GetProperty("modelFingerprint").GetProperty("fileHash").GetString());
        var actions = json.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, action =>
            action.GetProperty("sheetId").GetInt64() == 10 &&
            action.GetProperty("oldNumber").GetString() == "TMP-001" &&
            action.GetProperty("newNumber").GetString() == "A-101");
        Assert.Contains(actions, action =>
            action.GetProperty("sheetId").GetInt64() == 11 &&
            action.GetProperty("newNumber").GetString() == "A-102");

        using var rendered = JsonDocument.Parse(output.ToString());
        Assert.Equal("sheets renumber", rendered.RootElement.GetProperty("command").GetString());
        Assert.Contains("plan apply", rendered.RootElement.GetProperty("commands").GetProperty("dryRunApply").GetString());
    }

    [Fact]
    public async Task Renumber_RejectsRealWritePath()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteRenumberAsync(
            client,
            rulePath: Path.Combine(_root, "numbering.yml"),
            planOutputPath: Path.Combine(_root, "renumber.json"),
            selector: "all",
            maxChanges: null,
            dryRun: false,
            outputFormat: "table",
            output: output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Renumber_DuplicateTargetOnUnselectedSheet_FailsWithoutPlan()
    {
        var rulePath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
numbering:
  scheme: "A-{floor:1}{seq:02}"
  ranges:
    - floors: [1]
      seqMin: 1
      seqMax: 1
""");
        var planPath = Path.Combine(_root, "renumber.json");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "TMP-001", Name = "Pick sheet" },
                new SnapshotSheet { ViewId = 11, Number = "A-101", Name = "Unselected sheet" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            planPath,
            selector: "Pick",
            maxChanges: null,
            dryRun: true,
            outputFormat: "table",
            output: output);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite existing sheet numbers", output.ToString());
        Assert.False(File.Exists(planPath));
    }

    [Fact]
    public async Task Renumber_SelectedSheetNumberReuse_FailsWithoutPlan()
    {
        var rulePath = WriteIndex("""
name: project-sheet-frame
schemaVersion: 1
numbering:
  scheme: "A-{floor:1}{seq:02}"
  ranges:
    - floors: [1]
      seqMin: 1
      seqMax: 2
""");
        var planPath = Path.Combine(_root, "renumber-selected-reuse.json");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "", Name = "First" },
                new SnapshotSheet { ViewId = 11, Number = "A-101", Name = "Second" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            planPath,
            selector: "all",
            maxChanges: null,
            dryRun: true,
            outputFormat: "table",
            output: output);

        Assert.Equal(1, exitCode);
        Assert.Contains("still used by selected sheet", output.ToString());
        Assert.False(File.Exists(planPath));
    }

    [Fact]
    public async Task PlanShow_SheetIssuePlan_RendersMarkdown()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "sheet-issue.json");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1",
                    Parameters =
                    {
                        ["Sheet Issue Code"] = "R02",
                        ["Sheet Issue Date"] = "2026-05-01",
                    }
                },
            }
        });

        var writeExit = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: planPath,
            paramMapPath: null,
            dryRun: true,
            outputFormat: "table",
            output: new StringWriter());
        var output = new StringWriter();

        var showExit = await PlanCommand.ExecuteShowAsync(planPath, "markdown", output);

        Assert.Equal(0, writeExit);
        Assert.Equal(0, showExit);
        Assert.Contains("# Sheet Issue Plan", output.ToString());
        Assert.Contains("Sheet Issue Code", output.ToString());
    }

    [Fact]
    public async Task PlanShow_SheetIssuePlan_RendersSkippedEvidence()
    {
        var planPath = Path.Combine(_root, ".revitcli", "plans", "sheet-issue-skipped.json");
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A-101",
                    Name = "Level 1"
                },
            }
        });

        var writeExit = await SheetsCommand.ExecuteIssueMetaAsync(
            client,
            selector: "all",
            issueCode: "R03",
            issueDate: "2026-05-20",
            planOutputPath: planPath,
            paramMapPath: null,
            dryRun: true,
            outputFormat: "table",
            output: new StringWriter());
        var output = new StringWriter();

        var showExit = await PlanCommand.ExecuteShowAsync(planPath, "markdown", output);

        Assert.Equal(2, writeExit);
        Assert.Equal(0, showExit);
        var text = output.ToString();
        Assert.Contains("## Skipped", text);
        Assert.Contains("parameter-missing", text);
        Assert.Contains("issueCode", text);
        Assert.Contains("issueDate", text);
    }

    [Fact]
    public async Task IndexInit_WritesLocalYamlAndShowReadsIt()
    {
        var client = MakeClient(new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet { ViewId = 10, Number = "A-101", Name = "Level 1", PlacedViewIds = { 100 } },
                new SnapshotSheet { ViewId = 11, Number = "A-102", Name = "Level 1 RCP" },
            }
        });
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIndexInitAsync(
            client, path: null, force: false, outputFormat: "table", output);

        Assert.Equal(0, exitCode);
        var indexPath = Path.Combine(_root, ".revitcli", "sheets", "index.yml");
        Assert.True(File.Exists(indexPath));
        Assert.Contains("Writing sheet index:", output.ToString());

        var showOutput = new StringWriter();
        var showExit = await SheetsCommand.ExecuteIndexShowAsync(null, "table", showOutput);

        Assert.Equal(0, showExit);
        Assert.Contains("A-101", showOutput.ToString());
        Assert.Contains("Required sheets: 2", showOutput.ToString());
    }

    [Fact]
    public async Task IndexInit_ExistingFileRequiresForce()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".revitcli", "sheets"));
        File.WriteAllText(Path.Combine(_root, ".revitcli", "sheets", "index.yml"), "schemaVersion: 1\n");
        var client = MakeClient(new ModelSnapshot());
        var output = new StringWriter();

        var exitCode = await SheetsCommand.ExecuteIndexInitAsync(
            client, path: null, force: false, outputFormat: "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("--force", output.ToString());
    }

    private string WriteIndex(string yaml)
    {
        var path = Path.Combine(_root, "index.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private string WriteLargeRenumberRule(int sheetCount)
    {
        return WriteIndex($$"""
name: large-sheet-renumber
schemaVersion: 1
numbering:
  scheme: "A-{floor:1}{seq:04}"
  ranges:
    - floors: [1]
      seqMin: 1
      seqMax: {{sheetCount}}
""");
    }

    private static ModelSnapshot MakeIssueSheetSnapshot(int sheetCount)
    {
        var snapshot = new ModelSnapshot
        {
            Revit = { Document = "tower.rvt", DocumentPath = "D:\\models\\tower.rvt" },
            Model = { FileHash = "abc123" },
        };

        for (var index = sheetCount; index >= 1; index--)
        {
            snapshot.Sheets.Add(new SnapshotSheet
            {
                ViewId = 10_000 + index,
                Number = $"A-{index:D4}",
                Name = $"Level {index:D4}",
                Parameters =
                {
                    ["Sheet Issue Code"] = "R02",
                    ["Sheet Issue Date"] = "2026-05-01",
                }
            });
        }

        return snapshot;
    }

    private static ModelSnapshot MakeRenumberSheetSnapshot(int sheetCount)
    {
        var snapshot = new ModelSnapshot
        {
            Revit = { Document = "tower.rvt", DocumentPath = "D:\\models\\tower.rvt" },
            Model = { FileHash = "abc123" },
        };

        for (var index = sheetCount; index >= 1; index--)
        {
            snapshot.Sheets.Add(new SnapshotSheet
            {
                ViewId = 20_000 + index,
                Number = $"TMP-{index:D4}",
                Name = $"Level {index:D4}",
            });
        }

        return snapshot;
    }

    private static string[] ReadSheetIssueActionKeys(string planPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        return json.RootElement.GetProperty("actions")
            .EnumerateArray()
            .Select(action => string.Join(
                "|",
                action.GetProperty("sheetNumber").GetString(),
                action.GetProperty("key").GetString(),
                action.GetProperty("parameter").GetString(),
                action.GetProperty("oldValue").GetString(),
                action.GetProperty("newValue").GetString()))
            .ToArray();
    }

    private static string[] ReadSheetRenumberActionKeys(string planPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        return json.RootElement.GetProperty("actions")
            .EnumerateArray()
            .Select(action => string.Join(
                "|",
                action.GetProperty("sheetNumber").GetString(),
                action.GetProperty("parameter").GetString(),
                action.GetProperty("oldNumber").GetString(),
                action.GetProperty("newNumber").GetString()))
            .ToArray();
    }

    private static RevitClient MakeClient(ModelSnapshot snapshot)
    {
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }
}
