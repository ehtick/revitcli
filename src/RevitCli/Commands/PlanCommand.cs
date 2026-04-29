using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Plans;

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
            Param = plan.Summary.Param,
            Value = plan.Summary.Value,
            Preview = data.Preview
        });
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return 0;
    }
}
