using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class InspectCommandTests
{
    [Fact]
    public async Task Categories_Table_PrintsNextCommands()
    {
        var elements = new[]
        {
            new ElementInfo { Id = 10, Name = "Door 1", Category = "Doors", TypeName = "Single" }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteCategoriesAsync(client, "table", includeEmpty: false, writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("doors", output);
        Assert.Contains("revitcli inspect params doors", output);
        Assert.True(handler.CallCount >= 3);
    }

    [Fact]
    public async Task Categories_Markdown_PrintsDiscoveryTable()
    {
        var elements = new[]
        {
            new ElementInfo { Id = 10, Name = "Door 1", Category = "Doors", TypeName = "Single" }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteCategoriesAsync(client, "markdown", includeEmpty: false, writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Inspect Categories", output);
        Assert.Contains("| Alias | Label | Model category | Count | Sample element | Next command |", output);
        Assert.Contains("`revitcli inspect params doors`", output);
    }

    [Fact]
    public async Task Categories_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteCategoriesAsync(client, "table", includeEmpty: false, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task Plans_Json_PrintsSavedPlanInventory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-inspect-plans-{Guid.NewGuid():N}");
        try
        {
            var planDir = Path.Combine(root, ".revitcli", "plans");
            Directory.CreateDirectory(planDir);
            var planPath = Path.Combine(planDir, "doors.plan.json");
            File.WriteAllText(planPath, """
{
  "schemaVersion": 1,
  "type": "set",
  "createdAtUtc": "2026-05-19T10:00:00Z",
  "createdBy": "tester",
  "summary": {
    "operation": "set",
    "param": "Mark",
    "value": "D-01",
    "affected": 2,
    "frozenElementIds": [100, 200],
    "originalTarget": "category=doors",
    "applyTarget": "frozen elementIds"
  },
  "applyRequest": {
    "param": "Mark",
    "value": "D-01",
    "elementIds": [100, 200],
    "dryRun": false
  },
  "preview": [],
  "commands": {}
}
""");
            File.WriteAllText(planPath + ".receipt.json", """{"schemaVersion":"plan-receipt.v1"}""");
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecutePlansAsync(root, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var report = json.RootElement;
            Assert.Equal("inspect-plans.v1", report.GetProperty("schemaVersion").GetString());
            Assert.Equal(NormalizePath(Path.GetFullPath(root)), report.GetProperty("projectDirectory").GetString());
            Assert.Equal(1, report.GetProperty("planCount").GetInt32());
            Assert.Equal(2, report.GetProperty("totalActionCount").GetInt32());
            var plan = report.GetProperty("plans").EnumerateArray().Single();
            Assert.Equal("doors.plan.json", Path.GetFileName(plan.GetProperty("path").GetString()));
            Assert.Equal("set", plan.GetProperty("type").GetString());
            Assert.Equal("ready", plan.GetProperty("status").GetString());
            Assert.Equal(2, plan.GetProperty("actionCount").GetInt32());
            Assert.True(plan.GetProperty("hasReceipt").GetBoolean());
            Assert.Contains(
                "plan apply",
                plan.GetProperty("commands").GetProperty("dryRunApplyCommand").GetString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "rollback",
                plan.GetProperty("commands").GetProperty("rollbackPreviewCommand").GetString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plans_Json_RecognizesSheetIssuePlan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-inspect-plans-{Guid.NewGuid():N}");
        try
        {
            var planDir = Path.Combine(root, ".revitcli", "plans");
            Directory.CreateDirectory(planDir);
            File.WriteAllText(Path.Combine(planDir, "sheet-issue.json"), """
{
  "schemaVersion": "sheet-issue-plan.v1",
  "type": "sheet-issue",
  "command": "sheets issue-meta",
  "createdAtUtc": "2026-05-20T10:00:00Z",
  "createdBy": "tester",
  "dryRun": true,
  "selector": "all",
  "issueCode": "R03",
  "issueDate": "2026-05-20",
  "paramMap": "(builtin defaults)",
  "targetParameters": [],
  "modelFingerprint": { "document": "tower.rvt", "documentPath": "D:/tower.rvt", "fileHash": "abc" },
  "summary": { "totalSheets": 1, "matchedSheets": 1, "actionCount": 2, "skippedCount": 0 },
  "actions": [
    { "sheetId": 10, "sheetNumber": "A-101", "sheetName": "Level 1", "titleblockId": null, "key": "issueCode", "parameter": "Sheet Issue Code", "oldValue": "R02", "newValue": "R03" },
    { "sheetId": 10, "sheetNumber": "A-101", "sheetName": "Level 1", "titleblockId": null, "key": "issueDate", "parameter": "Sheet Issue Date", "oldValue": "2026-05-01", "newValue": "2026-05-20" }
  ],
  "skipped": [],
  "commands": {
    "show": "revitcli plan show \"sheet-issue.json\" --output markdown",
    "regenerateDryRun": "revitcli sheets issue-meta --selector all --issue-code R03 --issue-date 2026-05-20 --plan-output sheet-issue.json --dry-run --output markdown"
  }
}
""");
            File.WriteAllText(
                Path.Combine(planDir, "sheet-issue.json.receipt.json"),
                """{"schemaVersion":"plan-receipt.v1","operation":"sheet-issue","rollbackActions":[]}""");
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecutePlansAsync(root, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var plan = Assert.Single(json.RootElement.GetProperty("plans").EnumerateArray());
            Assert.Equal("sheet-issue", plan.GetProperty("type").GetString());
            Assert.Equal("ready", plan.GetProperty("status").GetString());
            Assert.Equal(2, plan.GetProperty("actionCount").GetInt32());
            Assert.True(plan.GetProperty("hasReceipt").GetBoolean());
            Assert.Contains("plan apply", plan.GetProperty("commands").GetProperty("dryRunApplyCommand").GetString());
            Assert.Contains("plan apply", plan.GetProperty("commands").GetProperty("applyCommand").GetString());
            Assert.Contains("rollback", plan.GetProperty("commands").GetProperty("rollbackPreviewCommand").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plans_Json_RecognizesSheetRenumberPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-inspect-plans-{Guid.NewGuid():N}");
        try
        {
            var planDir = Path.Combine(root, ".revitcli", "plans");
            Directory.CreateDirectory(planDir);
            File.WriteAllText(Path.Combine(planDir, "sheet-renumber.json"), """
{
  "schemaVersion": "sheet-renumber-plan.v1",
  "type": "sheet-renumber",
  "command": "sheets renumber",
  "createdAtUtc": "2026-05-20T10:00:00Z",
  "createdBy": "tester",
  "dryRun": true,
  "selector": "all",
  "rulePath": ".revitcli/sheets/numbering.yml",
  "parameter": "Sheet Number",
  "modelFingerprint": { "document": "tower.rvt", "documentPath": "D:/tower.rvt", "fileHash": "abc" },
  "summary": { "totalSheets": 1, "matchedSheets": 1, "generatedNumbers": 1, "actionCount": 1, "skippedCount": 0 },
  "actions": [
    { "sheetId": 10, "sheetNumber": "TMP-001", "sheetName": "Level 1", "parameter": "Sheet Number", "oldNumber": "TMP-001", "newNumber": "A-101", "floor": 1, "seq": 1 }
  ],
  "skipped": [],
  "commands": {
    "show": "revitcli plan show \"sheet-renumber.json\" --output markdown",
    "regenerateDryRun": "revitcli sheets renumber --rule .revitcli/sheets/numbering.yml --selector all --plan-output sheet-renumber.json --dry-run --output markdown",
    "dryRunApply": "revitcli plan apply sheet-renumber.json --dry-run",
    "apply": "revitcli plan apply sheet-renumber.json --yes"
  }
}
""");
            File.WriteAllText(
                Path.Combine(planDir, "sheet-renumber.json.receipt.json"),
                """{"schemaVersion":"plan-receipt.v1","operation":"sheet-renumber","rollbackActions":[]}""");
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecutePlansAsync(root, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var plan = Assert.Single(json.RootElement.GetProperty("plans").EnumerateArray());
            Assert.Equal("sheet-renumber", plan.GetProperty("type").GetString());
            Assert.Equal("ready", plan.GetProperty("status").GetString());
            Assert.Equal(1, plan.GetProperty("actionCount").GetInt32());
            Assert.True(plan.GetProperty("hasReceipt").GetBoolean());
            Assert.Contains("plan apply", plan.GetProperty("commands").GetProperty("dryRunApplyCommand").GetString());
            Assert.Contains("rollback", plan.GetProperty("commands").GetProperty("rollbackPreviewCommand").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plans_Markdown_PrintsReviewCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-inspect-plans-{Guid.NewGuid():N}");
        try
        {
            var planDir = Path.Combine(root, ".revitcli", "plans");
            Directory.CreateDirectory(planDir);
            File.WriteAllText(Path.Combine(planDir, "bad.json"), "{ not json");
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecutePlansAsync(root, "markdown", writer);

            var text = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# Inspect Plans", text);
            Assert.Contains("Schema: `inspect-plans.v1`", text);
            Assert.Contains("| Plan | Type | Status | Actions | Receipt | Show | Dry-run apply | Apply | Rollback preview |", text);
            Assert.Contains("`invalid`", text);
            Assert.Contains("## Issues", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plans_Json_InvalidSheetIssuePlanReportsIssue()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-inspect-plans-{Guid.NewGuid():N}");
        try
        {
            var planDir = Path.Combine(root, ".revitcli", "plans");
            Directory.CreateDirectory(planDir);
            File.WriteAllText(Path.Combine(planDir, "bad-sheet-issue.json"), """
{
  "schemaVersion": "sheet-issue-plan.v1",
  "type": "sheet-issue"
}
""");
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecutePlansAsync(root, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var plan = Assert.Single(json.RootElement.GetProperty("plans").EnumerateArray());
            Assert.Equal("invalid", plan.GetProperty("status").GetString());
            var issue = Assert.Single(plan.GetProperty("issues").EnumerateArray());
            Assert.Contains("missing", issue.GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plans_UnknownOutput_ReturnsFailureBeforeReadingDirectory()
    {
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecutePlansAsync("/path/that/does/not/exist", "yaml", writer);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public async Task Params_Table_AggregatesParameters()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-01",
                    ["Fire Rating"] = "60min"
                }
            },
            new ElementInfo
            {
                Id = 2,
                Name = "Door 2",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-02"
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Mark", output);
        Assert.Contains("100", output);
        Assert.Contains("Fire Rating", output);
        Assert.Contains("unknown", output);
        Assert.DoesNotContain("revitcli set doors --param \"Fire Rating\"", output);
        Assert.Contains("Dry-run probe", output);
    }

    [Fact]
    public async Task Params_Markdown_PrintsDryRunProbe()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                ParameterMetadata =
                {
                    new ElementParameterInfo
                    {
                        Name = "Fire Rating",
                        DefinitionName = "Fire Rating",
                        Value = "60min",
                        StorageType = "String",
                        HasValue = true,
                        CanWrite = true
                    }
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "markdown", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Inspect Parameters: doors", output);
        Assert.Contains("| Parameter | Seen | Values | Write | Type | Samples | Dry-run probe |", output);
        Assert.Contains("Fire Rating", output);
        Assert.Contains("`revitcli set --id 1 --param \"Fire Rating\" --value \"<value>\" --dry-run`", output);
    }

    [Fact]
    public async Task Params_Json_PrintsCodexFriendlyFields()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-01"
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "json", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"name\": \"Mark\"", output);
        Assert.Contains("\"coveragePercent\": 100", output);
        Assert.Contains("\"writeStatus\": \"unknown\"", output);
        Assert.Contains("\"dryRunProbeCommand\": \"\"", output);
    }

    [Fact]
    public async Task Params_Json_UsesParameterMetadataForWritableDiscovery()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                ParameterMetadata =
                {
                    new ElementParameterInfo
                    {
                        Name = "Mark",
                        DefinitionName = "Mark",
                        Value = "D-01",
                        StorageType = "String",
                        HasValue = true,
                        CanWrite = true
                    },
                    new ElementParameterInfo
                    {
                        Name = "Area",
                        DefinitionName = "Area",
                        Value = "12 sqm",
                        StorageType = "Double",
                        HasValue = true,
                        IsReadOnly = true,
                        CanWrite = false
                    }
                }
            },
            new ElementInfo
            {
                Id = 2,
                Name = "Door 2",
                Category = "Doors",
                ParameterMetadata =
                {
                    new ElementParameterInfo
                    {
                        Name = "Mark",
                        DefinitionName = "Mark",
                        StorageType = "String",
                        HasValue = false,
                        CanWrite = true
                    },
                    new ElementParameterInfo
                    {
                        Name = "Area",
                        DefinitionName = "Area",
                        Value = "14 sqm",
                        StorageType = "Double",
                        HasValue = true,
                        IsReadOnly = true,
                        CanWrite = false
                    }
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "json", writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var items = document.RootElement
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!);
        var mark = items["Mark"];
        var area = items["Area"];

        Assert.Equal("Mark", mark.GetProperty("name").GetString());
        Assert.True(mark.GetProperty("canWrite").GetBoolean());
        Assert.Equal("writable", mark.GetProperty("writeStatus").GetString());
        Assert.Equal(50, mark.GetProperty("valueCoveragePercent").GetDouble());
        Assert.Equal(2, mark.GetProperty("writableOn").GetInt32());
        Assert.Equal("String", mark.GetProperty("storageTypes")[0].GetString());
        Assert.Equal(2, mark.GetProperty("sampleElementId").GetInt64());
        Assert.Equal(2, mark.GetProperty("missingSampleElementId").GetInt64());
        Assert.Contains("--id 2", mark.GetProperty("dryRunProbeCommand").GetString());
        Assert.Contains("--param \"Mark\"", mark.GetProperty("dryRunProbeCommand").GetString());

        Assert.Equal("Area", area.GetProperty("name").GetString());
        Assert.False(area.GetProperty("canWrite").GetBoolean());
        Assert.Equal("read-only", area.GetProperty("writeStatus").GetString());
        Assert.Equal("", area.GetProperty("dryRunProbeCommand").GetString());
    }

    [Fact]
    public async Task Params_Table_PrintsWritableAndTypeColumns()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                ParameterMetadata =
                {
                    new ElementParameterInfo
                    {
                        Name = "Fire Rating",
                        DefinitionName = "Fire Rating",
                        Value = "60min",
                        StorageType = "String",
                        HasValue = true,
                        CanWrite = true
                    }
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Write", output);
        Assert.Contains("Type", output);
        Assert.Contains("writable", output);
        Assert.Contains("String", output);
        Assert.Contains("revitcli set --id 1 --param \"Fire Rating\" --value \"<value>\" --dry-run", output);
    }

    [Fact]
    public async Task Params_FiltersByNameWritableAndMissingValues()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                ParameterMetadata =
                {
                    new ElementParameterInfo
                    {
                        Name = "Fire Rating",
                        DefinitionName = "Fire Rating",
                        Value = "60min",
                        StorageType = "String",
                        HasValue = true,
                        CanWrite = true
                    },
                    new ElementParameterInfo
                    {
                        Name = "Fire Door Area",
                        DefinitionName = "Fire Door Area",
                        Value = "2 sqm",
                        StorageType = "Double",
                        HasValue = true,
                        CanWrite = false,
                        IsReadOnly = true
                    }
                }
            },
            new ElementInfo
            {
                Id = 2,
                Name = "Door 2",
                Category = "Doors",
                ParameterMetadata =
                {
                    new ElementParameterInfo
                    {
                        Name = "Fire Rating",
                        DefinitionName = "Fire Rating",
                        StorageType = "String",
                        HasValue = false,
                        CanWrite = true
                    },
                    new ElementParameterInfo
                    {
                        Name = "Fire Door Area",
                        DefinitionName = "Fire Door Area",
                        Value = "3 sqm",
                        StorageType = "Double",
                        HasValue = true,
                        CanWrite = false,
                        IsReadOnly = true
                    }
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(
            client,
            "doors",
            "json",
            nameFilter: "Fire*",
            writableOnly: true,
            missingOnly: true,
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Fire Rating", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("canWrite").GetBoolean());
        Assert.Equal(50, item.GetProperty("valueCoveragePercent").GetDouble());
        Assert.Equal(2, item.GetProperty("sampleElementId").GetInt64());
        Assert.Equal(2, item.GetProperty("missingSampleElementId").GetInt64());
        Assert.Contains("--id 2", item.GetProperty("dryRunProbeCommand").GetString());
        Assert.Contains("--param \"Fire Rating\"", item.GetProperty("dryRunProbeCommand").GetString());
    }

    [Fact]
    public async Task Params_FiltersReturnEmptyMessage()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-01"
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(
            client,
            "doors",
            "table",
            nameFilter: "Fire*",
            writableOnly: false,
            missingOnly: false,
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No parameters matched the inspect filters", writer.ToString());
    }

    [Fact]
    public async Task Sheets_Table_PrintsDryRunExportCommands()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100, 101 }
                },
                new SnapshotSheet
                {
                    ViewId = 11,
                    Number = "G001",
                    Name = "Cover",
                    PlacedViewIds = { 102 }
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("/api/snapshot", handler.LastRequestUri);
        Assert.Contains("A101", output);
        Assert.Contains("Floor Plan", output);
        Assert.Contains("revitcli export --format pdf --sheets \"A101\"", output);
        Assert.Contains("revitcli export --format pdf --sheets \"A101\" --dry-run", output);
        Assert.True(
            CountOccurrences(output, "revitcli export --format pdf --sheets \"A101\"") >= 2,
            "Table output should include separate export and dry-run commands for each sheet.");
        AssertSnapshotRequestCapturesOnlySheets(handler.LastRequestBody);
    }

    [Fact]
    public async Task Sheets_Markdown_PrintsIssueAndDryRunColumns()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100, 101 }
                },
                new SnapshotSheet
                {
                    ViewId = 11,
                    Number = "G001",
                    Name = "Cover"
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "markdown", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Inspect Sheets", output);
        Assert.Contains("| Number | Name | Views | Ready | Issues | Dry-run export |", output);
        Assert.Contains("| A101 | Floor Plan | 2 | yes | ok | `revitcli export --format pdf --sheets \"A101\" --dry-run` |", output);
        Assert.Contains("no-placed-views", output);
    }

    [Fact]
    public async Task Sheets_Json_PrintsCodexFriendlyFields()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100, 101 }
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "json", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        AssertSnapshotRequestCapturesOnlySheets(handler.LastRequestBody);
        Assert.Contains("\"number\": \"A101\"", output);
        Assert.Contains("\"placedViewCount\": 2", output);
        Assert.Contains("\"exportReady\": true", output);
        Assert.Contains("\"exportCommand\"", output);
        Assert.Contains("\"dryRunCommand\"", output);
        Assert.Contains("\"dryRunExportCommand\"", output);

        using var document = JsonDocument.Parse(output);
        var item = document.RootElement[0];
        Assert.Equal("revitcli export --format pdf --sheets \"A101\"", item.GetProperty("exportCommand").GetString());
        Assert.Equal(
            "revitcli export --format pdf --sheets \"A101\" --dry-run",
            item.GetProperty("dryRunCommand").GetString());
        Assert.Equal(
            item.GetProperty("dryRunCommand").GetString(),
            item.GetProperty("dryRunExportCommand").GetString());
    }

    [Fact]
    public async Task Sheets_Json_PrintsIssuesAndKeyParameters()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "",
                    Name = "Cover",
                    Parameters =
                    {
                        ["Drawn By"] = "AZ",
                        ["Sheet Issue Date"] = "2026-04-29"
                    }
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "json", writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var item = document.RootElement[0];
        Assert.Equal("Cover", item.GetProperty("selector").GetString());
        Assert.False(item.GetProperty("exportReady").GetBoolean());
        Assert.Equal("AZ", item.GetProperty("keyParameters").GetProperty("drawnBy").GetString());
        Assert.Equal("2026-04-29", item.GetProperty("keyParameters").GetProperty("issueDate").GetString());
        Assert.Contains("missing-number", writer.ToString());
        Assert.Contains("no-placed-views", writer.ToString());
    }

    [Fact]
    public async Task Sheets_FiltersByPatternAndIssuesOnly()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100 }
                },
                new SnapshotSheet
                {
                    ViewId = 11,
                    Number = "G001",
                    Name = "Cover"
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(
            client,
            "json",
            new[] { "G*" },
            readyOnly: false,
            issuesOnly: true,
            writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"number\": \"G001\"", output);
        Assert.DoesNotContain("\"number\": \"A101\"", output);
        Assert.Contains("\"issueSummary\": \"no-placed-views\"", output);
    }

    [Fact]
    public async Task Sheets_FiltersReadyOnly()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100 }
                },
                new SnapshotSheet
                {
                    ViewId = 11,
                    Number = "G001",
                    Name = "Cover"
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(
            client,
            "json",
            new[] { "all" },
            readyOnly: true,
            issuesOnly: false,
            writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"number\": \"A101\"", output);
        Assert.DoesNotContain("\"number\": \"G001\"", output);
    }

    [Fact]
    public async Task Sheets_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "table", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task Schedules_Table_PrintsExportCommands()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 2, Name = "Room Schedule", Category = "Rooms", FieldCount = 4, RowCount = 9 },
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Door Schedule", output);
        Assert.Contains("revitcli schedule export --name \"Door Schedule\" --output csv", output);
    }

    [Fact]
    public async Task Schedules_Markdown_PrintsReadinessTable()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 },
            new ScheduleInfo { Id = 2, Name = "Door Empty", Category = "Doors", FieldCount = 5, RowCount = 0 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "markdown", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Inspect Schedules", output);
        Assert.Contains("| Name | Category | Fields | Rows | Ready | Issues | CSV export | JSON export |", output);
        Assert.Contains("| Door Schedule | Doors | 5 | 12 | yes | ok | `revitcli schedule export --name \"Door Schedule\" --output csv` |", output);
        Assert.Contains("empty-schedule", output);
    }

    [Fact]
    public async Task Schedules_Json_PrintsCodexFriendlyFields()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "json", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"exportReady\": true", output);
        Assert.Contains("\"exportCommand\"", output);
        Assert.Contains("\"jsonExportCommand\"", output);
        Assert.Contains("\"issueSummary\": \"ok\"", output);
        Assert.Contains("revitcli schedule export --name", output);
        Assert.Contains("Door Schedule", output);
    }

    [Fact]
    public async Task Schedules_FiltersByCategoryNameAndReadyOnly()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 },
            new ScheduleInfo { Id = 2, Name = "Door Empty", Category = "Doors", FieldCount = 5, RowCount = 0 },
            new ScheduleInfo { Id = 3, Name = "Room Schedule", Category = "Rooms", FieldCount = 4, RowCount = 8 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(
            client,
            "json",
            categoryFilter: "Doors",
            nameFilter: "Door*",
            readyOnly: true,
            emptyOnly: false,
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Door Schedule", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("exportReady").GetBoolean());
        Assert.Equal("revitcli schedule export --name \"Door Schedule\" --output json",
            item.GetProperty("jsonExportCommand").GetString());
    }

    [Fact]
    public async Task Schedules_EmptyOnlyShowsEmptyScheduleIssues()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 },
            new ScheduleInfo { Id = 2, Name = "Door Empty", Category = "Doors", FieldCount = 5, RowCount = 0 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(
            client,
            "json",
            categoryFilter: null,
            nameFilter: null,
            readyOnly: false,
            emptyOnly: true,
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Door Empty", item.GetProperty("name").GetString());
        Assert.False(item.GetProperty("exportReady").GetBoolean());
        Assert.Equal("empty-schedule", item.GetProperty("issues")[0].GetProperty("code").GetString());
        Assert.Equal("empty-schedule", item.GetProperty("issueSummary").GetString());
    }

    [Fact]
    public async Task Schedules_IssuesOnlyShowsReadinessIssues()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 },
            new ScheduleInfo { Id = 2, Name = "Door Empty", Category = "Doors", FieldCount = 5, RowCount = 0 },
            new ScheduleInfo { Id = 3, Name = "Room Broken", Category = "Rooms", FieldCount = 0, RowCount = 4 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(
            client,
            "json",
            categoryFilter: null,
            nameFilter: null,
            readyOnly: false,
            emptyOnly: false,
            issuesOnly: true,
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var items = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, item => item.GetProperty("name").GetString() == "Door Empty");
        Assert.Contains(items, item => item.GetProperty("name").GetString() == "Room Broken");
        Assert.DoesNotContain(items, item => item.GetProperty("name").GetString() == "Door Schedule");
    }

    [Fact]
    public async Task Schedules_ReadyOnlyAndEmptyOnlyConflict()
    {
        var response = ApiResponse<ScheduleInfo[]>.Ok(Array.Empty<ScheduleInfo>());
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(
            client,
            "table",
            categoryFilter: null,
            nameFilter: null,
            readyOnly: true,
            emptyOnly: true,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--ready-only", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Schedules_ReadyOnlyAndIssuesOnlyConflict()
    {
        var response = ApiResponse<ScheduleInfo[]>.Ok(Array.Empty<ScheduleInfo>());
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(
            client,
            "table",
            categoryFilter: null,
            nameFilter: null,
            readyOnly: true,
            emptyOnly: false,
            issuesOnly: true,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--issues-only", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Schedules_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "table", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task Workflows_Json_PrintsLocalWorkflowDiscovery()
    {
        var root = CreateTempProject();
        try
        {
            var workflowPath = Path.Combine(root, ".revitcli", "workflows", "pre-issue.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
            await File.WriteAllTextAsync(workflowPath, ValidWorkflowYaml());
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecuteWorkflowsAsync(root, "json", writer);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(writer.ToString());
            var json = document.RootElement;
            Assert.Equal("inspect-workflows.v1", json.GetProperty("schemaVersion").GetString());
            Assert.Equal(1, json.GetProperty("workflowCount").GetInt32());
            Assert.Equal(1, json.GetProperty("runnableCount").GetInt32());
            var workflow = Assert.Single(json.GetProperty("workflows").EnumerateArray());
            Assert.Equal(".revitcli/workflows/pre-issue.yml", workflow.GetProperty("path").GetString());
            Assert.Equal("pre-issue", workflow.GetProperty("name").GetString());
            Assert.Equal("ready", workflow.GetProperty("status").GetString());
            Assert.Equal(3, workflow.GetProperty("stepCount").GetInt32());
            Assert.Equal(1, workflow.GetProperty("mutatingStepCount").GetInt32());
            Assert.Equal(1, workflow.GetProperty("approvalRequiredCount").GetInt32());
            var commands = workflow.GetProperty("commands");
            Assert.Contains("workflow review \".revitcli/workflows/pre-issue.yml\" --output markdown", commands.GetProperty("reviewCommand").GetString());
            Assert.Contains("workflow run \".revitcli/workflows/pre-issue.yml\" --dry-run --output markdown", commands.GetProperty("dryRunCommand").GetString());
            Assert.Contains("workflow receipts --name \"pre-issue\" --output markdown", commands.GetProperty("receiptsCommand").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Workflows_Markdown_PrintsReviewAndReceiptCommands()
    {
        var root = CreateTempProject();
        try
        {
            var workflowPath = Path.Combine(root, ".revitcli", "workflows", "pre-issue.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
            await File.WriteAllTextAsync(workflowPath, ValidWorkflowYaml());
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecuteWorkflowsAsync(root, "markdown", writer);

            var text = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# Inspect Workflows", text);
            Assert.Contains("Schema: `inspect-workflows.v1`", text);
            Assert.Contains("`revitcli workflow review \".revitcli/workflows/pre-issue.yml\" --output markdown`", text);
            Assert.Contains("`revitcli workflow receipts --name \"pre-issue\" --output markdown`", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Workflows_Table_NoWorkflowDirectory_ReturnsEmptyDiscovery()
    {
        var root = CreateTempProject();
        try
        {
            var writer = new StringWriter();

            var exitCode = await InspectCommand.ExecuteWorkflowsAsync(root, "table", writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("RevitCli inspect workflows (inspect-workflows.v1)", writer.ToString());
            Assert.Contains("No workflow YAML files found", writer.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Workflows_InvalidOutput_ReturnsFailure()
    {
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteWorkflowsAsync(Directory.GetCurrentDirectory(), "yaml", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--output must be 'table', 'json', or 'markdown'", writer.ToString());
    }

    private static void AssertSnapshotRequestCapturesOnlySheets(string? requestBody)
    {
        Assert.False(string.IsNullOrWhiteSpace(requestBody));

        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;

        Assert.True(root.GetProperty("includeSheets").GetBoolean());
        Assert.False(root.GetProperty("includeSchedules").GetBoolean());

        var includeCategories = root.GetProperty("includeCategories");
        Assert.Equal(JsonValueKind.Array, includeCategories.ValueKind);
        Assert.Equal(0, includeCategories.GetArrayLength());
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    private static string CreateTempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitcli-inspect-workflows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ValidWorkflowYaml() =>
        """
        name: pre-issue
        description: Pre-issue review
        steps:
          - name: inspect sheets
            run: revitcli inspect sheets --issues-only
            mode: read-only
          - name: publish dry-run
            run: revitcli publish issue --dry-run
            mode: dry-run
          - name: approved publish
            run: revitcli publish issue
            mode: mutating
            requiresApproval: true
        """;

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/');
}
