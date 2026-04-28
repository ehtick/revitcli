using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.History;
using RevitCli.Shared;

namespace RevitCli.Commands;

/// <summary>
/// CLI surface for the v1.6 history feature. Commands:
/// <list type="bullet">
///   <item><c>history init</c> - create the directory + empty index.</item>
///   <item><c>history capture</c> - capture a fresh snapshot via the addin and append it.</item>
///   <item><c>history list</c> - tabular listing of stored snapshots.</item>
///   <item><c>history prune</c> - drop entries older than retention or beyond a count cap.</item>
/// </list>
/// Exit codes follow the project convention: <c>0</c> on success, <c>1</c> on
/// user/usage errors and Revit communication failures.
/// </summary>
public static class HistoryCommand
{
    public static Command Create(RevitClient client)
    {
        var command = new Command("history", "Manage RevitCli snapshot history (v1.6)");
        command.AddCommand(CreateInitCommand());
        command.AddCommand(CreateCaptureCommand(client));
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreatePruneCommand());
        return command;
    }

    // ------------------------------------------------------------------
    // init
    // ------------------------------------------------------------------

    private static Command CreateInitCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Override history directory (default: .revitcli/history)");
        var cmd = new Command("init", "Create .revitcli/history/ and an empty index")
        {
            dirOpt,
        };
        cmd.SetHandler(async (string? dir) =>
        {
            Environment.ExitCode = await ExecuteInitAsync(dir, Console.Out);
        }, dirOpt);
        return cmd;
    }

    public static async Task<int> ExecuteInitAsync(string? overrideDir, TextWriter output)
    {
        try
        {
            var store = ResolveStore(overrideDir);
            var created = await store.InitAsync();
            await output.WriteLineAsync(created
                ? $"Initialized history store at {store.RootDirectory}"
                : $"History store already exists at {store.RootDirectory}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await output.WriteLineAsync($"Error: failed to initialise history store: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // capture
    // ------------------------------------------------------------------

    private static Command CreateCaptureCommand(RevitClient client)
    {
        var sourceOpt = new Option<string>("--source", () => "manual",
            "Capture source label (manual | cron | fix-baseline | <custom>)");
        var excludeFixesOpt = new Option<bool>("--exclude-fixes", () => true,
            "Reserved: exclude fix-baseline-style content from capture (default: true)");
        var dirOpt = new Option<string?>("--dir", "Override history directory");
        var cmd = new Command("capture", "Capture a snapshot via the addin and append it to history")
        {
            sourceOpt,
            excludeFixesOpt,
            dirOpt,
        };

        cmd.SetHandler(async (string source, bool excludeFixes, string? dir) =>
        {
            Environment.ExitCode = await ExecuteCaptureAsync(client, source, excludeFixes, dir, Console.Out);
        }, sourceOpt, excludeFixesOpt, dirOpt);

        return cmd;
    }

    public static async Task<int> ExecuteCaptureAsync(
        RevitClient client,
        string source,
        bool excludeFixes,
        string? overrideDir,
        TextWriter output)
    {
        if (client == null)
        {
            await output.WriteLineAsync("Error: Revit client unavailable.");
            return 1;
        }

        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim();

        ApiResponse<ModelSnapshot> response;
        try
        {
            response = await client.CaptureSnapshotAsync(new SnapshotRequest());
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to capture snapshot: {ex.Message}");
            return 1;
        }

        if (response == null || !response.Success || response.Data == null)
        {
            await output.WriteLineAsync($"Error: {response?.Error ?? "snapshot capture returned no data."}");
            return 1;
        }

        // The --exclude-fixes flag is wired through but presently has no effect on the
        // captured payload — fix-baseline content is exclusively populated by the
        // FixCommand integration scheduled for v1.5 round 2. We keep the flag here
        // so the CLI surface is stable when that work lands.
        _ = excludeFixes;

        try
        {
            var store = ResolveStore(overrideDir);
            await store.InitAsync();
            var meta = await store.AppendAsync(response.Data, resolvedSource);
            await output.WriteLineAsync(
                $"Captured {meta.Id} ({meta.ElementCount} elements, {FormatBytes(meta.Size)}) source={meta.Source}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await output.WriteLineAsync($"Error: failed to write history entry: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // list
    // ------------------------------------------------------------------

    private static Command CreateListCommand()
    {
        var includeFixesOpt = new Option<bool>("--include-fixes", () => false,
            "Include entries marked source=fix-baseline (default: hidden)");
        var limitOpt = new Option<int>("--limit", () => 20, "Maximum number of rows to display");
        var dirOpt = new Option<string?>("--dir", "Override history directory");
        var cmd = new Command("list", "List captured snapshots")
        {
            includeFixesOpt,
            limitOpt,
            dirOpt,
        };

        cmd.SetHandler(async (bool includeFixes, int limit, string? dir) =>
        {
            Environment.ExitCode = await ExecuteListAsync(includeFixes, limit, dir, Console.Out);
        }, includeFixesOpt, limitOpt, dirOpt);

        return cmd;
    }

    public static async Task<int> ExecuteListAsync(
        bool includeFixes,
        int limit,
        string? overrideDir,
        TextWriter output)
    {
        if (limit <= 0)
        {
            await output.WriteLineAsync("Error: --limit must be greater than 0.");
            return 1;
        }

        try
        {
            var store = ResolveStore(overrideDir);
            if (!Directory.Exists(store.RootDirectory))
            {
                await output.WriteLineAsync(
                    $"History store not initialised. Run 'revitcli history init' (looked in {store.RootDirectory}).");
                return 0;
            }

            var entries = await store.ListAsync(includeFixes);
            if (entries.Count == 0)
            {
                await output.WriteLineAsync("No snapshots recorded.");
                return 0;
            }

            var rows = entries.Take(limit).ToList();
            await WriteListTableAsync(rows, output);
            if (entries.Count > rows.Count)
            {
                await output.WriteLineAsync(
                    $"... {entries.Count - rows.Count} older entry(ies) hidden (raise --limit to see more).");
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await output.WriteLineAsync($"Error: failed to read history: {ex.Message}");
            return 1;
        }
    }

    private static async Task WriteListTableAsync(IReadOnlyList<SnapshotMetadata> rows, TextWriter output)
    {
        // We render a plain ASCII table to keep test output deterministic and
        // independent of the Spectre.Console terminal width detection.
        const string idHeader = "id";
        const string capturedHeader = "capturedAt";
        const string sourceHeader = "source";
        const string countHeader = "elements";
        const string sizeHeader = "size";

        var idWidth = Math.Max(idHeader.Length, rows.Max(r => r.Id?.Length ?? 0));
        var capturedWidth = Math.Max(capturedHeader.Length, rows.Max(r => r.CapturedAt?.Length ?? 0));
        var sourceWidth = Math.Max(sourceHeader.Length, rows.Max(r => r.Source?.Length ?? 0));
        var countWidth = Math.Max(countHeader.Length, rows.Max(r =>
            r.ElementCount.ToString(CultureInfo.InvariantCulture).Length));
        var sizeStrings = rows.ToDictionary(r => r.Id ?? string.Empty, r => FormatBytes(r.Size), StringComparer.Ordinal);
        var sizeWidth = Math.Max(sizeHeader.Length, sizeStrings.Values.Max(s => s.Length));

        await output.WriteLineAsync(string.Join("  ", new[]
        {
            idHeader.PadRight(idWidth),
            capturedHeader.PadRight(capturedWidth),
            sourceHeader.PadRight(sourceWidth),
            countHeader.PadLeft(countWidth),
            sizeHeader.PadLeft(sizeWidth),
        }));

        foreach (var row in rows)
        {
            await output.WriteLineAsync(string.Join("  ", new[]
            {
                (row.Id ?? string.Empty).PadRight(idWidth),
                (row.CapturedAt ?? string.Empty).PadRight(capturedWidth),
                (row.Source ?? string.Empty).PadRight(sourceWidth),
                row.ElementCount.ToString(CultureInfo.InvariantCulture).PadLeft(countWidth),
                sizeStrings[row.Id ?? string.Empty].PadLeft(sizeWidth),
            }));
        }
    }

    // ------------------------------------------------------------------
    // prune
    // ------------------------------------------------------------------

    private static Command CreatePruneCommand()
    {
        var keepOpt = new Option<string?>("--keep",
            "Retention spec: duration (7d / 24h / 30m) or 'count:N' to cap by count");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview removals without deleting (default)");
        var applyOpt = new Option<bool>("--apply", () => false, "Actually delete files and rewrite the index");
        var includeFixesOpt = new Option<bool>("--include-fixes", () => false,
            "Allow pruning of fix-baseline entries (default: protected)");
        var dirOpt = new Option<string?>("--dir", "Override history directory");

        var cmd = new Command("prune", "Remove history entries beyond retention")
        {
            keepOpt,
            dryRunOpt,
            applyOpt,
            includeFixesOpt,
            dirOpt,
        };

        cmd.SetHandler(async (string? keep, bool dryRun, bool apply, bool includeFixes, string? dir) =>
        {
            Environment.ExitCode = await ExecutePruneAsync(keep, dryRun, apply, includeFixes, dir, Console.Out);
        }, keepOpt, dryRunOpt, applyOpt, includeFixesOpt, dirOpt);

        return cmd;
    }

    public static async Task<int> ExecutePruneAsync(
        string? keep,
        bool dryRun,
        bool apply,
        bool includeFixes,
        string? overrideDir,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(keep))
        {
            await output.WriteLineAsync("Error: --keep is required (e.g. --keep 30d or --keep count:50).");
            return 1;
        }

        if (apply && dryRun)
        {
            // Default for --dry-run is true; if the user explicitly passed --apply
            // we honor it. Treat the combination as "user wants apply".
            dryRun = false;
        }

        TimeSpan? maxAge = null;
        int? maxCount = null;
        try
        {
            ParseRetention(keep, out maxAge, out maxCount);
        }
        catch (FormatException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        try
        {
            var store = ResolveStore(overrideDir);
            if (!Directory.Exists(store.RootDirectory))
            {
                await output.WriteLineAsync(
                    $"History store not initialised. Run 'revitcli history init' (looked in {store.RootDirectory}).");
                return 0;
            }

            var result = await store.PruneAsync(
                maxAge: maxAge,
                maxCount: maxCount,
                apply: apply && !dryRun,
                includeFixBaselines: includeFixes);

            var verb = result.Applied ? "Pruned" : "Would prune";
            await output.WriteLineAsync(
                $"{verb} {result.RemovedCount} entry(ies) ({FormatBytes(result.RemovedBytes)}); kept {result.KeptCount}.");
            foreach (var removed in result.Removed)
            {
                await output.WriteLineAsync(
                    $"  - {removed.Id}  {removed.CapturedAt}  source={removed.Source}");
            }

            if (!result.Applied && result.RemovedCount > 0)
            {
                await output.WriteLineAsync("Re-run with --apply to delete these entries.");
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await output.WriteLineAsync($"Error: failed to prune history: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static HistoryStore ResolveStore(string? overrideDir)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return new HistoryStore(overrideDir);
        }

        return HistoryStore.ForProject(Directory.GetCurrentDirectory());
    }

    private static void ParseRetention(string keep, out TimeSpan? maxAge, out int? maxCount)
    {
        maxAge = null;
        maxCount = null;
        var trimmed = keep.Trim();
        if (trimmed.StartsWith("count:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed.Substring("count:".Length);
            if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
            {
                throw new FormatException(
                    $"Invalid retention '{keep}': count must be a non-negative integer.");
            }

            maxCount = n;
            return;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare) && bare >= 0)
        {
            // Bare integer treated as a count (backward-compatible with the most
            // intuitive shorthand: "--keep 30" === "keep 30 entries").
            maxCount = bare;
            return;
        }

        if (trimmed.Length < 2)
        {
            throw new FormatException(
                $"Invalid retention '{keep}': use a duration like 30d, 24h, 30m, or count:N.");
        }

        var suffix = char.ToLowerInvariant(trimmed[trimmed.Length - 1]);
        var head = trimmed.Substring(0, trimmed.Length - 1);
        if (!long.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            throw new FormatException(
                $"Invalid retention '{keep}': numeric portion must be a non-negative integer.");
        }

        try
        {
            maxAge = suffix switch
            {
                'd' => TimeSpan.FromDays(amount),
                'h' => TimeSpan.FromHours(amount),
                'm' => TimeSpan.FromMinutes(amount),
                's' => TimeSpan.FromSeconds(amount),
                _ => throw new FormatException(
                    $"Invalid retention '{keep}': unknown suffix '{suffix}' (use d/h/m/s or count:N)."),
            };
        }
        catch (OverflowException)
        {
            throw new FormatException($"Invalid retention '{keep}': value too large.");
        }
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        string[] suffixes = { "KB", "MB", "GB", "TB" };
        var i = -1;
        do
        {
            value /= 1024;
            i++;
        }
        while (value >= 1024 && i < suffixes.Length - 1);

        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + suffixes[i];
    }
}
