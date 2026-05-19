using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Families;
using RevitCli.Output;
using RevitCli.Shared;
using RevitCli.Standards;

namespace RevitCli.Commands;

public static class FamilyCommand
{
    public static Command Create(RevitClient client)
    {
        var command = new Command("family", "Manage Revit families (list, validate, purge, export)");
        command.AddCommand(CreateListCommand(client));
        command.AddCommand(CreateValidateCommand(client));
        command.AddCommand(CreatePurgeCommand(client));
        command.AddCommand(CreateExportCommand(client));
        return command;
    }

    private static Command CreateListCommand(RevitClient client)
    {
        var unusedOpt = new Option<bool>(
            "--unused",
            () => false,
            "Only list families with zero placed FamilyInstances");
        var categoryOpt = new Option<string?>(
            "--category",
            "Filter by Revit category name (e.g. Doors, Windows)");
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, csv");

        var cmd = new Command("ls", "List families in the active Revit document")
        {
            unusedOpt, categoryOpt, outputOpt
        };

        cmd.SetHandler(async (unused, category, output) =>
        {
            Environment.ExitCode = await ExecuteListAsync(client, unused, category, output, Console.Out);
        }, unusedOpt, categoryOpt, outputOpt);

        return cmd;
    }

    public static async Task<int> ExecuteListAsync(
        RevitClient client,
        bool unused,
        string? category,
        string outputFormat,
        TextWriter output)
    {
        var request = new FamilyListRequest
        {
            IncludeUnplaced = unused,
            Category = string.IsNullOrWhiteSpace(category) ? null : category
        };

        var result = await client.ListFamiliesAsync(request);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var families = result.Data ?? Array.Empty<FamilyInfo>();

        switch ((outputFormat ?? "table").ToLowerInvariant())
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(families, TerminalJsonOptions.Pretty));
                break;
            case "csv":
                await output.WriteLineAsync(FormatCsv(families));
                break;
            default:
                await output.WriteLineAsync(FormatTable(families));
                break;
        }

        return 0;
    }

    private static string FormatTable(FamilyInfo[] families)
    {
        if (families.Length == 0)
            return "No families found.";

        var sb = new StringBuilder();
        sb.AppendLine($"{"Id",-10} {"Name",-32} {"Category",-18} {"InPlace",-8} {"Placed",-7} FilePath");
        sb.AppendLine(new string('-', 90));
        foreach (var f in families)
        {
            sb.AppendLine(
                $"{f.Id,-10} {Truncate(f.Name, 32),-32} {Truncate(f.Category, 18),-18} " +
                $"{(f.IsInPlace ? "yes" : "no"),-8} {(f.IsPlaced ? "yes" : "no"),-7} {f.FilePath ?? ""}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatCsv(FamilyInfo[] families)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Category,IsInPlace,IsPlaced,FilePath");
        foreach (var f in families)
        {
            sb.AppendLine(string.Join(",",
                f.Id.ToString(),
                EscapeCsvField(f.Name),
                EscapeCsvField(f.Category),
                f.IsInPlace ? "true" : "false",
                f.IsPlaced ? "true" : "false",
                EscapeCsvField(f.FilePath ?? "")));
        }
        return sb.ToString().TrimEnd();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";
    }

    // ─── family validate ──────────────────────────────────────────────────

    private static Command CreateValidateCommand(RevitClient client)
    {
        var categoryOpt = new Option<string?>("--category", "Filter to a single Revit category");
        var rulesOpt = new Option<string?>("--rules",
            "Comma-separated list of rule ids to run (default: all). " +
            $"Available: {string.Join(", ", FamilyValidator.AllRuleIds)}");
        var rulesFromOpt = new Option<string?>("--rules-from",
            "Standards manifest path to read required.familyRules");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, csv, sarif");
        var failOnOpt = new Option<string?>("--fail-on",
            "Exit code 1 when any issue at this severity or above is found: error|warning. " +
            "Default: error.");

        var cmd = new Command("validate", "Run built-in invariant checks on the active document's families")
        {
            categoryOpt, rulesOpt, rulesFromOpt, outputOpt, failOnOpt
        };

        cmd.SetHandler(async (category, rules, rulesFrom, outputFormat, failOn) =>
        {
            Environment.ExitCode = await ExecuteValidateAsync(
                client, category, rules, outputFormat, failOn, rulesFrom, Console.Out);
        }, categoryOpt, rulesOpt, rulesFromOpt, outputOpt, failOnOpt);

        return cmd;
    }

    public static async Task<int> ExecuteValidateAsync(
        RevitClient client, string? category, string? rulesCsv, string outputFormat, string? failOn, TextWriter output)
        => await ExecuteValidateAsync(client, category, rulesCsv, outputFormat, failOn, rulesFromManifestPath: null, output);

    public static async Task<int> ExecuteValidateAsync(
        RevitClient client,
        string? category,
        string? rulesCsv,
        string outputFormat,
        string? failOn,
        string? rulesFromManifestPath,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "csv", "sarif"))
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, csv, sarif.");
            return 1;
        }

        IReadOnlyCollection<string>? enabled;
        try
        {
            enabled = ResolveEnabledRules(rulesCsv, rulesFromManifestPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or YamlDotNet.Core.YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var listResult = await client.ListFamiliesAsync(new FamilyListRequest
        {
            // Validate against ALL families (placed and unplaced) — corrupted
            // unplaced families are exactly the ones we want to catch.
            IncludeUnplaced = true,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
        });
        if (!listResult.Success)
        {
            await output.WriteLineAsync($"Error: {listResult.Error}");
            return 1;
        }

        var unknown = enabled?.Where(r => !FamilyValidator.AllRuleIds.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown is { Count: > 0 })
        {
            await output.WriteLineAsync(
                $"Error: unknown rule(s): {string.Join(", ", unknown)}. " +
                $"Available: {string.Join(", ", FamilyValidator.AllRuleIds)}");
            return 1;
        }

        var families = listResult.Data ?? Array.Empty<FamilyInfo>();
        var issues = FamilyValidator.Validate(families, enabled);

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(issues, TerminalJsonOptions.Pretty));
                break;
            case "csv":
                await output.WriteLineAsync(FormatValidationCsv(issues));
                break;
            case "sarif":
                // Same SARIF 2.1.0 envelope as `audit --output sarif` so
                // GitHub Code Scanning can ingest both uniformly. Family
                // issues carry revitFamilyId / revitFamilyName under
                // `properties` and `family:<name>#<id>` logical locations.
                await output.WriteLineAsync(
                    Reports.SarifWriter.RenderFamilyValidation(issues));
                break;
            default:
                await output.WriteLineAsync(FormatValidationTable(issues, families.Length));
                break;
        }

        return DecideValidateExitCode(issues, failOn);
    }

    private static IReadOnlyCollection<string>? ResolveEnabledRules(string? rulesCsv, string? rulesFromManifestPath)
    {
        if (!string.IsNullOrWhiteSpace(rulesCsv) && !string.IsNullOrWhiteSpace(rulesFromManifestPath))
        {
            throw new InvalidOperationException("--rules and --rules-from cannot be used together.");
        }

        if (string.IsNullOrWhiteSpace(rulesFromManifestPath))
        {
            return ParseRulesCsv(rulesCsv);
        }

        var path = Path.GetFullPath(rulesFromManifestPath!);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Standards manifest not found: {path}", path);
        }

        var manifest = StandardsValidator.LoadManifest(path);
        var rules = manifest.Required.FamilyRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Select(rule => rule.Trim())
            .ToArray();
        if (rules.Length == 0)
        {
            throw new InvalidOperationException(
                $"Standards manifest has no required.familyRules entries: {path}");
        }

        return rules;
    }

    private static int DecideValidateExitCode(IReadOnlyList<FamilyValidationIssue> issues, string? failOn)
    {
        // Default: fail only on `error` severity, matching `audit` semantics.
        var threshold = (failOn ?? "error").Trim().ToLowerInvariant();
        return threshold switch
        {
            "warning" => issues.Count > 0 ? 1 : 0,
            _         => issues.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)) ? 1 : 0,
        };
    }

    private static string FormatValidationTable(IReadOnlyList<FamilyValidationIssue> issues, int totalFamilies)
    {
        if (issues.Count == 0)
            return $"No issues found across {totalFamilies} family(ies).";

        var sb = new StringBuilder();
        sb.AppendLine($"{"Severity",-9} {"Rule",-22} {"Family",-32} Message");
        sb.AppendLine(new string('-', 90));
        foreach (var i in issues)
        {
            sb.AppendLine($"{i.Severity,-9} {Truncate(i.Rule, 22),-22} {Truncate(i.FamilyName, 32),-32} {i.Message}");
        }
        sb.AppendLine();
        sb.AppendLine($"{issues.Count} issue(s) across {totalFamilies} family(ies).");
        return sb.ToString().TrimEnd();
    }

    private static string FormatValidationCsv(IReadOnlyList<FamilyValidationIssue> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Severity,Rule,FamilyId,FamilyName,Category,Message");
        foreach (var i in issues)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsvField(i.Severity),
                EscapeCsvField(i.Rule),
                i.FamilyId.ToString(),
                EscapeCsvField(i.FamilyName),
                EscapeCsvField(i.Category),
                EscapeCsvField(i.Message)));
        }
        return sb.ToString().TrimEnd();
    }

    // ─── family purge ─────────────────────────────────────────────────────

    private static Command CreatePurgeCommand(RevitClient client)
    {
        var categoryOpt = new Option<string?>("--category", "Limit to a single Revit category");
        var keepOpt = new Option<string?>("--keep",
            "Comma-separated substring patterns to safelist (case-insensitive); " +
            "matching family names are NEVER purged.");
        var dryRunOpt = new Option<bool>("--dry-run", "Show what would be purged without modifying the document");
        var applyOpt = new Option<bool>("--apply", "Actually delete the families. Required for non-dry-run.");
        var yesOpt = new Option<bool>("--yes", "Skip the interactive confirmation (CI-friendly)");
        var reportOpt = new Option<string?>("--report", "Write a JSON purge review report to file");

        var cmd = new Command("purge", "Delete families with no placed instances from the active document")
        {
            categoryOpt, keepOpt, dryRunOpt, applyOpt, yesOpt, reportOpt
        };

        cmd.SetHandler(async (category, keep, dryRun, apply, yes, report) =>
        {
            Environment.ExitCode = await ExecutePurgeAsync(
                client, category, keep, dryRun, apply, yes, report, Console.Out);
        }, categoryOpt, keepOpt, dryRunOpt, applyOpt, yesOpt, reportOpt);

        return cmd;
    }

    public static async Task<int> ExecutePurgeAsync(
        RevitClient client, string? category, string? keepCsv, bool dryRun, bool apply, bool yes, TextWriter output)
        => await ExecutePurgeAsync(client, category, keepCsv, dryRun, apply, yes, reportPath: null, output);

    public static async Task<int> ExecutePurgeAsync(
        RevitClient client,
        string? category,
        string? keepCsv,
        bool dryRun,
        bool apply,
        bool yes,
        string? reportPath,
        TextWriter output)
    {
        if (dryRun && apply)
        {
            await output.WriteLineAsync("Error: --dry-run and --apply cannot be combined.");
            return 1;
        }

        // Step 1: enumerate all families and let the CLI decide which to drop.
        // Doing this client-side keeps the addin endpoint dumb (it just deletes
        // the listed ids) and keeps the --keep / --category logic testable
        // without a real Revit doc.
        var listResult = await client.ListFamiliesAsync(new FamilyListRequest
        {
            IncludeUnplaced = true,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
        });
        if (!listResult.Success)
        {
            await output.WriteLineAsync($"Error: {listResult.Error}");
            return 1;
        }

        var families = listResult.Data ?? Array.Empty<FamilyInfo>();
        var keep = ParseRulesCsv(keepCsv) ?? new List<string>();
        var selection = SelectPurgeCandidates(families, keep);
        var candidates = selection.Candidates;

        if (candidates.Count == 0)
        {
            await output.WriteLineAsync("No purgeable families found.");
            return await WritePurgeReportAndReturnAsync(
                reportPath,
                BuildPurgeReport(
                    families, selection, category, keep, dryRun, apply, yes,
                    mode: "no-candidates", exitCode: 0),
                exitCode: 0,
                output);
        }

        // Effective dry-run: explicit --dry-run, OR no --apply, OR neither flag.
        var effectiveDryRun = dryRun || !apply;

        if (effectiveDryRun)
        {
            await output.WriteLineAsync($"Would purge {candidates.Count} family(ies):");
            foreach (var f in candidates.Take(50))
                await output.WriteLineAsync($"  [{f.Id}] {f.Name} ({f.Category})");
            if (candidates.Count > 50)
                await output.WriteLineAsync($"  ... and {candidates.Count - 50} more.");
            await output.WriteLineAsync($"Re-run with --apply to delete (or --apply --yes for non-interactive).");
            return await WritePurgeReportAndReturnAsync(
                reportPath,
                BuildPurgeReport(
                    families, selection, category, keep, dryRun, apply, yes,
                    mode: "dry-run", exitCode: 0),
                exitCode: 0,
                output);
        }

        if (!yes)
        {
            await output.WriteLineAsync($"Refusing to purge {candidates.Count} family(ies) without --yes.");
            return await WritePurgeReportAndReturnAsync(
                reportPath,
                BuildPurgeReport(
                    families, selection, category, keep, dryRun, apply, yes,
                    mode: "refused", exitCode: 1,
                    refusedReason: "--yes is required when --apply would delete families."),
                exitCode: 1,
                output);
        }

        var purgeResult = await client.PurgeFamiliesAsync(new FamilyPurgeRequest
        {
            Ids = candidates.Select(f => f.Id).ToList(),
            DryRun = false,
        });
        if (!purgeResult.Success)
        {
            await output.WriteLineAsync($"Error: {purgeResult.Error}");
            return 1;
        }

        var data = purgeResult.Data!;
        await output.WriteLineAsync($"Purged {data.Purged.Count} family(ies).");
        foreach (var s in data.Skipped)
            await output.WriteLineAsync($"  Skipped [{s.Id}] {s.Name}: {s.Reason}");

        LogPurgeOperation(data, category, keep);
        var exit = data.Skipped.Count > 0 ? 2 : 0;
        return await WritePurgeReportAndReturnAsync(
            reportPath,
            BuildPurgeReport(
                families, selection, category, keep, dryRun, apply, yes,
                mode: data.Skipped.Count > 0 ? "partial" : "applied",
                exitCode: exit,
                result: data),
            exit,
            output);
    }

    private static FamilyPurgeSelection SelectPurgeCandidates(FamilyInfo[] families, IReadOnlyList<string> keep)
    {
        var selection = new FamilyPurgeSelection();
        foreach (var family in families)
        {
            if (MatchesKeep(family.Name, keep))
            {
                selection.KeptByPattern.Add(family);
                continue;
            }

            if (family.IsPlaced)
            {
                selection.ExcludedPlaced.Add(family);
                continue;
            }

            // In-place families can't be deleted as families; they're owned
            // by the project document rather than a loadable family asset.
            if (family.IsInPlace)
            {
                selection.ExcludedInPlace.Add(family);
                continue;
            }

            selection.Candidates.Add(family);
        }

        return selection;
    }

    private static bool MatchesKeep(string name, IReadOnlyList<string> keepPatterns)
    {
        if (keepPatterns.Count == 0 || string.IsNullOrEmpty(name)) return false;
        foreach (var pat in keepPatterns)
        {
            if (!string.IsNullOrEmpty(pat)
                && name.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static void LogPurgeOperation(FamilyPurgeResult result, string? category, IReadOnlyList<string> keep)
    {
        var profileDir = Profile.ProfileLoader.Discover() is { } p
            ? Path.GetDirectoryName(Path.GetFullPath(p))
            : null;
        JournalLogger.Log(profileDir, new
        {
            action = "family-purge",
            purged = result.Purged.Count,
            skipped = result.Skipped.Count,
            category,
            keepPatterns = keep,
            timestamp = DateTime.UtcNow.ToString("o"),
            user = Environment.UserName,
        });
    }

    private static FamilyPurgeReport BuildPurgeReport(
        FamilyInfo[] families,
        FamilyPurgeSelection selection,
        string? category,
        IReadOnlyList<string> keep,
        bool dryRun,
        bool apply,
        bool yes,
        string mode,
        int exitCode,
        string? refusedReason = null,
        FamilyPurgeResult? result = null)
    {
        var purged = result?.Purged ?? new List<FamilyPurgedItem>();
        var skipped = result?.Skipped ?? new List<FamilyPurgeSkipped>();

        return new FamilyPurgeReport
        {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Mode = mode,
            ExitCode = exitCode,
            Operator = Environment.UserName,
            Machine = Environment.MachineName,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Filters = new FamilyPurgeReportFilters
            {
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                KeepPatterns = keep.ToArray(),
            },
            Safety = new FamilyPurgeReportSafety
            {
                DryRunRequested = dryRun,
                ApplyRequested = apply,
                Confirmed = yes,
                EffectiveDryRun = dryRun || !apply,
                RequiresApply = !apply,
                RequiresYes = apply && !yes,
                RefusedReason = refusedReason,
            },
            Summary = new FamilyPurgeReportSummary
            {
                TotalFamiliesReviewed = families.Length,
                CandidateCount = selection.Candidates.Count,
                KeptByPatternCount = selection.KeptByPattern.Count,
                ExcludedPlacedCount = selection.ExcludedPlaced.Count,
                ExcludedInPlaceCount = selection.ExcludedInPlace.Count,
                PurgedCount = purged.Count,
                RevitSkippedCount = skipped.Count,
            },
            Candidates = selection.Candidates.Select(ToReportItem).ToArray(),
            KeptByPattern = selection.KeptByPattern.Select(ToReportItem).ToArray(),
            ExcludedPlaced = selection.ExcludedPlaced.Select(ToReportItem).ToArray(),
            ExcludedInPlace = selection.ExcludedInPlace.Select(ToReportItem).ToArray(),
            Result = new FamilyPurgeReportResult
            {
                Purged = purged
                    .Select(item => new FamilyPurgeResultItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Category = item.Category,
                    })
                    .ToArray(),
                Skipped = skipped
                    .Select(item => new FamilyPurgeResultSkipped
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Reason = item.Reason,
                    })
                    .ToArray(),
            },
        };
    }

    private static FamilyPurgeReportItem ToReportItem(FamilyInfo family) => new()
    {
        Id = family.Id,
        Name = family.Name,
        Category = family.Category,
        IsLoadable = family.IsLoadable,
        IsInPlace = family.IsInPlace,
        IsPlaced = family.IsPlaced,
        FilePath = family.FilePath,
    };

    private static async Task<int> WritePurgeReportAndReturnAsync(
        string? reportPath,
        FamilyPurgeReport report,
        int exitCode,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
            return exitCode;

        try
        {
            var fullPath = Path.GetFullPath(reportPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel);
            await File.WriteAllTextAsync(fullPath, json + Environment.NewLine);
            await output.WriteLineAsync($"Wrote purge report: {fullPath}");
            return exitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await output.WriteLineAsync($"Error: failed to write purge report: {ex.Message}");
            return 1;
        }
    }

    private sealed class FamilyPurgeSelection
    {
        public List<FamilyInfo> Candidates { get; } = new();
        public List<FamilyInfo> KeptByPattern { get; } = new();
        public List<FamilyInfo> ExcludedPlaced { get; } = new();
        public List<FamilyInfo> ExcludedInPlace { get; } = new();
    }

    private sealed class FamilyPurgeReport
    {
        public string Schema { get; init; } = "family-purge-report.v1";
        public string Command { get; init; } = "family purge";
        public string GeneratedAt { get; init; } = "";
        public string Mode { get; init; } = "";
        public int ExitCode { get; init; }
        public string Operator { get; init; } = "";
        public string Machine { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public FamilyPurgeReportFilters Filters { get; init; } = new();
        public FamilyPurgeReportSafety Safety { get; init; } = new();
        public FamilyPurgeReportSummary Summary { get; init; } = new();
        public FamilyPurgeReportItem[] Candidates { get; init; } = Array.Empty<FamilyPurgeReportItem>();
        public FamilyPurgeReportItem[] KeptByPattern { get; init; } = Array.Empty<FamilyPurgeReportItem>();
        public FamilyPurgeReportItem[] ExcludedPlaced { get; init; } = Array.Empty<FamilyPurgeReportItem>();
        public FamilyPurgeReportItem[] ExcludedInPlace { get; init; } = Array.Empty<FamilyPurgeReportItem>();
        public FamilyPurgeReportResult Result { get; init; } = new();
    }

    private sealed class FamilyPurgeReportFilters
    {
        public string? Category { get; init; }
        public string[] KeepPatterns { get; init; } = Array.Empty<string>();
    }

    private sealed class FamilyPurgeReportSafety
    {
        public bool DryRunRequested { get; init; }
        public bool ApplyRequested { get; init; }
        public bool Confirmed { get; init; }
        public bool EffectiveDryRun { get; init; }
        public bool RequiresApply { get; init; }
        public bool RequiresYes { get; init; }
        public string? RefusedReason { get; init; }
    }

    private sealed class FamilyPurgeReportSummary
    {
        public int TotalFamiliesReviewed { get; init; }
        public int CandidateCount { get; init; }
        public int KeptByPatternCount { get; init; }
        public int ExcludedPlacedCount { get; init; }
        public int ExcludedInPlaceCount { get; init; }
        public int PurgedCount { get; init; }
        public int RevitSkippedCount { get; init; }
    }

    private sealed class FamilyPurgeReportItem
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public bool IsLoadable { get; init; }
        public bool IsInPlace { get; init; }
        public bool IsPlaced { get; init; }
        public string? FilePath { get; init; }
    }

    private sealed class FamilyPurgeReportResult
    {
        public FamilyPurgeResultItem[] Purged { get; init; } = Array.Empty<FamilyPurgeResultItem>();
        public FamilyPurgeResultSkipped[] Skipped { get; init; } = Array.Empty<FamilyPurgeResultSkipped>();
    }

    private sealed class FamilyPurgeResultItem
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
    }

    private sealed class FamilyPurgeResultSkipped
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string Reason { get; init; } = "";
    }

    // ─── family export ────────────────────────────────────────────────────

    private static Command CreateExportCommand(RevitClient client)
    {
        var categoryOpt = new Option<string?>("--category", "Limit to a single Revit category");
        var nameOpt = new Option<string?>("--name", "Export only families whose name contains this substring (case-insensitive)");
        var allOpt = new Option<bool>("--all", "Export every family in the active document");
        var outputDirOpt = new Option<string>("--output-dir", () => "./families", "Directory to save .rfa files into");
        var overwriteOpt = new Option<bool>("--overwrite", "Overwrite existing .rfa files in the output directory");
        var dryRunOpt = new Option<bool>("--dry-run", "List which families would be exported without saving");

        var cmd = new Command("export", "Save families as standalone .rfa files")
        {
            categoryOpt, nameOpt, allOpt, outputDirOpt, overwriteOpt, dryRunOpt
        };

        cmd.SetHandler(async (category, name, all, outputDir, overwrite, dryRun) =>
        {
            Environment.ExitCode = await ExecuteExportAsync(client, category, name, all, outputDir, overwrite, dryRun, Console.Out);
        }, categoryOpt, nameOpt, allOpt, outputDirOpt, overwriteOpt, dryRunOpt);

        return cmd;
    }

    public static async Task<int> ExecuteExportAsync(
        RevitClient client, string? category, string? nameFilter, bool all, string outputDir, bool overwrite, bool dryRun,
        TextWriter output)
    {
        if (!all && string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(nameFilter))
        {
            await output.WriteLineAsync("Error: specify --all OR --category OR --name to scope the export.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            await output.WriteLineAsync("Error: --output-dir is required.");
            return 1;
        }

        var listResult = await client.ListFamiliesAsync(new FamilyListRequest
        {
            IncludeUnplaced = true,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
        });
        if (!listResult.Success)
        {
            await output.WriteLineAsync($"Error: {listResult.Error}");
            return 1;
        }

        var candidates = (listResult.Data ?? Array.Empty<FamilyInfo>())
            // In-place families cannot be saved as standalone .rfa.
            .Where(f => !f.IsInPlace)
            .Where(f => f.IsLoadable)
            .Where(f => string.IsNullOrEmpty(nameFilter)
                || f.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (candidates.Count == 0)
        {
            await output.WriteLineAsync("No exportable families matched the filters.");
            return 0;
        }

        if (dryRun)
        {
            await output.WriteLineAsync($"Would export {candidates.Count} family(ies) to {Path.GetFullPath(outputDir)}:");
            foreach (var f in candidates.Take(50))
                await output.WriteLineAsync($"  [{f.Id}] {f.Name} ({f.Category}) -> {f.Name}.rfa");
            if (candidates.Count > 50)
                await output.WriteLineAsync($"  ... and {candidates.Count - 50} more.");
            return 0;
        }

        var exportResult = await client.ExportFamiliesAsync(new FamilyExportRequest
        {
            Ids = candidates.Select(f => f.Id).ToList(),
            OutputDir = Path.GetFullPath(outputDir),
            Overwrite = overwrite,
            DryRun = false,
        });
        if (!exportResult.Success)
        {
            await output.WriteLineAsync($"Error: {exportResult.Error}");
            return 1;
        }

        var data = exportResult.Data!;
        await output.WriteLineAsync($"Exported {data.Exported.Count} family(ies) to {data.OutputDir}.");
        foreach (var e in data.Exported.Take(50))
            await output.WriteLineAsync($"  {e.Name}.rfa  ({e.SizeBytes:N0} bytes)");
        foreach (var f in data.Failed)
            await output.WriteLineAsync($"  FAILED [{f.Id}] {f.Name}: {f.Reason}");

        LogExportOperation(data, category, nameFilter);
        return data.Failed.Count > 0 ? 2 : 0;
    }

    private static void LogExportOperation(FamilyExportResult result, string? category, string? nameFilter)
    {
        var profileDir = Profile.ProfileLoader.Discover() is { } p
            ? Path.GetDirectoryName(Path.GetFullPath(p))
            : null;
        JournalLogger.Log(profileDir, new
        {
            action = "family-export",
            exported = result.Exported.Count,
            failed = result.Failed.Count,
            outputDir = result.OutputDir,
            category,
            nameFilter,
            timestamp = DateTime.UtcNow.ToString("o"),
            user = Environment.UserName,
        });
    }

    // ─── shared helpers ───────────────────────────────────────────────────

    private static List<string>? ParseRulesCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
