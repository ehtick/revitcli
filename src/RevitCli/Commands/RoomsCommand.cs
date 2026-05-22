using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Numbering;
using RevitCli.Shared;
using YamlDotNet.Core;

namespace RevitCli.Commands;

public static class RoomsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(RevitClient client)
    {
        var command = new Command("rooms", "Plan and review room numbering workflows");
        command.AddCommand(CreateRenumberCommand(client));
        return command;
    }

    private static Command CreateRenumberCommand(RevitClient client)
    {
        var ruleOpt = new Option<string>("--rule", "Room numbering rule YAML") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write frozen room-numbering plan JSON") { IsRequired = true };
        var scopeOpt = new Option<string>("--scope", () => "all", "Room scope: all or text/glob matched against name/type/parameters");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var maxChangesOpt = new Option<int?>("--max-changes", "Maximum planned room number changes");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("renumber", "Create a reviewed room numbering plan")
        {
            ruleOpt,
            planOutputOpt,
            scopeOpt,
            dryRunOpt,
            maxChangesOpt,
            outputOpt
        };

        command.SetHandler(async (string rule, string planOutput, string scope, bool dryRun, int? maxChanges, string output) =>
        {
            Environment.ExitCode = await ExecuteRenumberAsync(
                client,
                rule,
                planOutput,
                scope,
                dryRun,
                maxChanges,
                output,
                Console.Out);
        }, ruleOpt, planOutputOpt, scopeOpt, dryRunOpt, maxChangesOpt, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteRenumberAsync(
        RevitClient client,
        string rulePath,
        string planOutputPath,
        string scope,
        bool dryRun,
        int? maxChanges,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat);
        if (normalizedOutput == null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        if (!dryRun)
        {
            await output.WriteLineAsync("Error: rooms renumber only creates reviewed plans. Use --dry-run, then apply the saved plan with revitcli plan apply.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(rulePath))
        {
            await output.WriteLineAsync("Error: --rule is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(planOutputPath))
        {
            await output.WriteLineAsync("Error: --plan-output is required.");
            return 1;
        }

        if (maxChanges.HasValue && maxChanges.Value < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        LoadedRoomNumberingRule rule;
        try
        {
            rule = RoomNumberingRuleStore.Load(rulePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var query = await client.QueryElementsAsync("rooms", filter: null);
        if (!query.Success)
        {
            await output.WriteLineAsync($"Error: {query.Error}");
            return 4;
        }

        RoomNumberingPlan plan;
        try
        {
            plan = RoomNumberingPlanner.Create(query.Data ?? Array.Empty<ElementInfo>(), rule, scope, planOutputPath);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (maxChanges.HasValue && plan.Summary.ActionCount > maxChanges.Value)
        {
            await output.WriteLineAsync(
                $"Error: plan has {plan.Summary.ActionCount} change(s), exceeds --max-changes {maxChanges.Value}.");
            return 1;
        }

        RoomNumberingPlanStore.Save(planOutputPath, plan);

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(plan, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderMarkdown(plan, planOutputPath));
                break;
            default:
                await output.WriteLineAsync(RenderTable(plan, planOutputPath));
                break;
        }

        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    private static string RenderTable(RoomNumberingPlan plan, string planPath)
    {
        var writer = new StringWriter();
        writer.WriteLine($"Room numbering dry-run plan: {Path.GetFullPath(planPath)}");
        writer.WriteLine($"Rooms: {plan.Summary.RoomCount}; selected: {plan.Summary.SelectedCount}; actions: {plan.Summary.ActionCount}; skipped: {plan.Summary.SkippedCount}");
        foreach (var action in plan.Actions)
            writer.WriteLine($"  [{action.RoomId}] {action.RoomName}: \"{action.OldNumber}\" -> \"{action.NewNumber}\"");
        foreach (var skipped in plan.Skipped)
            writer.WriteLine($"  skipped [{skipped.RoomId}] {skipped.RoomName}: {skipped.Message}");
        writer.WriteLine($"Review: {plan.Commands.Show}");
        writer.WriteLine($"Dry-run apply: {plan.Commands.DryRunApply}");
        writer.WriteLine($"Apply: {plan.Commands.Apply}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderMarkdown(RoomNumberingPlan plan, string planPath)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Room Numbering Plan");
        writer.WriteLine();
        writer.WriteLine($"- Plan: `{EscapeInline(Path.GetFullPath(planPath))}`");
        writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
        writer.WriteLine($"- Rule: `{EscapeInline(plan.RulePath)}`");
        writer.WriteLine($"- Scope: `{EscapeInline(plan.Scope)}`");
        writer.WriteLine($"- Rooms: `{plan.Summary.RoomCount}`");
        writer.WriteLine($"- Selected: `{plan.Summary.SelectedCount}`");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();

        if (plan.Actions.Count > 0)
        {
            writer.WriteLine("## Actions");
            writer.WriteLine();
            writer.WriteLine("| Room | Old number | New number | Group | Sort |");
            writer.WriteLine("| --- | --- | --- | --- | --- |");
            foreach (var action in plan.Actions)
            {
                writer.WriteLine(
                    $"| {EscapeTable($"{action.RoomName} [{action.RoomId}]")} | {EscapeTable(action.OldNumber)} | {EscapeTable(action.NewNumber)} | {EscapeTable(action.GroupKey)} | {EscapeTable(action.SortKey)} |");
            }
            writer.WriteLine();
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Room | Reason | Message |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var skipped in plan.Skipped)
                writer.WriteLine($"| {EscapeTable($"{skipped.RoomName} [{skipped.RoomId}]")} | `{EscapeInline(skipped.Reason)}` | {EscapeTable(skipped.Message)} |");
            writer.WriteLine();
        }

        writer.WriteLine("## Commands");
        writer.WriteLine();
        writer.WriteLine($"- Review: `{EscapeInline(plan.Commands.Show)}`");
        writer.WriteLine($"- Dry-run apply: `{EscapeInline(plan.Commands.DryRunApply)}`");
        writer.WriteLine($"- Apply: `{EscapeInline(plan.Commands.Apply)}`");
        return writer.ToString().TrimEnd();
    }

    private static string? NormalizeOutput(string? outputFormat)
    {
        var normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();
        return normalized is "table" or "json" or "markdown" ? normalized : null;
    }

    private static string EscapeInline(string? value) =>
        (value ?? "").Replace("`", "'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeTable(string? value) =>
        (value ?? "").Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
}
