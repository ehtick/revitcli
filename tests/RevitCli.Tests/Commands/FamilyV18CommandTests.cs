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
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// Tests for the v1.8 family completion subcommands: <c>validate</c>,
/// <c>purge</c>, <c>export</c>. The CLI side is fully covered with
/// mocked HTTP — the real Revit API integration in the addin lands on
/// Windows in a follow-up.
///
/// Each test is named (action_scenario_expectedOutcome) so a future
/// reader can scan the file and grasp the behavior surface without
/// reading bodies.
/// </summary>
public class FamilyV18CommandTests
{
    private static FamilyInfo[] SampleFamilies() => new[]
    {
        new FamilyInfo { Id = 5001, Name = "M_Single-Flush", Category = "Doors",   IsLoadable = true,  IsInPlace = false, IsPlaced = true  },
        new FamilyInfo { Id = 5002, Name = "M_Fixed",        Category = "Windows", IsLoadable = true,  IsInPlace = false, IsPlaced = false },
        new FamilyInfo { Id = 5003, Name = "InPlace-Stair",  Category = "Stairs",  IsLoadable = false, IsInPlace = true,  IsPlaced = true  },
        new FamilyInfo { Id = 5004, Name = "Bad/Name",       Category = "Walls",   IsLoadable = true,  IsInPlace = false, IsPlaced = false },
    };

    // ─── validate ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_NoIssues_ExitsZeroAndReportsClean()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Door", Category = "Doors", IsLoadable = true, IsInPlace = false }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteValidateAsync(client, null, null, "table", null, writer);

