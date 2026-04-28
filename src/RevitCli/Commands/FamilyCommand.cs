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

namespace RevitCli.Commands;

public static class FamilyCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

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
                await output.WriteLineAsync(JsonSerializer.Serialize(families, PrettyJson));
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
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, csv, sarif");
        var failOnOpt = new Option<string?>("--fail-on",
            "Exit code 1 when any issue at this severity or above is found: error|warning. " +
            "Default: error.");

        var cmd = new Command("validate", "Run built-in invariant checks on the active document's families")
        {
            categoryOpt, rulesOpt, outputOpt, failOnOpt
        };

        cmd.SetHandler(async (category, rules, outputFormat, failOn) =>
        {
            Environment.ExitCode = await ExecuteValidateAsync(
                client, category, rules, outputFormat, failOn, Console.Out);
        }, categoryOpt, rulesOpt, outputOpt, failOnOpt);

        return cmd;
    }

    public static async Task<int> ExecuteValidateAsync(
        RevitClient client, string? category, string? rulesCsv, string outputFormat, string? failOn, TextWriter output)
    {
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

        var enabled = ParseRulesCsv(rulesCsv);
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

        switch ((outputFormat ?? "table").ToLowerInvariant())
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(issues, PrettyJson));
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

        var cmd = new Command("purge", "Delete families with no placed instances from the active document")
        {
            categoryOpt, keepOpt, dryRunOpt, applyOpt, yesOpt
        };

        cmd.SetHandler(async (category, keep, dryRun, apply, yes) =>
        {
            Environment.ExitCode = await ExecutePurgeAsync(client, category, keep, dryRun, apply, yes, Console.Out);
        }, categoryOpt, keepOpt, dryRunOpt, applyOpt, yesOpt);

        return cmd;
    }

    public static async Task<int> ExecutePurgeAsync(
        RevitClient client, string? category, string? keepCsv, bool dryRun, bool apply, bool yes, TextWriter output)
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

        var keep = ParseRulesCsv(keepCsv) ?? new List<string>();
        var candidates = (listResult.Data ?? Array.Empty<FamilyInfo>())
            .Where(f => !f.IsPlaced)
            .Where(f => !MatchesKeep(f.Name, keep))
            // In-place families can't be deleted as families — they're
            // owned by the doc. Skip them silently.
            .Where(f => !f.IsInPlace)
            .ToList();

        if (candidates.Count == 0)
        {
            await output.WriteLineAsync("No purgeable families found.");
            return 0;
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
            return 0;
        }

        if (!yes)
        {
            await output.WriteLineAsync($"Refusing to purge {candidates.Count} family(ies) without --yes.");
            return 1;
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
        return data.Skipped.Count > 0 ? 2 : 0;
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
