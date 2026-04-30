using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Plans;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class PlanCommand
{
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
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json");
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
        var maxChangesOpt = new Option<int>("--max-changes", () => 50, "Maximum number of element writes");
        var command = new Command("apply", "Apply a saved mutation plan") { fileArg, yesOpt, dryRunOpt, maxChangesOpt };
        command.SetHandler(async (file, yes, dryRun, maxChanges) =>
        {
            Environment.ExitCode = await ExecuteApplyAsync(client, file, yes, dryRun, maxChanges, Console.Out);
        }, fileArg, yesOpt, dryRunOpt, maxChangesOpt);
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
            await output.WriteLineAsync(JsonSerializer.Serialize(plan, SetPlanFileStore.JsonOptions));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: {plan.Type} {plan.Summary.Affected} element(s), param=\"{plan.Summary.Param}\", value=\"{plan.Summary.Value}\"");
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
        TextWriter output)
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
            return await ExecuteApplyImportAsync(client, file, yes, dryRun, maxChanges, output);

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
        var receiptPath = SetPlanFileStore.SaveReceipt(file, new PlanReceipt
        {
            PlanPath = Path.GetFullPath(file),
            AppliedAtUtc = DateTime.UtcNow.ToString("o"),
            AppliedBy = Environment.UserName,
            Affected = data.Affected,
            Operation = "set",
            Param = plan.Summary.Param,
            Value = plan.Summary.Value,
            Preview = data.Preview
        });
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
            await output.WriteLineAsync(JsonSerializer.Serialize(plan, SetPlanFileStore.JsonOptions));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: import {plan.Summary.GroupCount} group(s), {plan.Summary.ElementWrites} element-write(s), category=\"{plan.Summary.Category}\"");
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

    private static async Task<int> ExecuteApplyImportAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
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

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        var (affected, previews, failures) = await ApplyImportGroupsAsync(
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

        var receiptPath = SetPlanFileStore.SaveReceipt(file, new PlanReceipt
        {
            PlanPath = Path.GetFullPath(file),
            AppliedAtUtc = DateTime.UtcNow.ToString("o"),
            AppliedBy = Environment.UserName,
            Affected = affected,
            Success = failures.Count == 0,
            Operation = "import",
            GroupCount = plan.Groups.Count,
            ElementWrites = elementWrites,
            Groups = plan.Groups,
            Failures = failures
        });
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return failures.Count == 0 ? 0 : 2;
    }

    private static async Task<(int Affected, List<SetPreviewItem> Previews, List<PlanApplyFailure> Failures)> ApplyImportGroupsAsync(
        RevitClient client,
        ImportPlanFile plan,
        bool dryRun,
        int batchSize)
    {
        var failures = new List<PlanApplyFailure>();
        var previews = new List<SetPreviewItem>();
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

                affected += resp.Data?.Affected ?? 0;
                previews.AddRange(resp.Data?.Preview ?? new List<SetPreviewItem>());
            }
        }

        return (affected, previews, failures);
    }
}
