using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Fix;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Profile;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class PlanCommand
{
    private const int DefaultMaxChanges = 50;

    public static Command Create(RevitClient client)
    {
        var command = new Command("plan", "Review and apply saved mutation plans");
        command.AddCommand(CreateShowCommand());
        command.AddCommand(CreateApplyCommand(client));
        return command;
    }

    private static Command CreateShowCommand()
    {
        var fileArg = new Argument<string>("file", "Plan JSON file");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var command = new Command("show", "Show a saved mutation plan") { fileArg, outputOpt };
        command.SetHandler(async (file, output) =>
        {
            Environment.ExitCode = await ExecuteShowAsync(file, output, Console.Out);
        }, fileArg, outputOpt);
        return command;
    }

    private static Command CreateApplyCommand(RevitClient client)
    {
        var fileArg = new Argument<string>("file", "Plan JSON file");
        var yesOpt = new Option<bool>("--yes", "Confirm plan apply in non-interactive mode");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview the saved plan without writing");
        var maxChangesOpt = new Option<int?>("--max-changes", "Maximum number of element writes");
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml safety defaults");
        var highImpactThresholdOpt = new Option<int?>(
            "--high-impact-threshold",
            "Require --confirm-high-impact at or above this many writes");
        var confirmHighImpactOpt = new Option<bool>(
            "--confirm-high-impact",
            "Confirm a high-impact plan apply after reviewing the dry-run");
        var allowInferredOpt = new Option<bool>("--allow-inferred", "Allow inferred fix actions in saved fix plans");
        var command = new Command("apply", "Apply a saved mutation plan")
        {
            fileArg, yesOpt, dryRunOpt, maxChangesOpt, profileOpt,
            highImpactThresholdOpt, confirmHighImpactOpt, allowInferredOpt
        };
        command.SetHandler(async (file, yes, dryRun, maxChanges, profile, highImpactThreshold, confirmHighImpact, allowInferred) =>
        {
            var safety = ResolveApplySafety(profile, maxChanges, highImpactThreshold);
            if (!safety.Success)
            {
                await Console.Out.WriteLineAsync($"Error: {safety.Error}");
                Environment.ExitCode = 1;
                return;
            }

            Environment.ExitCode = await ExecuteApplyAsync(
                client,
                file,
                yes,
                dryRun,
                safety.MaxChanges,
                Console.Out,
                allowInferred,
                safety.HighImpactThreshold,
                confirmHighImpact);
        }, fileArg, yesOpt, dryRunOpt, maxChangesOpt, profileOpt, highImpactThresholdOpt, confirmHighImpactOpt, allowInferredOpt);
        return command;
    }

    public static async Task<int> ExecuteShowAsync(string file, string outputFormat, TextWriter output)
    {
        string type;
        try
        {
            type = SetPlanFileStore.ReadType(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (type.Equals("import", StringComparison.OrdinalIgnoreCase))
            return await ExecuteShowImportAsync(file, outputFormat, output);

        if (type.Equals("fix", StringComparison.OrdinalIgnoreCase))
            return await ExecuteShowFixAsync(file, outputFormat, output);

        SetPlanFile plan;
        try
        {
            plan = SetPlanFileStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                CreateSetSummary(file, plan),
                SetPlanFileStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderMarkdownSummary(CreateSetSummary(file, plan), plan.Preview));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: {plan.Type} {plan.Summary.Affected} element(s), param=\"{plan.Summary.Param}\", value=\"{plan.Summary.Value}\"");
        await output.WriteLineAsync(RenderRisk(CreateSetIssues(plan), CreateSetRisk(plan)));
        await output.WriteLineAsync($"Original target: {plan.Summary.OriginalTarget}");
        await output.WriteLineAsync($"Apply target: {plan.Summary.ApplyTarget} ({plan.Summary.FrozenElementIds.Count} frozen id(s))");

        foreach (var item in plan.Preview.Take(20))
            await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");

        if (plan.Preview.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Preview.Count - 20} more.");

        await output.WriteLineAsync($"Dry-run apply: {plan.Commands.DryRunApply}");
        await output.WriteLineAsync($"Apply: {plan.Commands.Apply}");
        return 0;
    }

    public static async Task<int> ExecuteApplyAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        TextWriter output,
        bool allowInferred = false,
        int? highImpactThreshold = null,
        bool confirmHighImpact = false)
    {
        string type;
        try
        {
            type = SetPlanFileStore.ReadType(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (type.Equals("import", StringComparison.OrdinalIgnoreCase))
            return await ExecuteApplyImportAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

        if (type.Equals("fix", StringComparison.OrdinalIgnoreCase))
            return await ExecuteApplyFixAsync(
                client, file, yes, dryRun, maxChanges, allowInferred, highImpactThreshold, confirmHighImpact, output);

        SetPlanFile plan;
        try
        {
            plan = SetPlanFileStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var frozenIds = plan.Summary.FrozenElementIds ?? new();
        if (frozenIds.Count == 0)
        {
            await output.WriteLineAsync("Plan has no frozen element IDs. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (frozenIds.Count > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {frozenIds.Count} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(frozenIds.Count, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {frozenIds.Count} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        plan.ApplyRequest.DryRun = dryRun;
        plan.ApplyRequest.Category = null;
        plan.ApplyRequest.Filter = null;
        plan.ApplyRequest.ElementId = null;
        plan.ApplyRequest.ElementIds = frozenIds;

        var result = await client.SetParameterAsync(plan.ApplyRequest);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var data = result.Data!;
        if (dryRun)
        {
            await output.WriteLineAsync($"Dry run: {data.Affected} element(s) would be modified from plan.");
            foreach (var item in data.Preview)
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            return 0;
        }

        await output.WriteLineAsync($"Applied plan: modified {data.Affected} element(s).");
        var metadata = await TryGetReceiptMetadataAsync(client);
        var receipt = CreatePlanReceipt(
            file,
            "set",
            data.Affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Param = plan.Summary.Param;
        receipt.Value = plan.Summary.Value;
        receipt.Preview = data.Preview;
        receipt.RollbackActions = CreateRollbackActions(plan.Summary.Param, data.Preview, "set");
        receipt.AffectedElementIds = receipt.RollbackActions.Count > 0
            ? DistinctSorted(receipt.RollbackActions.Select(action => action.ElementId))
            : DistinctSorted(frozenIds);
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return 0;
    }

    private static async Task<int> ExecuteShowImportAsync(string file, string outputFormat, TextWriter output)
    {
        ImportPlanFile plan;
        try
        {
            plan = SetPlanFileStore.LoadImport(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                CreateImportSummary(file, plan),
                SetPlanFileStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderMarkdownSummary(CreateImportSummary(file, plan), plan.Groups));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: import {plan.Summary.GroupCount} group(s), {plan.Summary.ElementWrites} element-write(s), category=\"{plan.Summary.Category}\"");
        await output.WriteLineAsync(RenderRisk(CreateImportIssues(plan), CreateImportRisk(plan)));
        await output.WriteLineAsync(
            $"CSV: {plan.Summary.SourceCsv} ({plan.Summary.CsvRows} row(s), encoding={plan.Summary.Encoding}, matchBy={plan.Summary.MatchBy})");

        foreach (var group in plan.Groups.Take(20))
            await output.WriteLineAsync(
                $"  [{group.Param}] = \"{group.Value}\" on {group.ElementIds.Count} element(s): {string.Join(",", group.ElementIds.Take(20))}{(group.ElementIds.Count > 20 ? ",..." : "")}");

        if (plan.Groups.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Groups.Count - 20} more group(s).");

        if (plan.Misses.Count > 0)
            await output.WriteLineAsync($"Misses: {plan.Misses.Count}");
        if (plan.Duplicates.Count > 0)
            await output.WriteLineAsync($"Duplicates: {plan.Duplicates.Count}");
        if (plan.Skipped.Count > 0)
            await output.WriteLineAsync($"Skipped cells: {plan.Skipped.Count}");
        foreach (var warning in plan.Warnings.Take(10))
            await output.WriteLineAsync($"Warning: {warning}");

        await output.WriteLineAsync($"Dry-run apply: {plan.Commands.DryRunApply}");
        await output.WriteLineAsync($"Apply: {plan.Commands.Apply}");
        return 0;
    }

    private static async Task<int> ExecuteShowFixAsync(string file, string outputFormat, TextWriter output)
    {
        FixPlanFile plan;
        try
        {
            plan = SetPlanFileStore.LoadFix(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                CreateFixSummary(file, plan),
                SetPlanFileStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderMarkdownSummary(CreateFixSummary(file, plan), plan.Actions));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: fix {plan.Summary.ActionCount} action(s), {plan.Summary.SkippedCount} skipped, check=\"{plan.Summary.CheckName}\"");
        await output.WriteLineAsync(RenderRisk(CreateFixIssues(plan), CreateFixRisk(plan)));
        if (plan.Summary.InferredCount > 0)
            await output.WriteLineAsync($"Inferred actions: {plan.Summary.InferredCount} (apply requires --allow-inferred)");
        if (!string.IsNullOrWhiteSpace(plan.Summary.ProfilePath))
            await output.WriteLineAsync($"Profile: {plan.Summary.ProfilePath}");

        foreach (var action in plan.Actions.Take(20))
        {
            var oldValue = action.OldValue ?? string.Empty;
            await output.WriteLineAsync(
                $"  [{action.Strategy}] {action.Rule} Element {action.ElementId} {action.Parameter}: \"{oldValue}\" -> \"{action.NewValue}\" ({action.Confidence})");
        }

        if (plan.Actions.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Actions.Count - 20} more action(s).");

        if (plan.Skipped.Count > 0)
            await output.WriteLineAsync($"Skipped: {plan.Skipped.Count}");
        foreach (var warning in plan.Warnings.Take(10))
            await output.WriteLineAsync($"Warning: {warning}");

        await output.WriteLineAsync($"Dry-run apply: {plan.Commands.DryRunApply}");
        await output.WriteLineAsync($"Apply: {plan.Commands.Apply}");
        return 0;
    }

    private static async Task<int> ExecuteApplyImportAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        ImportPlanFile plan;
        try
        {
            plan = SetPlanFileStore.LoadImport(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var elementWrites = plan.Groups.Sum(group => group.ElementIds.Count);
        if (elementWrites == 0)
        {
            await output.WriteLineAsync("Plan has no element writes. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (elementWrites > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {elementWrites} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(elementWrites, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {elementWrites} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        var (affected, previews, failures, rollbackActions) = await ApplyImportGroupsAsync(
            client,
            plan,
            dryRun,
            Math.Max(plan.Summary.BatchSize, 1));

        if (dryRun)
        {
            if (failures.Count > 0)
            {
                await output.WriteLineAsync($"Error: dry-run failed for {failures.Count} group(s):");
                foreach (var failure in failures)
                    await output.WriteLineAsync(
                        $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {affected} element-parameter pair(s) would be modified from import plan.");
            foreach (var item in previews.Take(20))
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            if (previews.Count > 20)
                await output.WriteLineAsync($"  ... and {previews.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync(
            $"Applied import plan: modified {affected} element-parameter pair(s) across {plan.Groups.Count} group(s).");
        if (failures.Count > 0)
        {
            await output.WriteLineAsync($"Failed: {failures.Count} group(s):");
            foreach (var failure in failures)
                await output.WriteLineAsync(
                    $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
        }

        var metadata = await TryGetReceiptMetadataAsync(client);
        var receipt = CreatePlanReceipt(
            file,
            "import",
            affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = failures.Count == 0;
        receipt.GroupCount = plan.Groups.Count;
        receipt.ElementWrites = elementWrites;
        receipt.Groups = plan.Groups;
        receipt.Failures = failures;
        receipt.RollbackActions = rollbackActions;
        receipt.AffectedElementIds = receipt.RollbackActions.Count > 0
            ? DistinctSorted(receipt.RollbackActions.Select(action => action.ElementId))
            : DistinctSorted(plan.Groups.SelectMany(group => group.ElementIds));
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return failures.Count == 0 ? 0 : 2;
    }

    private static async Task<int> ExecuteApplyFixAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        bool allowInferred,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        FixPlanFile plan;
        try
        {
            plan = SetPlanFileStore.LoadFix(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var actions = plan.Actions.Where(action => action is not null).ToList();
        if (actions.Count == 0)
        {
            await output.WriteLineAsync("Plan has no fix actions. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (actions.Count > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actions.Count} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(actions.Count, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actions.Count} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (dryRun)
            return await DryRunFixActionsAsync(client, actions, output);

        if (actions.Any(action => action.Inferred) && !allowInferred)
        {
            await output.WriteLineAsync("Error: inferred fix actions require --allow-inferred.");
            return 1;
        }

        if (actions.Any(action => string.Equals(action.Confidence, "low", StringComparison.OrdinalIgnoreCase)))
        {
            await output.WriteLineAsync("Error: low-confidence fallback actions are dry-run only.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        var baselinePath = BuildFixBaselinePath(file);
        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest());
        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: failed to capture baseline: {snapshotResult.Error}");
            return 1;
        }

        try
        {
            var baselineDir = Path.GetDirectoryName(Path.GetFullPath(baselinePath));
            if (!string.IsNullOrWhiteSpace(baselineDir))
                Directory.CreateDirectory(baselineDir);

            await File.WriteAllTextAsync(
                baselinePath,
                JsonSerializer.Serialize(snapshotResult.Data, new JsonSerializerOptions { WriteIndented = true }),
                default);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to save baseline snapshot: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Baseline saved: {baselinePath}");

        var journal = new FixJournal
        {
            CheckName = plan.Summary.CheckName,
            ProfilePath = plan.Summary.ProfilePath,
            BaselinePath = baselinePath,
            StartedAt = DateTime.UtcNow.ToString("o"),
            User = Environment.UserName,
            Actions = actions
        };

        string journalPath;
        try
        {
            journalPath = FixJournalStore.SaveForBaseline(baselinePath, journal);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to save fix journal: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Journal saved: {journalPath}");

        var modified = 0;
        foreach (var action in actions)
        {
            var result = await client.SetParameterAsync(new SetRequest
            {
                ElementId = action.ElementId,
                Param = action.Parameter,
                Value = action.NewValue,
                DryRun = false
            });

            if (!result.Success || result.Data == null)
            {
                await output.WriteLineAsync($"Error: failed to apply fix for element {action.ElementId}: {result.Error}");
                await output.WriteLineAsync($"Rollback: revitcli rollback {baselinePath} --yes");
                return 1;
            }

            modified += result.Data.Affected;
        }

        journal.CompletedAt = DateTime.UtcNow.ToString("o");
        try
        {
            await File.WriteAllTextAsync(
                journalPath,
                JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }),
                default);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to write fix journal: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Applied fix plan: modified {modified} element parameter(s).");
        await output.WriteLineAsync($"Rollback: revitcli rollback {baselinePath} --yes");
        var metadata = CreateReceiptMetadata(snapshotResult.Data);
        var receipt = CreatePlanReceipt(
            file,
            "fix",
            modified,
            maxChanges,
            allowInferred,
            highImpactThreshold,
            confirmHighImpact,
            metadata);
        receipt.ActionCount = actions.Count;
        receipt.RequiresRollback = true;
        receipt.BaselinePath = Path.GetFullPath(baselinePath);
        receipt.JournalPath = journalPath;
        receipt.AffectedElementIds = DistinctSorted(actions.Select(action => action.ElementId));
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return 0;
    }

    private static async Task<int> DryRunFixActionsAsync(
        RevitClient client,
        IReadOnlyList<FixAction> actions,
        TextWriter output)
    {
        var affected = 0;
        var previews = new List<SetPreviewItem>();
        foreach (var action in actions)
        {
            var result = await client.SetParameterAsync(new SetRequest
            {
                ElementId = action.ElementId,
                Param = action.Parameter,
                Value = action.NewValue,
                DryRun = true
            });

            if (!result.Success || result.Data == null)
            {
                await output.WriteLineAsync($"Error: failed to dry-run fix for element {action.ElementId}: {result.Error}");
                return 1;
            }

            affected += result.Data.Affected;
            previews.AddRange(result.Data.Preview ?? new List<SetPreviewItem>());
        }

        await output.WriteLineAsync($"Dry run: {affected} element parameter(s) would be modified from fix plan.");
        foreach (var item in previews.Take(20))
            await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
        if (previews.Count > 20)
            await output.WriteLineAsync($"  ... and {previews.Count - 20} more.");
        return 0;
    }

    private static string BuildFixBaselinePath(string planPath)
    {
        var planDir = Path.GetDirectoryName(Path.GetFullPath(planPath));
        if (string.IsNullOrWhiteSpace(planDir))
            planDir = Directory.GetCurrentDirectory();

        return Path.Combine(planDir, $"fix-baseline-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.json");
    }

    internal static PlanApplySafetyOptions ResolveApplySafety(
        string? profilePath,
        int? maxChanges,
        int? highImpactThreshold)
    {
        ProjectProfile? profile = null;
        var resolvedProfilePath = string.IsNullOrWhiteSpace(profilePath)
            ? ProfileLoader.Discover()
            : profilePath;

        if (!string.IsNullOrWhiteSpace(resolvedProfilePath))
        {
            try
            {
                profile = ProfileLoader.Load(resolvedProfilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return PlanApplySafetyOptions.Fail($"failed to load profile safety defaults: {ex.Message}");
            }
        }

        var resolvedMaxChanges = maxChanges ?? profile?.Defaults.PlanMaxChanges ?? DefaultMaxChanges;
        if (resolvedMaxChanges <= 0)
            return PlanApplySafetyOptions.Fail("--max-changes must be at least 1.");

        var resolvedHighImpactThreshold = highImpactThreshold ?? profile?.Defaults.HighImpactChanges;
        if (resolvedHighImpactThreshold.HasValue && resolvedHighImpactThreshold.Value <= 0)
            return PlanApplySafetyOptions.Fail("--high-impact-threshold must be at least 1.");

        return PlanApplySafetyOptions.Ok(resolvedMaxChanges, resolvedHighImpactThreshold);
    }

    private static bool IsHighImpact(int changeCount, int? threshold)
    {
        return threshold.HasValue
            && threshold.Value > 0
            && changeCount >= threshold.Value;
    }

    private static PlanReceipt CreatePlanReceipt(
        string file,
        string operation,
        int affected,
        int maxChanges,
        bool allowInferred = false,
        int? highImpactThreshold = null,
        bool confirmHighImpact = false,
        PlanReceiptMetadata? metadata = null)
    {
        var timestamp = DateTime.UtcNow.ToString("o");
        var user = Environment.UserName;
        return new PlanReceipt
        {
            PlanPath = Path.GetFullPath(file),
            Command = BuildPlanApplyCommand(file, maxChanges, allowInferred, highImpactThreshold, confirmHighImpact),
            DryRun = false,
            Timestamp = timestamp,
            AppliedAtUtc = timestamp,
            Operator = user,
            User = user,
            AppliedBy = user,
            Machine = Environment.MachineName,
            ModelPath = metadata?.ModelPath,
            DocumentName = metadata?.DocumentName,
            DocumentVersion = metadata?.DocumentVersion,
            Affected = affected,
            Operation = operation
        };
    }

    private static string BuildPlanApplyCommand(
        string file,
        int maxChanges,
        bool allowInferred,
        int? highImpactThreshold,
        bool confirmHighImpact)
    {
        var parts = new List<string>
        {
            "revitcli",
            "plan",
            "apply",
            QuoteArgument(Path.GetFullPath(file)),
            "--yes",
            "--max-changes",
            maxChanges.ToString(CultureInfo.InvariantCulture)
        };
        if (allowInferred)
            parts.Add("--allow-inferred");
        if (highImpactThreshold.HasValue)
        {
            parts.Add("--high-impact-threshold");
            parts.Add(highImpactThreshold.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (confirmHighImpact)
            parts.Add("--confirm-high-impact");
        return string.Join(" ", parts);
    }

    private static async Task<PlanReceiptMetadata?> TryGetReceiptMetadataAsync(RevitClient client)
    {
        try
        {
            var status = await client.GetStatusAsync();
            return status.Success ? CreateReceiptMetadata(status.Data) : null;
        }
        catch
        {
            return null;
        }
    }

    private static PlanReceiptMetadata? CreateReceiptMetadata(StatusInfo? status)
    {
        if (status == null)
            return null;

        return new PlanReceiptMetadata(
            status.DocumentPath,
            status.DocumentName,
            status.RevitVersion);
    }

    private static PlanReceiptMetadata? CreateReceiptMetadata(ModelSnapshot? snapshot)
    {
        if (snapshot?.Revit == null)
            return null;

        return new PlanReceiptMetadata(
            snapshot.Revit.DocumentPath,
            snapshot.Revit.Document,
            snapshot.Revit.Version);
    }

    private static List<long> DistinctSorted(IEnumerable<long> ids)
    {
        return ids
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    private static List<PlanReceiptRollbackAction> CreateRollbackActions(
        string param,
        IEnumerable<SetPreviewItem>? previews,
        string source)
    {
        if (string.IsNullOrWhiteSpace(param) || previews == null)
            return new List<PlanReceiptRollbackAction>();

        return previews
            .Where(item => item != null && item.Id > 0)
            .Select(item => new PlanReceiptRollbackAction
            {
                ElementId = item.Id,
                Param = param,
                OldValue = item.OldValue,
                NewValue = item.NewValue,
                Source = source
            })
            .ToList();
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record PlanReceiptMetadata(
        string? ModelPath,
        string? DocumentName,
        string? DocumentVersion);

    internal sealed record PlanApplySafetyOptions(
        bool Success,
        int MaxChanges,
        int? HighImpactThreshold,
        string Error)
    {
        public static PlanApplySafetyOptions Ok(int maxChanges, int? highImpactThreshold) =>
            new(true, maxChanges, highImpactThreshold, string.Empty);

        public static PlanApplySafetyOptions Fail(string error) =>
            new(false, DefaultMaxChanges, null, error);
    }

    private static PlanShowOutput CreateSetSummary(string file, SetPlanFile plan)
    {
        var issues = CreateSetIssues(plan);
        return new PlanShowOutput(
            "plan-summary.v1",
            true,
            IsValid(issues),
            "set",
            Path.GetFullPath(file),
            plan.Summary,
            CreateSetRisk(plan),
            plan.Commands,
            issues,
            plan);
    }

    private static PlanShowOutput CreateImportSummary(string file, ImportPlanFile plan)
    {
        var issues = CreateImportIssues(plan);
        return new PlanShowOutput(
            "plan-summary.v1",
            true,
            IsValid(issues),
            "import",
            Path.GetFullPath(file),
            plan.Summary,
            CreateImportRisk(plan),
            plan.Commands,
            issues,
            plan);
    }

    private static PlanShowOutput CreateFixSummary(string file, FixPlanFile plan)
    {
        var issues = CreateFixIssues(plan);
        return new PlanShowOutput(
            "plan-summary.v1",
            true,
            IsValid(issues),
            "fix",
            Path.GetFullPath(file),
            plan.Summary,
            CreateFixRisk(plan),
            plan.Commands,
            issues,
            plan);
    }

    private static IReadOnlyList<PlanSummaryIssue> CreateSetIssues(SetPlanFile plan)
    {
        var issues = new List<PlanSummaryIssue>();
        var frozenIds = plan.Summary.FrozenElementIds ?? new();
        if (frozenIds.Count == 0)
            issues.Add(new PlanSummaryIssue("info", "set-no-targets", "Plan has no frozen element IDs."));

        if (plan.Preview.Count != plan.Summary.Affected)
        {
            issues.Add(new PlanSummaryIssue(
                "warn",
                "set-preview-count-mismatch",
                $"Plan summary reports {plan.Summary.Affected} affected element(s), but preview contains {plan.Preview.Count} row(s)."));
        }

        return issues;
    }

    private static IReadOnlyList<PlanSummaryIssue> CreateImportIssues(ImportPlanFile plan)
    {
        var issues = new List<PlanSummaryIssue>();
        if (plan.Summary.ElementWrites == 0)
            issues.Add(new PlanSummaryIssue("info", "import-no-writes", "Plan has no element writes."));
        if (plan.Misses.Count > 0)
            issues.Add(new PlanSummaryIssue("warn", "import-misses", $"Plan has {plan.Misses.Count} unmatched CSV row(s)."));
        if (plan.Duplicates.Count > 0)
            issues.Add(new PlanSummaryIssue("warn", "import-duplicates", $"Plan has {plan.Duplicates.Count} duplicate match group(s)."));
        if (plan.Skipped.Count > 0)
            issues.Add(new PlanSummaryIssue("warn", "import-skipped-cells", $"Plan has {plan.Skipped.Count} skipped CSV cell(s)."));
        issues.AddRange(plan.Warnings.Select(warning => new PlanSummaryIssue("warn", "import-warning", warning)));
        return issues;
    }

    private static IReadOnlyList<PlanSummaryIssue> CreateFixIssues(FixPlanFile plan)
    {
        var issues = new List<PlanSummaryIssue>();
        if (plan.Actions.Count == 0)
            issues.Add(new PlanSummaryIssue("info", "fix-no-actions", "Plan has no fix actions."));
        if (plan.Summary.InferredCount > 0)
        {
            issues.Add(new PlanSummaryIssue(
                "warn",
                "fix-inferred-actions",
                $"Plan has {plan.Summary.InferredCount} inferred action(s); real apply requires --allow-inferred."));
        }

        var lowConfidence = plan.Actions.Count(action =>
            string.Equals(action.Confidence, "low", StringComparison.OrdinalIgnoreCase));
        if (lowConfidence > 0)
        {
            issues.Add(new PlanSummaryIssue(
                "error",
                "fix-low-confidence-actions",
                $"Plan has {lowConfidence} low-confidence action(s), which cannot be applied."));
        }

        if (plan.Skipped.Count > 0)
            issues.Add(new PlanSummaryIssue("info", "fix-skipped-issues", $"Plan skipped {plan.Skipped.Count} issue(s)."));
        issues.AddRange(plan.Warnings.Select(warning => new PlanSummaryIssue("warn", "fix-warning", warning)));
        return issues;
    }

    private static PlanRisk CreateSetRisk(SetPlanFile plan)
    {
        var changeCount = plan.Summary.FrozenElementIds?.Count ?? plan.Summary.Affected;
        return CreateRisk(changeCount, requiresAllowInferred: false, writesBaseline: false, CreateSetIssues(plan));
    }

    private static PlanRisk CreateImportRisk(ImportPlanFile plan)
    {
        return CreateRisk(plan.Summary.ElementWrites, requiresAllowInferred: false, writesBaseline: false, CreateImportIssues(plan));
    }

    private static PlanRisk CreateFixRisk(FixPlanFile plan)
    {
        return CreateRisk(
            plan.Actions.Count,
            requiresAllowInferred: plan.Summary.InferredCount > 0,
            writesBaseline: true,
            CreateFixIssues(plan));
    }

    private static PlanRisk CreateRisk(
        int changeCount,
        bool requiresAllowInferred,
        bool writesBaseline,
        IReadOnlyList<PlanSummaryIssue> issues)
    {
        var hasError = issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var level = hasError
            ? "blocked"
            : changeCount switch
            {
                <= 0 => "none",
                <= 10 => "low",
                <= 50 => "medium",
                _ => "high"
            };
        if (requiresAllowInferred && level == "low")
            level = "medium";

        var notes = new List<string> { "Real apply requires --yes." };
        if (changeCount > 0)
            notes.Add("Dry-run apply is available before real apply.");
        if (requiresAllowInferred)
            notes.Add("Real apply requires --allow-inferred.");
        if (writesBaseline)
            notes.Add("Real apply writes a rollback baseline and fix journal.");
        if (hasError)
            notes.Add("Plan has blocking issues and cannot be applied as-is.");

        return new PlanRisk(level, changeCount, true, true, requiresAllowInferred, writesBaseline, notes);
    }

    private static bool IsValid(IReadOnlyList<PlanSummaryIssue> issues)
    {
        return issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
    }

    private static string RenderRisk(IReadOnlyList<PlanSummaryIssue> issues, PlanRisk risk)
    {
        var suffix = risk.RequiresAllowInferred
            ? ", requires --allow-inferred"
            : "";
        if (!IsValid(issues))
            suffix += ", blocked";
        return $"Risk: {risk.Level} ({risk.ChangeCount} change(s), requires --yes{suffix})";
    }

    private static string RenderMarkdownSummary(PlanShowOutput summary, object preview)
    {
        var lines = new List<string>
        {
            "# RevitCli Plan Review",
            "",
            $"- Schema: {InlineCode(summary.SchemaVersion)}",
            $"- Type: {InlineCode(summary.Type)}",
            $"- Valid: {(summary.Valid ? "yes" : "no")}",
            $"- Plan: {InlineCode(summary.PlanPath)}",
            "",
            "## Summary"
        };

        AppendMarkdownSummary(lines, summary.Summary);

        lines.Add("");
        lines.Add("## Risk");
        lines.Add($"- Level: {InlineCode(summary.Risk.Level)}");
        lines.Add($"- Change count: {summary.Risk.ChangeCount}");
        lines.Add($"- Requires dry-run review: {(summary.Risk.RequiresDryRunReview ? "yes" : "no")}");
        lines.Add($"- Requires `--yes`: {(summary.Risk.RequiresYes ? "yes" : "no")}");
        if (summary.Risk.RequiresAllowInferred)
            lines.Add("- Requires `--allow-inferred`: yes");
        if (summary.Risk.WritesBaseline)
            lines.Add("- Writes rollback baseline: yes");
        foreach (var note in summary.Risk.Notes)
            lines.Add($"- {EscapeMarkdownText(note)}");

        lines.Add("");
        lines.Add("## Issues");
        if (summary.Issues.Count == 0)
        {
            lines.Add("- None.");
        }
        else
        {
            foreach (var issue in summary.Issues)
                lines.Add($"- {InlineCode(issue.Severity)} {InlineCode(issue.Code)}: {EscapeMarkdownText(issue.Message)}");
        }

        lines.Add("");
        lines.Add("## Preview");
        AppendMarkdownPreview(lines, preview);

        lines.Add("");
        lines.Add("## Commands");
        lines.Add($"- Dry-run apply: {InlineCode(summary.Commands.DryRunApply)}");
        lines.Add($"- Apply: {InlineCode(summary.Commands.Apply)}");
        lines.Add("");
        lines.Add("> Review the dry-run output before approving a real apply.");

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendMarkdownSummary(List<string> lines, object summary)
    {
        switch (summary)
        {
            case SetPlanSummary set:
                lines.Add($"- Operation: {InlineCode(set.Operation)}");
                lines.Add($"- Parameter: {InlineCode(set.Param)}");
                lines.Add($"- New value: {InlineCode(set.Value)}");
                lines.Add($"- Affected elements: {set.Affected}");
                lines.Add($"- Original target: {InlineCode(set.OriginalTarget)}");
                lines.Add($"- Apply target: {InlineCode(set.ApplyTarget)}");
                lines.Add($"- Frozen element IDs: {set.FrozenElementIds.Count}");
                break;
            case ImportPlanSummary import:
                lines.Add($"- Operation: {InlineCode(import.Operation)}");
                lines.Add($"- Category: {InlineCode(import.Category)}");
                lines.Add($"- Source CSV: {InlineCode(import.SourceCsv)}");
                lines.Add($"- Match by: {InlineCode(import.MatchBy)}");
                lines.Add($"- CSV rows: {import.CsvRows}");
                lines.Add($"- Groups: {import.GroupCount}");
                lines.Add($"- Element writes: {import.ElementWrites}");
                lines.Add($"- Missing rows policy: {InlineCode(import.OnMissing)}");
                lines.Add($"- Duplicate rows policy: {InlineCode(import.OnDuplicate)}");
                break;
            case FixPlanSummary fix:
                lines.Add($"- Operation: {InlineCode(fix.Operation)}");
                lines.Add($"- Check: {InlineCode(fix.CheckName)}");
                lines.Add($"- Actions: {fix.ActionCount}");
                lines.Add($"- Skipped issues: {fix.SkippedCount}");
                lines.Add($"- Inferred actions: {fix.InferredCount}");
                if (!string.IsNullOrWhiteSpace(fix.ProfilePath))
                    lines.Add($"- Profile: {InlineCode(fix.ProfilePath)}");
                if (fix.Rules.Count > 0)
                    lines.Add($"- Rules: {InlineCode(string.Join(", ", fix.Rules))}");
                if (!string.IsNullOrWhiteSpace(fix.Severity))
                    lines.Add($"- Severity: {InlineCode(fix.Severity)}");
                break;
            default:
                lines.Add("- Summary type is unknown; use `--output json` for the full envelope.");
                break;
        }
    }

    private static void AppendMarkdownPreview(List<string> lines, object preview)
    {
        switch (preview)
        {
            case IReadOnlyList<SetPreviewItem> items when items.Count > 0:
                foreach (var item in items.Take(10))
                {
                    lines.Add(
                        $"- Element {InlineCode(item.Id.ToString(CultureInfo.InvariantCulture))} {EscapeMarkdownText(item.Name)}: {InlineCode(item.OldValue ?? "")} -> {InlineCode(item.NewValue)}");
                }
                if (items.Count > 10)
                    lines.Add($"- ... and {items.Count - 10} more preview row(s).");
                break;
            case IReadOnlyList<ImportGroup> groups when groups.Count > 0:
                foreach (var group in groups.Take(10))
                {
                    var ids = string.Join(",", group.ElementIds.Take(10));
                    var suffix = group.ElementIds.Count > 10 ? ",..." : "";
                    lines.Add(
                        $"- {InlineCode(group.Param)} = {InlineCode(group.Value)} on {group.ElementIds.Count} element(s): {InlineCode(ids + suffix)}");
                }
                if (groups.Count > 10)
                    lines.Add($"- ... and {groups.Count - 10} more group(s).");
                break;
            case IReadOnlyList<FixAction> actions when actions.Count > 0:
                foreach (var action in actions.Take(10))
                {
                    lines.Add(
                        $"- {InlineCode(action.Strategy)} {InlineCode(action.Rule)} element {InlineCode(action.ElementId.ToString(CultureInfo.InvariantCulture))} {InlineCode(action.Parameter)}: {InlineCode(action.OldValue ?? "")} -> {InlineCode(action.NewValue)} ({InlineCode(action.Confidence)})");
                }
                if (actions.Count > 10)
                    lines.Add($"- ... and {actions.Count - 10} more action(s).");
                break;
            default:
                lines.Add("- No preview rows.");
                break;
        }
    }

    private static string InlineCode(string? value)
    {
        var normalized = string.IsNullOrEmpty(value)
            ? "-"
            : value.Replace("`", "'", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
        return $"`{normalized}`";
    }

    private static string EscapeMarkdownText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static async Task<(
        int Affected,
        List<SetPreviewItem> Previews,
        List<PlanApplyFailure> Failures,
        List<PlanReceiptRollbackAction> RollbackActions)> ApplyImportGroupsAsync(
        RevitClient client,
        ImportPlanFile plan,
        bool dryRun,
        int batchSize)
    {
        var failures = new List<PlanApplyFailure>();
        var previews = new List<SetPreviewItem>();
        var rollbackActions = new List<PlanReceiptRollbackAction>();
        var affected = 0;

        foreach (var group in plan.Groups)
        {
            for (var off = 0; off < group.ElementIds.Count; off += batchSize)
            {
                var slice = group.ElementIds.GetRange(off, Math.Min(batchSize, group.ElementIds.Count - off));
                var resp = await client.SetParameterAsync(new SetRequest
                {
                    ElementIds = slice,
                    Param = group.Param,
                    Value = group.Value,
                    DryRun = dryRun
                });

                if (!resp.Success)
                {
                    failures.Add(new PlanApplyFailure
                    {
                        Param = group.Param,
                        Value = group.Value,
                        ElementIds = slice,
                        Message = resp.Error ?? "unknown"
                    });
                    break;
                }

                var responsePreviews = resp.Data?.Preview ?? new List<SetPreviewItem>();
                affected += resp.Data?.Affected ?? 0;
                previews.AddRange(responsePreviews);
                if (!dryRun)
                    rollbackActions.AddRange(CreateRollbackActions(group.Param, responsePreviews, "import"));
            }
        }

        return (affected, previews, failures, rollbackActions);
    }

    private sealed record PlanShowOutput(
        string SchemaVersion,
        bool Success,
        bool Valid,
        string Type,
        string PlanPath,
        object Summary,
        PlanRisk Risk,
        SetPlanCommands Commands,
        IReadOnlyList<PlanSummaryIssue> Issues,
        object Plan);

    private sealed record PlanRisk(
        string Level,
        int ChangeCount,
        bool RequiresYes,
        bool RequiresDryRunReview,
        bool RequiresAllowInferred,
        bool WritesBaseline,
        IReadOnlyList<string> Notes);

    private sealed record PlanSummaryIssue(
        string Severity,
        string Code,
        string Message);
}