        Assert.Equal(0, exit);
        Assert.Contains("No issues", writer.ToString());
    }

    [Fact]
    public async Task Validate_NamePathChars_ExitsOneOnError()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Bad/Name", Category = "Walls", IsLoadable = true, IsInPlace = false }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteValidateAsync(client, null, null, "table", null, writer);

        Assert.Equal(1, exit);
        Assert.Contains("name-no-path-chars", writer.ToString());
    }

    [Fact]
    public async Task Validate_OnlyWarnings_DefaultFailOnError_ExitsZero()
    {
        // category-known is severity=warning. With default --fail-on=error,
        // warnings should NOT escalate to exit 1.
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Foo", Category = "Unknown", IsLoadable = true, IsInPlace = false }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteValidateAsync(client, null, null, "table", null, writer);

        Assert.Equal(0, exit);
        Assert.Contains("category-known", writer.ToString());
    }

    [Fact]
    public async Task Validate_FailOnWarning_EscalatesWarningsToExitOne()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Foo", Category = "Unknown", IsLoadable = true, IsInPlace = false }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteValidateAsync(client, null, null, "table", "warning", writer);

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Validate_RulesFromStandardsManifest_UsesFamilyRulePack()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitcli-family-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manifest = Path.Combine(root, "standards.yml");
        File.WriteAllText(manifest, """
version: 1
name: office
required:
  familyRules: [category-known]
""");
        try
        {
            var handler = new FamilyHttpHandler();
            handler.Enqueue("/api/families", HttpStatusCode.OK,
                ApiResponse<FamilyInfo[]>.Ok(new[]
                {
                    new FamilyInfo { Id = 1, Name = "Bad/Name", Category = "Unknown", IsLoadable = true, IsInPlace = false }
                }));
            var (client, writer) = MakeClientAndWriter(handler);

            var exit = await FamilyCommand.ExecuteValidateAsync(
                client,
                category: null,
                rulesCsv: null,
                outputFormat: "json",
                failOn: null,
                rulesFromManifestPath: manifest,
                output: writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            var issue = Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal("category-known", issue.GetProperty("rule").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Validate_RulesAndRulesFromTogether_ExitsOneBeforeHttp()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitcli-family-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manifest = Path.Combine(root, "standards.yml");
        File.WriteAllText(manifest, """
version: 1
name: office
required:
  familyRules: [category-known]
""");
        try
        {
            var handler = new FamilyHttpHandler();
            var (client, writer) = MakeClientAndWriter(handler);

            var exit = await FamilyCommand.ExecuteValidateAsync(
                client,
                category: null,
                rulesCsv: "name-non-empty",
                outputFormat: "table",
                failOn: null,
                rulesFromManifestPath: manifest,
                output: writer);

            Assert.Equal(1, exit);
            Assert.Contains("--rules and --rules-from", writer.ToString());
            Assert.Empty(handler.Requests);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Validate_UnknownRule_ExitsOneWithDiagnostic()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(Array.Empty<FamilyInfo>()));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteValidateAsync(client, null, "name-no-such-rule", "table", null, writer);

        Assert.Equal(1, exit);
        Assert.Contains("unknown rule", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_UnknownOutputFormat_ExitsOneBeforeHttp()
    {
        var handler = new FamilyHttpHandler();
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteValidateAsync(client, null, null, "sari", null, writer);

        Assert.Equal(1, exit);
        Assert.Contains("unknown output format", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Validate_JsonOutput_IsParseable()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Bad/Name", Category = "Walls", IsLoadable = true, IsInPlace = false }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        await FamilyCommand.ExecuteValidateAsync(client, null, null, "json", null, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Validate_SarifOutput_IsValidSarif21WithFamilyResults()
    {
        // The SARIF projection is unit-tested in
        // FamilyValidationSarifTests; this smoke-test covers the CLI
        // wire-through (--output sarif → SarifWriter.RenderFamilyValidation).
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 5001, Name = "Bad/Name", Category = "Walls", IsLoadable = true, IsInPlace = false }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        await FamilyCommand.ExecuteValidateAsync(client, null, null, "sarif", null, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
        var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("name-no-path-chars", results[0].GetProperty("ruleId").GetString());
        Assert.Equal(5001, results[0].GetProperty("properties").GetProperty("revitFamilyId").GetInt64());
    }

    // ─── purge ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Purge_DefaultNoApply_IsDryRunAndDoesNotCallPurgeEndpoint()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecutePurgeAsync(client, null, null, dryRun: false, apply: false, yes: true, writer);

        Assert.Equal(0, exit);
        Assert.Contains("Would purge", writer.ToString());
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/purge"));
    }

    [Fact]
    public async Task Purge_Apply_CallsEndpointWithUnplacedNonInPlaceIds()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        // 5002 (M_Fixed unplaced loadable) and 5004 (Bad/Name unplaced loadable)
        // are the candidates. 5001 is placed; 5003 is in-place. Both filtered out.
        handler.Enqueue("/api/families/purge", HttpStatusCode.OK,
            ApiResponse<FamilyPurgeResult>.Ok(new FamilyPurgeResult
            {
                Purged = new List<FamilyPurgedItem>
                {
                    new() { Id = 5002, Name = "M_Fixed", Category = "Windows" },
                    new() { Id = 5004, Name = "Bad/Name", Category = "Walls" },
                }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecutePurgeAsync(client, null, null, dryRun: false, apply: true, yes: true, writer);

        Assert.Equal(0, exit);
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("/api/families/purge"));
        Assert.Contains("Purged 2", writer.ToString());
    }

    [Fact]
    public async Task Purge_ApplyWithoutYes_IsRejectedBeforePurgeEndpoint()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecutePurgeAsync(client, null, null, dryRun: false, apply: true, yes: false, writer);

        Assert.Equal(1, exit);
        Assert.Contains("without --yes", writer.ToString());
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/api/families/purge"));
    }

    [Fact]
    public async Task Purge_KeepPattern_SafelistsMatchingNames()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var (client, writer) = MakeClientAndWriter(handler);

        // --keep "Fixed" should keep M_Fixed; only Bad/Name remains.
        var exit = await FamilyCommand.ExecutePurgeAsync(client, null, "Fixed", dryRun: true, apply: false, yes: true, writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.DoesNotContain("M_Fixed", output);
        Assert.Contains("Bad/Name", output);
    }

    [Fact]
    public async Task Purge_DryRunReport_WritesReviewableCandidateAndExclusionJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitcli-family-purge-report-" + Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(root, "reports", "family-purge.json");
        try
        {
            var handler = new FamilyHttpHandler();
            handler.Enqueue("/api/families", HttpStatusCode.OK,
                ApiResponse<FamilyInfo[]>.Ok(new[]
                {
                    new FamilyInfo { Id = 10, Name = "Placed Door", Category = "Doors", IsLoadable = true, IsInPlace = false, IsPlaced = true },
                    new FamilyInfo { Id = 11, Name = "Keep Window", Category = "Windows", IsLoadable = true, IsInPlace = false, IsPlaced = false },
                    new FamilyInfo { Id = 12, Name = "InPlace Casework", Category = "Casework", IsLoadable = false, IsInPlace = true, IsPlaced = false },
                    new FamilyInfo { Id = 13, Name = "Old Window", Category = "Windows", IsLoadable = true, IsInPlace = false, IsPlaced = false },
                }));
            var (client, writer) = MakeClientAndWriter(handler);

            var exit = await FamilyCommand.ExecutePurgeAsync(
                client,
                category: "Windows",
                keepCsv: "Keep",
                dryRun: true,
                apply: false,
                yes: false,
                reportPath,
                writer);

            Assert.Equal(0, exit);
            Assert.Contains("Wrote purge report:", writer.ToString());
            Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/purge"));

            using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var rootElement = doc.RootElement;
            Assert.Equal("family-purge-report.v1", rootElement.GetProperty("schema").GetString());
            Assert.Equal("dry-run", rootElement.GetProperty("mode").GetString());
            Assert.Equal("Windows", rootElement.GetProperty("filters").GetProperty("category").GetString());
            Assert.Equal("Keep", rootElement.GetProperty("filters").GetProperty("keepPatterns")[0].GetString());
            Assert.True(rootElement.GetProperty("safety").GetProperty("effectiveDryRun").GetBoolean());
            Assert.True(rootElement.GetProperty("safety").GetProperty("requiresApply").GetBoolean());

            var summary = rootElement.GetProperty("summary");
            Assert.Equal(4, summary.GetProperty("totalFamiliesReviewed").GetInt32());
            Assert.Equal(1, summary.GetProperty("candidateCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("keptByPatternCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("excludedPlacedCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("excludedInPlaceCount").GetInt32());
            Assert.Equal(13, rootElement.GetProperty("candidates")[0].GetProperty("id").GetInt64());
            Assert.Equal(11, rootElement.GetProperty("keptByPattern")[0].GetProperty("id").GetInt64());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Purge_ApplyWithoutYesReport_CapturesRefusalReason()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitcli-family-purge-refused-" + Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(root, "family-purge.json");
        try
        {
            var handler = new FamilyHttpHandler();
            handler.Enqueue("/api/families", HttpStatusCode.OK,
                ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
            var (client, writer) = MakeClientAndWriter(handler);

            var exit = await FamilyCommand.ExecutePurgeAsync(
                client,
                category: null,
                keepCsv: null,
                dryRun: false,
                apply: true,
                yes: false,
                reportPath,
                writer);

            Assert.Equal(1, exit);
            Assert.Contains("without --yes", writer.ToString());
            Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/api/families/purge"));

            using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var rootElement = doc.RootElement;
            Assert.Equal("refused", rootElement.GetProperty("mode").GetString());
            Assert.True(rootElement.GetProperty("safety").GetProperty("requiresYes").GetBoolean());
            Assert.Contains("--yes", rootElement.GetProperty("safety").GetProperty("refusedReason").GetString());
            Assert.Equal(2, rootElement.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Purge_DryRunAndApplyTogether_IsRejected()
    {
        var handler = new FamilyHttpHandler();
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecutePurgeAsync(client, null, null, dryRun: true, apply: true, yes: true, writer);

        Assert.Equal(1, exit);
        Assert.Contains("cannot be combined", writer.ToString());
        // No HTTP traffic — the rejection is purely client-side.
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Purge_PartialFailure_ExitsTwoAndReportsSkipped()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        handler.Enqueue("/api/families/purge", HttpStatusCode.OK,
            ApiResponse<FamilyPurgeResult>.Ok(new FamilyPurgeResult
            {
                Purged = new List<FamilyPurgedItem> { new() { Id = 5002, Name = "M_Fixed", Category = "Windows" } },
                Skipped = new List<FamilyPurgeSkipped>
                {
                    new() { Id = 5004, Name = "Bad/Name", Reason = "Revit refused: still referenced" }
                }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecutePurgeAsync(client, null, null, dryRun: false, apply: true, yes: true, writer);

        Assert.Equal(2, exit);
        Assert.Contains("Skipped", writer.ToString());
    }

    [Fact]
    public async Task Purge_PartialFailureReport_CapturesRevitResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitcli-family-purge-partial-" + Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(root, "family-purge.json");
        try
        {
            var handler = new FamilyHttpHandler();
            handler.Enqueue("/api/families", HttpStatusCode.OK,
                ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
            handler.Enqueue("/api/families/purge", HttpStatusCode.OK,
                ApiResponse<FamilyPurgeResult>.Ok(new FamilyPurgeResult
                {
                    Purged = new List<FamilyPurgedItem> { new() { Id = 5002, Name = "M_Fixed", Category = "Windows" } },
                    Skipped = new List<FamilyPurgeSkipped>
                    {
                        new() { Id = 5004, Name = "Bad/Name", Reason = "Revit refused: still referenced" }
                    }
                }));
            var (client, writer) = MakeClientAndWriter(handler);

            var exit = await FamilyCommand.ExecutePurgeAsync(
                client,
                category: null,
                keepCsv: null,
                dryRun: false,
                apply: true,
                yes: true,
                reportPath,
                writer);

            Assert.Equal(2, exit);
            using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var rootElement = doc.RootElement;
            Assert.Equal("partial", rootElement.GetProperty("mode").GetString());
            Assert.Equal(1, rootElement.GetProperty("summary").GetProperty("purgedCount").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("summary").GetProperty("revitSkippedCount").GetInt32());
            Assert.Equal(5002, rootElement.GetProperty("result").GetProperty("purged")[0].GetProperty("id").GetInt64());
            Assert.Equal(5004, rootElement.GetProperty("result").GetProperty("skipped")[0].GetProperty("id").GetInt64());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ─── export ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_NoFilters_RejectedAtClientSide()
    {
        var handler = new FamilyHttpHandler();
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteExportAsync(client,
            category: null, nameFilter: null, all: false,
            outputDir: "./out", overwrite: false, dryRun: true, writer);

        Assert.Equal(1, exit);
        Assert.Contains("specify --all", writer.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Export_DryRunAll_FiltersInPlaceAndNonLoadable()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteExportAsync(client,
            category: null, nameFilter: null, all: true,
            outputDir: "./out", overwrite: false, dryRun: true, writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        // Loadable + not in-place: 5001, 5002, 5004 (3 candidates).
        Assert.Contains("M_Single-Flush", output);
        Assert.Contains("M_Fixed", output);
        Assert.Contains("Bad/Name", output);
        // In-place "InPlace-Stair" must be filtered out.
        Assert.DoesNotContain("InPlace-Stair", output);
        // Dry-run does not call /export.
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/export"));
    }

    [Fact]
    public async Task Export_NameFilter_NarrowsCandidates()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteExportAsync(client,
            category: null, nameFilter: "Single", all: false,
            outputDir: "./out", overwrite: false, dryRun: true, writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("M_Single-Flush", output);
        Assert.DoesNotContain("M_Fixed", output);
    }

    [Fact]
    public async Task Export_RealApplyCallsExportEndpointWithFullOutputPath()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Door", Category = "Doors", IsLoadable = true, IsInPlace = false }
            }));
        handler.Enqueue("/api/families/export", HttpStatusCode.OK,
            ApiResponse<FamilyExportResult>.Ok(new FamilyExportResult
            {
                OutputDir = Path.GetFullPath("./out"),
                Exported = new List<FamilyExportedItem>
                {
                    new() { Id = 1, Name = "Door", Category = "Doors",
                            FilePath = Path.Combine(Path.GetFullPath("./out"), "Door.rfa"),
                            SizeBytes = 12_345 },
                }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteExportAsync(client,
            category: null, nameFilter: null, all: true,
            outputDir: "./out", overwrite: false, dryRun: false, writer);

        Assert.Equal(0, exit);
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("/api/families/export"));
        Assert.Contains("Exported 1", writer.ToString());
        Assert.Contains("Door.rfa", writer.ToString());
    }

    [Fact]
    public async Task Export_PartialFailure_ExitsTwo()
    {
        var handler = new FamilyHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[]
            {
                new FamilyInfo { Id = 1, Name = "Door", Category = "Doors", IsLoadable = true, IsInPlace = false },
                new FamilyInfo { Id = 2, Name = "Win",  Category = "Windows", IsLoadable = true, IsInPlace = false },
            }));
        handler.Enqueue("/api/families/export", HttpStatusCode.OK,
            ApiResponse<FamilyExportResult>.Ok(new FamilyExportResult
            {
                OutputDir = Path.GetFullPath("./out"),
                Exported = new List<FamilyExportedItem> { new() { Id = 1, Name = "Door", Category = "Doors", FilePath = "x.rfa" } },
                Failed = new List<FamilyExportFailure>
                {
                    new() { Id = 2, Name = "Win", Reason = "Revit returned an unexpected error during EditFamily" }
                }
            }));
        var (client, writer) = MakeClientAndWriter(handler);

        var exit = await FamilyCommand.ExecuteExportAsync(client,
            category: null, nameFilter: null, all: true,
            outputDir: "./out", overwrite: false, dryRun: false, writer);

        Assert.Equal(2, exit);
        Assert.Contains("FAILED", writer.ToString());
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static (RevitClient, StringWriter) MakeClientAndWriter(FamilyHttpHandler handler)
    {
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        return (client, new StringWriter());
    }

    private sealed class FamilyHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(string Path, HttpStatusCode Status, string Body)> _responses = new();
        public List<(string Path, string Query)> Requests { get; } = new();

        public void Enqueue<T>(string path, HttpStatusCode status, ApiResponse<T>? response)
        {
            var body = response == null ? "" : JsonSerializer.Serialize(response);
            _responses.Enqueue((path, status, body));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add((uri.AbsolutePath, uri.Query.TrimStart('?')));
            var next = _responses.Dequeue();
            Assert.Equal(next.Path, uri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
