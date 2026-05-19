using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Checks;
using RevitCli.Client;
using RevitCli.Fix;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class FixCommand
{
    public static Command Create(RevitClient client)
    {
        var checkNameArg = new Argument<string?>("name", () => null, "Check set name (default: 'default')");
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml profile");
        var rulesOpt = new Option<string[]?>("--rule")
        {
            Description = "Filter by specific rule names (comma-separated or repeated).",
            AllowMultipleArgumentsPerToken = true
        };
        var severityOpt = new Option<string?>("--severity", "Filter by issue severity (error, warning, info)");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview the fix plan without applying");
        var applyOpt = new Option<bool>("--apply", "Apply generated fixes");
        var yesOpt = new Option<bool>("--yes", "Confirm risky operations in non-interactive mode");
        var allowInferredOpt = new Option<bool>("--allow-inferred", "Allow inferred fixes");
        var maxChangesOpt = new Option<int>("--max-changes", () => 50, "Maximum number of fixes to apply");
        var baselineOutputOpt = new Option<string?>("--baseline-output", "Path to save baseline snapshot");
        var noSnapshotOpt = new Option<bool>("--no-snapshot", "Skip baseline snapshot and journal rollback support");
        var planOutputOpt = new Option<string?>("--plan-output", "Write a saved fix plan JSON file without applying");

        var command = new Command("fix", "Plan or apply profile-driven parameter fixes")
        {
            checkNameArg,
            profileOpt,
            rulesOpt,
            severityOpt,
            dryRunOpt,
            applyOpt,
            yesOpt,
            allowInferredOpt,
            maxChangesOpt,
            baselineOutputOpt,
            noSnapshotOpt,
            planOutputOpt
        };

        command.SetHandler(async (context) =>
        {
            var checkName = context.ParseResult.GetValueForArgument(checkNameArg);
            var profilePath = context.ParseResult.GetValueForOption(profileOpt);
            var rules = context.ParseResult.GetValueForOption(rulesOpt);
            var severity = context.ParseResult.GetValueForOption(severityOpt);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOpt);
            var apply = context.ParseResult.GetValueForOption(applyOpt);
            var yes = context.ParseResult.GetValueForOption(yesOpt);
            var allowInferred = context.ParseResult.GetValueForOption(allowInferredOpt);
            var maxChanges = context.ParseResult.GetValueForOption(maxChangesOpt);
            var baselineOutput = context.ParseResult.GetValueForOption(baselineOutputOpt);
            var noSnapshot = context.ParseResult.GetValueForOption(noSnapshotOpt);
            var planOutput = context.ParseResult.GetValueForOption(planOutputOpt);

            Environment.ExitCode = await ExecuteAsync(
                client, checkName, profilePath, rules, severity, dryRun, apply,
                yes, allowInferred, maxChanges, baselineOutput, noSnapshot, Console.Out, planOutput);
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string? checkName,
        string? profilePath,
        IReadOnlyList<string>? rules,
        string? severity,
        bool dryRun,
        bool apply,
        bool yes,
        bool allowInferred,
        int maxChanges,
        string? baselineOutput,
        bool noSnapshot,
        TextWriter output,
        string? planOutputPath = null)
    {
        if (dryRun && apply)
        {
            await output.WriteLineAsync("Error: --dry-run and --apply cannot be combined.");
            return 1;
        }

        if (apply && !string.IsNullOrWhiteSpace(planOutputPath))
        {
            await output.WriteLineAsync("Error: --plan-output cannot be combined with --apply.");
            return 1;
        }

        var run = await CheckRunner.RunAsync(client, checkName, profilePath);
        if (!run.Success)
        {
            await output.WriteLineAsync(run.Error);
            return 1;
        }

        var profile = run.Data!.Profile;
        var normalizedRules = (rules ?? Array.Empty<string>())
            .SelectMany(rule => rule?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>())
            .Select(rule => rule.Trim())
            .Where(rule => !string.IsNullOrWhiteSpace(rule));
        var options = new FixPlanOptions
        {
            Severity = severity,
            Rules = new HashSet<string>(normalizedRules, StringComparer.OrdinalIgnoreCase)
        };
        var plan = FixPlanner.Plan(run.Data.CheckName, run.Data.Issues, profile, options);

        if (!string.IsNullOrWhiteSpace(planOutputPath))
        {
            var planFile = FixPlanFile.Create(
                plan,
                run.Data.ProfilePath,
                options.Rules.ToList(),
                severity,
                planOutputPath);
            SetPlanFileStore.SaveFix(planOutputPath, planFile);
            await output.WriteLineAsync($"Plan written to {Path.GetFullPath(planOutputPath)}");
            await output.WriteLineAsync($"Review: {planFile.Commands.Show}");
            await output.WriteLineAsync($"Dry-run apply: {planFile.Commands.DryRunApply}");
            await output.WriteLineAsync($"Apply: {planFile.Commands.Apply}");
            if (planFile.Summary.InferredCount > 0)
                await output.WriteLineAsync("Apply note: inferred actions require plan apply --allow-inferred.");
            return 0;
        }

        if (!apply)
        {
            await output.WriteLineAsync(FixPlanRenderer.Render(plan));
            return 0;
        }

        var applyActions = (plan.Actions ?? []).Where(a => a is not null).ToList();
        if (applyActions.Count == 0)
        {
            await output.WriteLineAsync("No fixable issues found.");
            return 0;
        }

        var safety = FixPlanSafety.ValidateApply(plan, yes, allowInferred, maxChanges);
        if (!safety.Success)
        {
            await output.WriteLineAsync($"Error: {safety.Error}");
            return 1;
        }

        string? baselinePath = null;
        if (!noSnapshot)
        {
            baselinePath = baselineOutput ?? Path.Combine(".revitcli", $"fix-baseline-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.json");
            var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest());
            if (!snapshotResult.Success)
            {
                await output.WriteLineAsync($"Error: failed to capture baseline: {snapshotResult.Error}");
                return 1;
            }

            try
            {
                var snapshotDir = Path.GetDirectoryName(Path.GetFullPath(baselinePath));
                if (!string.IsNullOrEmpty(snapshotDir))
                {
                    Directory.CreateDirectory(snapshotDir);
                }

                await File.WriteAllTextAsync(
                    baselinePath,
                    JsonSerializer.Serialize(snapshotResult.Data, TerminalJsonOptions.Pretty),
                    default);
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"Error: failed to save baseline snapshot: {ex.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Baseline saved: {baselinePath}");
        }
        else
        {
            await output.WriteLineAsync("Warning: --no-snapshot disables automatic rollback support.");
        }

        var startedAt = DateTime.UtcNow.ToString("o");
        FixJournal? journal = null;
        string? journalPath = null;
        if (baselinePath != null)
        {
            journal = new FixJournal
            {
                CheckName = run.Data.CheckName,
                ProfilePath = run.Data.ProfilePath,
                BaselinePath = baselinePath,
                StartedAt = startedAt,
                User = Environment.UserName,
                Actions = applyActions
            };
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
        }

        var modified = 0;
        var baselineExists = baselinePath != null;
        foreach (var action in applyActions)
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
                if (baselineExists)
                    await output.WriteLineAsync($"Rollback: revitcli rollback {baselinePath} --yes");
                return 1;
            }

            modified += result.Data.Affected;
        }

        if (journal != null)
        {
            journal.CompletedAt = DateTime.UtcNow.ToString("o");
            try
            {
                await File.WriteAllTextAsync(
                    journalPath!,
                    JsonSerializer.Serialize(journal, TerminalJsonOptions.Pretty),
                    default);
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"Error: failed to write fix journal: {ex.Message}");
                return 1;
            }
        }

        await output.WriteLineAsync($"Modified {modified} element parameter(s).");
        if (baselineExists)
        {
            await output.WriteLineAsync($"Rollback: revitcli rollback {baselinePath} --yes");
            if (journalPath != null)
                await output.WriteLineAsync($"Journal saved: {journalPath}");
        }

        return 0;
    }
}
