using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using RevitCli.Output;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class DeliverablesCommandTests
{
    [Fact]
    public async Task Verify_ValidManifest_ReturnsZero()
    {
        var dir = TempDir();
        try
        {
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export");
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "table", writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Delivery manifest valid", writer.ToString());
            Assert.Contains("Entries verified: 1", writer.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_ValidManifestWithReceiptHash_ReportsActualReceiptHash()
    {
        var dir = TempDir();
        try
        {
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export");
            var receiptHash = DeliveryManifestWriter.ComputeSha256Hex(receiptPath);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                receiptHash,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.True(root.GetProperty("valid").GetBoolean());
            var entry = Assert.Single(root.GetProperty("entries").EnumerateArray());
            Assert.Equal(receiptHash, entry.GetProperty("receiptHash").GetString());
            Assert.Equal(receiptHash, entry.GetProperty("actualReceiptHash").GetString());
            Assert.DoesNotContain(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString()!.StartsWith("receipt-hash", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_MismatchedReceiptHash_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export");
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                receiptHash = new string('0', 64),
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.False(root.GetProperty("valid").GetBoolean());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "receipt-hash-mismatch");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task List_Table_ShowsEntriesAndReceiptStatus()
    {
        var dir = TempDir();
        try
        {
            var receiptPath = WriteReceipt(dir, "publish-receipt.v1", "publish");
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "publish",
                success = true,
                dryRun = false,
                pipeline = "issue",
                receiptPath,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteListAsync(dir, "table", writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("publish", output);
            Assert.Contains("success", output);
            Assert.Contains("receipt-ok", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task List_Markdown_PrintsHandoffTable()
    {
        var dir = TempDir();
        try
        {
            var receiptPath = WriteReceipt(dir, "publish-receipt.v1", "publish");
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "publish",
                success = true,
                dryRun = false,
                pipeline = "issue",
                receiptPath,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteListAsync(dir, "markdown", writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# Delivery Manifest", output);
            Assert.Contains("| Line | Kind | Outcome | Receipt | Timestamp | Receipt path |", output);
            Assert.Contains("| 1 | publish | success | receipt-ok | 2026-05-17T12:00:00Z |", output);
            Assert.Contains("## Issues", output);
            Assert.Contains("- None.", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Stats_Json_SummarizesKindsAndOutcomes()
    {
        var dir = TempDir();
        try
        {
            var exportReceipt = WriteReceipt(dir, "export-receipt.v1", "export");
            var publishReceipt = WriteReceipt(dir, "publish-receipt.v1", "publish", "publish-issue.json");
            WriteManifest(
                dir,
                new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "export",
                    success = true,
                    dryRun = false,
                    receiptPath = exportReceipt
                },
                new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = false,
                    dryRun = false,
                    receiptPath = publishReceipt
                });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteStatsAsync(dir, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.Equal("deliverables.v1", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.True(root.GetProperty("valid").GetBoolean());
            Assert.Equal(2, root.GetProperty("entryCount").GetInt32());
            var stats = root.GetProperty("stats");
            Assert.Equal(2, stats.GetProperty("entryCount").GetInt32());
            Assert.Equal(0, stats.GetProperty("errorCount").GetInt32());
            Assert.Contains(stats.GetProperty("kinds").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "export" && item.GetProperty("count").GetInt32() == 1);
            Assert.Contains(stats.GetProperty("outcomes").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "failed" && item.GetProperty("count").GetInt32() == 1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Stats_Markdown_PrintsReviewCounts()
    {
        var dir = TempDir();
        try
        {
            var exportReceipt = WriteReceipt(dir, "export-receipt.v1", "export");
            var publishReceipt = WriteReceipt(dir, "publish-receipt.v1", "publish", "publish-issue.json");
            WriteManifest(
                dir,
                new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "export",
                    success = true,
                    dryRun = false,
                    receiptPath = exportReceipt
                },
                new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = false,
                    dryRun = false,
                    receiptPath = publishReceipt
                });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteStatsAsync(dir, "markdown", writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# Delivery Manifest Stats", output);
            Assert.Contains("- Entries: `2`", output);
            Assert.Contains("## Kinds", output);
            Assert.Contains("| export | 1 |", output);
            Assert.Contains("| publish | 1 |", output);
            Assert.Contains("## Outcomes", output);
            Assert.Contains("| failed | 1 |", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_MissingReceipt_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath = Path.Combine(dir, ".revitcli", "receipts", "missing.json")
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.False(root.GetProperty("valid").GetBoolean());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "receipt-missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_MalformedManifestLine_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            var manifestDir = Path.Combine(dir, ".revitcli", "deliveries");
            Directory.CreateDirectory(manifestDir);
            File.WriteAllText(Path.Combine(manifestDir, "manifest.jsonl"), "{not-json" + System.Environment.NewLine);
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "manifest-json-invalid");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_MalformedReceiptJson_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            var receiptDir = Path.Combine(dir, ".revitcli", "receipts");
            Directory.CreateDirectory(receiptDir);
            var receiptPath = Path.Combine(receiptDir, "bad.json");
            File.WriteAllText(receiptPath, "{bad-json");
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "receipt-json-invalid");
            Assert.Equal(1, root.GetProperty("stats").GetProperty("receiptUnreadableCount").GetInt32());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_ManifestLineMissingRequiredFields_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            WriteManifest(dir, new
            {
                success = true,
                dryRun = false
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var issues = json.RootElement.GetProperty("issues").EnumerateArray().ToArray();
            Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "manifest-schema-invalid");
            Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "manifest-kind-invalid");
            Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "receipt-path-missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_TamperedReceiptSchemaAndAction_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            var receiptPath = WriteReceipt(dir, "publish-receipt.v1", "publish");
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var issues = json.RootElement.GetProperty("issues").EnumerateArray().ToArray();
            Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "receipt-schema-invalid");
            Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "receipt-action-mismatch");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Markdown_PrintsIssues()
    {
        var dir = TempDir();
        try
        {
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath = Path.Combine(dir, ".revitcli", "receipts", "missing.json")
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteVerifyAsync(dir, "markdown", writer);

            var output = writer.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("# Delivery Manifest Verification", output);
            Assert.Contains("- Status: `FAIL`", output);
            Assert.Contains("`ERROR` `line 1` `receipt-missing`", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingManifest_ListReturnsInfoButVerifyFails()
    {
        var dir = TempDir();
        try
        {
            var listWriter = new StringWriter();
            var listExit = await DeliverablesCommand.ExecuteListAsync(dir, "table", listWriter);

            Assert.Equal(0, listExit);
            Assert.Contains("No delivery manifest found", listWriter.ToString());

            var verifyWriter = new StringWriter();
            var verifyExit = await DeliverablesCommand.ExecuteVerifyAsync(dir, "json", verifyWriter);

            Assert.Equal(1, verifyExit);
            using var json = JsonDocument.Parse(verifyWriter.ToString());
            Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "manifest-missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Plan_Json_ExpandsProfilePipelinesAndExports()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteProfile(dir, """
version: 1
defaults:
  outputDir: ./deliverables
checks:
  quick:
    failOn: error
    auditRules:
      - rule: sheets-missing-info
exports:
  pdf:
    format: pdf
    sheets: [A101, A102]
    outputDir: ./deliverables/pdf
  dwg:
    format: dwg
    sheets: [all]
    outputDir: ./deliverables/dwg
publish:
  default:
    precheck: quick
    presets:
      - pdf
      - dwg
""");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecutePlanAsync(profilePath, null, "json", writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.Equal("delivery-plan.v1", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(1, root.GetProperty("pipelineCount").GetInt32());
            Assert.Equal(2, root.GetProperty("exportCount").GetInt32());
            Assert.Contains(root.GetProperty("commandPaths").EnumerateArray(), command =>
                command.GetString()!.Contains("revitcli publish default --profile"));

            var pipeline = root.GetProperty("pipelines").EnumerateArray().Single();
            Assert.Equal("quick", pipeline.GetProperty("precheck").GetString());
            Assert.Contains(pipeline.GetProperty("exports").EnumerateArray(), export =>
                export.GetProperty("preset").GetString() == "pdf" &&
                export.GetProperty("selector").GetString() == "sheets: A101,A102");
            Assert.Contains(root.GetProperty("risks").EnumerateArray(), risk =>
                risk.GetProperty("code").GetString() == "preset-all-sheets");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Plan_Markdown_WithSinceBaseline_PrintsBaselineAndSheetEstimates()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteProfile(dir, """
version: 1
exports:
  pdf:
    format: pdf
    sheets: [all]
    outputDir: ./deliverables/pdf
publish:
  issue:
    presets:
      - pdf
""");
            var baselinePath = WriteBaseline(dir, "baseline.json", "A101", "A102");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecutePlanAsync(profilePath, baselinePath, "markdown", writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# Delivery Plan", output);
            Assert.Contains("- Schema: `delivery-plan.v1`", output);
            Assert.Contains("## Baseline", output);
            Assert.Contains("- Sheets: `2`", output);
            Assert.Contains("| issue | pdf | pdf | sheets: all | 2 |", output);
            Assert.Contains("revitcli publish issue --profile", output);
            Assert.Contains("--since", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Plan_MissingPreset_ReturnsFailure()
    {
        var dir = TempDir();
        try
        {
            var profilePath = WriteProfile(dir, """
version: 1
publish:
  default:
    presets:
      - missing
""");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecutePlanAsync(profilePath, null, "json", writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.False(root.GetProperty("valid").GetBoolean());
            Assert.Contains(root.GetProperty("risks").EnumerateArray(), risk =>
                risk.GetProperty("code").GetString() == "preset-missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Plan_RequiresProfile()
    {
        var writer = new StringWriter();

        var exitCode = await DeliverablesCommand.ExecutePlanAsync(null, null, "json", writer);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("delivery-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Contains(json.RootElement.GetProperty("risks").EnumerateArray(), risk =>
            risk.GetProperty("code").GetString() == "delivery-plan-failed");
    }

    [Fact]
    public async Task Bundle_DryRunMarkdown_PrintsFileTable()
    {
        var dir = TempDir();
        try
        {
            var outputDir = Path.Combine(dir, "deliverables", "pdf");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: outputDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath: null,
                dryRun: true,
                force: false,
                outputFormat: "markdown",
                writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# Delivery Bundle", output);
            Assert.Contains("- Mode: `dry-run`", output);
            Assert.Contains("- Bundle written: `false`", output);
            Assert.Contains("| Kind | Bytes | SHA256 | Archive path | Source path | Manifest line |", output);
            Assert.Contains("| deliverable | 9 |", output);
            Assert.Contains("| deliverables/pdf/A101.pdf |", output);
            Assert.Contains("## Issues", output);
            Assert.Contains("- None.", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundle_DryRunJson_PlansReceiptsAndDeliverableFiles()
    {
        var dir = TempDir();
        try
        {
            var outputDir = Path.Combine(dir, "deliverables", "pdf");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
            Directory.CreateDirectory(Path.Combine(outputDir, ".revitcli", "receipts"));
            File.WriteAllText(Path.Combine(outputDir, ".revitcli", "receipts", "ignored.json"), "{}");

            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: outputDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath: null,
                dryRun: true,
                force: false,
                outputFormat: "json",
                writer);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.Equal("delivery-bundle.v1", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.True(root.GetProperty("dryRun").GetBoolean());
            Assert.False(root.GetProperty("bundleWritten").GetBoolean());
            Assert.False(root.GetProperty("receiptWritten").GetBoolean());
            Assert.False(File.Exists(root.GetProperty("bundlePath").GetString()!));
            Assert.False(File.Exists(root.GetProperty("receiptPath").GetString()!));
            Assert.Equal(3, root.GetProperty("fileCount").GetInt32());
            Assert.Equal(1, root.GetProperty("receiptCount").GetInt32());
            Assert.Equal(1, root.GetProperty("deliverableCount").GetInt32());
            foreach (var file in root.GetProperty("files").EnumerateArray())
            {
                Assert.True(File.Exists(file.GetProperty("sourcePath").GetString()!), file.GetProperty("sourcePath").GetString());
                Assert.Equal(64, file.GetProperty("sha256").GetString()!.Length);
            }
            Assert.Contains(root.GetProperty("files").EnumerateArray(), file =>
                file.GetProperty("archivePath").GetString() == "deliverables/pdf/A101.pdf");
            Assert.DoesNotContain(root.GetProperty("files").EnumerateArray(), file =>
                file.GetProperty("archivePath").GetString()!.Contains("ignored.json"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundle_ExternalReceiptOutputDirectory_ReturnsFailureWithoutIncludingFiles()
    {
        var dir = TempDir();
        var externalDir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(externalDir, "outside.pdf"), "external");
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: externalDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                timestamp = "2026-05-17T12:00:00Z"
            });
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath: null,
                dryRun: true,
                force: false,
                outputFormat: "json",
                writer);

            Assert.Equal(1, exitCode);
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.False(root.GetProperty("valid").GetBoolean());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "output-dir-outside-project");
            Assert.DoesNotContain(root.GetProperty("files").EnumerateArray(), file =>
                file.GetProperty("sourcePath").GetString()!.StartsWith(externalDir, System.StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(externalDir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundle_WritesZipAndReceipt()
    {
        var dir = TempDir();
        try
        {
            var outputDir = Path.Combine(dir, "deliverables", "pdf");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: outputDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath
            });
            var bundlePath = Path.Combine(dir, "review", "package.zip");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath,
                dryRun: false,
                force: false,
                outputFormat: "table",
                writer);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(bundlePath));
            Assert.True(File.Exists(bundlePath + ".receipt.json"));
            Assert.Contains("Delivery bundle saved", writer.ToString());

            using var archive = ZipFile.OpenRead(bundlePath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Contains(".revitcli/deliveries/manifest.jsonl", entries);
            Assert.Contains(".revitcli/receipts/receipt.json", entries);
            Assert.Contains("deliverables/pdf/A101.pdf", entries);

            using var receipt = JsonDocument.Parse(File.ReadAllText(bundlePath + ".receipt.json"));
            var root = receipt.RootElement;
            Assert.Equal("delivery-bundle-receipt.v1", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("deliverables.bundle", root.GetProperty("action").GetString());
            Assert.Contains("deliverables bundle", root.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--bundle-path", root.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, root.GetProperty("fileCount").GetInt32());
            Assert.Equal(64, root.GetProperty("bundleHash").GetString()!.Length);
            foreach (var file in root.GetProperty("files").EnumerateArray())
                Assert.Equal(64, file.GetProperty("sha256").GetString()!.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundle_MissingOutputDirectory_ReturnsFailureWithoutWritingZip()
    {
        var dir = TempDir();
        try
        {
            var missingOutputDir = Path.Combine(dir, "deliverables", "missing");
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: missingOutputDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath
            });
            var bundlePath = Path.Combine(dir, "review", "package.zip");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath,
                dryRun: false,
                force: false,
                outputFormat: "json",
                writer);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(bundlePath));
            using var json = JsonDocument.Parse(writer.ToString());
            Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "output-dir-missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundle_UnwritableBundleParentPath_ReturnsFailureWithoutWritingReceipt()
    {
        var dir = TempDir();
        try
        {
            var outputDir = Path.Combine(dir, "deliverables", "pdf");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: outputDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath
            });
            var parentAsFile = Path.Combine(dir, "review-as-file");
            File.WriteAllText(parentAsFile, "not a directory");
            var bundlePath = Path.Combine(parentAsFile, "package.zip");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath,
                dryRun: false,
                force: false,
                outputFormat: "json",
                writer);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(bundlePath));
            Assert.False(File.Exists(bundlePath + ".receipt.json"));
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.False(root.GetProperty("bundleWritten").GetBoolean());
            Assert.False(root.GetProperty("receiptWritten").GetBoolean());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "bundle-write-failed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundle_ReceiptWriteFailureCleansPartialBundle()
    {
        var dir = TempDir();
        try
        {
            var outputDir = Path.Combine(dir, "deliverables", "pdf");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
            var receiptPath = WriteReceipt(dir, "export-receipt.v1", "export", outputDir: outputDir);
            WriteManifest(dir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                receiptPath
            });
            var bundlePath = Path.Combine(dir, "review", "package.zip");
            Directory.CreateDirectory(bundlePath + ".receipt.json");
            var writer = new StringWriter();

            var exitCode = await DeliverablesCommand.ExecuteBundleAsync(
                dir,
                bundlePath,
                dryRun: false,
                force: false,
                outputFormat: "json",
                writer);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(bundlePath));
            Assert.False(File.Exists(bundlePath + ".receipt.json"));
            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            Assert.False(root.GetProperty("bundleWritten").GetBoolean());
            Assert.False(root.GetProperty("receiptWritten").GetBoolean());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "bundle-receipt-write-failed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"revitcli_deliverables_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteProfile(string dir, string body)
    {
        var path = Path.Combine(dir, ".revitcli.yml");
        File.WriteAllText(path, body);
        return path;
    }

    private static string WriteBaseline(string dir, string fileName, params string[] sheetNumbers)
    {
        var path = Path.Combine(dir, fileName);
        var snapshot = new ModelSnapshot
        {
            TakenAt = "2026-05-20T00:00:00Z",
            Revit =
            {
                Document = "sample.rvt",
                DocumentPath = Path.Combine(dir, "sample.rvt")
            },
            Sheets = sheetNumbers.Select(number => new SnapshotSheet
            {
                Number = number,
                Name = $"Sheet {number}"
            }).ToList(),
            Summary =
            {
                SheetCount = sheetNumbers.Length
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot));
        return path;
    }

    private static string WriteReceipt(
        string dir,
        string schemaVersion,
        string action,
        string fileName = "receipt.json",
        string? outputDir = null)
    {
        var receiptDir = Path.Combine(dir, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var path = Path.Combine(receiptDir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion,
            action,
            success = true,
            dryRun = false,
            outputDir,
            command = $"revitcli {action}"
        }));
        return path;
    }

    private static void WriteManifest(string dir, params object[] entries)
    {
        var manifestDir = Path.Combine(dir, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        var path = Path.Combine(manifestDir, "manifest.jsonl");
        File.WriteAllLines(path, entries.Select(entry => JsonSerializer.Serialize(entry)));
    }
}
