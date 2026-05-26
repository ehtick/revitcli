using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Fix;
using RevitCli.Numbering;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Profile;
using RevitCli.Shared;
using RevitCli.Sheets;

namespace RevitCli.Commands;

public static class PlanCommand
{
    private const int DefaultMaxChanges = 50;
    private static readonly JsonSerializerOptions SchemaPlanReadOptions = new() { PropertyNameCaseInsensitive = true };

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

        if (type.Equals("sheet-issue", StringComparison.OrdinalIgnoreCase))
            return await ExecuteShowSheetIssueAsync(file, outputFormat, output);

        if (type.Equals("sheet-renumber", StringComparison.OrdinalIgnoreCase))
            return await ExecuteShowSheetRenumberAsync(file, outputFormat, output);

        if (type.Equals("room-numbering", StringComparison.OrdinalIgnoreCase))
            return await ExecuteShowRoomNumberingAsync(file, outputFormat, output);

        if (type.Equals("mark-assignment", StringComparison.OrdinalIgnoreCase))
            return await ExecuteShowMarkAssignmentAsync(file, outputFormat, output);

        var schemaVersion = ReadSchemaVersion(file);
        if (schemaVersion is "schedule-ensure-plan.v1" or "view-template-plan.v1" or "view-clone-plan.v1" or "link-repair-plan.v1" or "model-map-fix-plan.v1")
            return await ExecuteShowSchemaPlanAsync(file, schemaVersion, outputFormat, output);

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

    private static async Task<int> ExecuteShowSheetIssueAsync(string file, string outputFormat, TextWriter output)
    {
        SheetIssuePlan plan;
        try
        {
            plan = SheetIssuePlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(plan, SheetIssuePlanStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderSheetIssueMarkdown(plan, file));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: {plan.Type} {plan.Summary.ActionCount} action(s), selector=\"{plan.Selector}\", issue=\"{plan.IssueCode} / {plan.IssueDate}\"");
        await output.WriteLineAsync($"Model: {plan.ModelFingerprint.Document} {plan.ModelFingerprint.FileHash}");
        await output.WriteLineAsync($"Skipped: {plan.Summary.SkippedCount}");
        foreach (var action in plan.Actions.Take(20))
            await output.WriteLineAsync($"  [{action.SheetId}] {action.SheetNumber} {action.SheetName}: {action.Parameter} \"{action.OldValue}\" -> \"{action.NewValue}\"");

        if (plan.Actions.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Actions.Count - 20} more.");

        foreach (var skipped in plan.Skipped.Take(20))
            await output.WriteLineAsync($"  skipped [{skipped.SheetId}] {skipped.SheetNumber} {skipped.Key}: {skipped.Message}");

        if (plan.Skipped.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Skipped.Count - 20} more skipped.");

        return 0;
    }

    private static string RenderSheetIssueMarkdown(SheetIssuePlan plan, string file)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Sheet Issue Plan");
        writer.WriteLine();
        writer.WriteLine($"- Plan: {InlineCode(Path.GetFullPath(file))}");
        writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
        writer.WriteLine($"- Selector: {InlineCode(plan.Selector)}");
        writer.WriteLine($"- Issue code: {InlineCode(plan.IssueCode)}");
        writer.WriteLine($"- Issue date: {InlineCode(plan.IssueDate)}");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();

        if (plan.Actions.Count == 0)
        {
            writer.WriteLine("No sheet metadata changes planned.");
        }
        else
        {
            writer.WriteLine("| Sheet | Parameter | Old | New |");
            writer.WriteLine("| --- | --- | --- | --- |");
            foreach (var action in plan.Actions.Take(50))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{action.SheetNumber} {action.SheetName}".Trim())} | {InlineCode(action.Parameter)} | {EscapeTableCell(action.OldValue)} | {EscapeTableCell(action.NewValue)} |");
            }
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Sheet | Key | Reason | Message |");
            writer.WriteLine("| --- | --- | --- | --- |");
            foreach (var skipped in plan.Skipped.Take(50))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{skipped.SheetNumber} {skipped.SheetName}".Trim())} | {InlineCode(skipped.Key)} | `{EscapeTableCell(skipped.Reason)}` | {EscapeTableCell(skipped.Message)} |");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static async Task<int> ExecuteShowSheetRenumberAsync(string file, string outputFormat, TextWriter output)
    {
        SheetRenumberPlan plan;
        try
        {
            plan = SheetRenumberPlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(plan, SheetRenumberPlanStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderSheetRenumberMarkdown(plan, file));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: {plan.Type} {plan.Summary.ActionCount} action(s), selector=\"{plan.Selector}\", rule=\"{plan.RulePath}\"");
        await output.WriteLineAsync($"Model: {plan.ModelFingerprint.Document} {plan.ModelFingerprint.FileHash}");
        await output.WriteLineAsync($"Skipped: {plan.Summary.SkippedCount}");
        foreach (var action in plan.Actions.Take(20))
            await output.WriteLineAsync($"  [{action.SheetId}] {action.SheetNumber} {action.SheetName}: \"{action.OldNumber}\" -> \"{action.NewNumber}\"");

        if (plan.Actions.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Actions.Count - 20} more.");

        foreach (var skipped in plan.Skipped.Take(20))
            await output.WriteLineAsync($"  skipped [{skipped.SheetId}] {skipped.SheetNumber}: {skipped.Message}");

        if (plan.Skipped.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Skipped.Count - 20} more skipped.");

        return 0;
    }

    private static string RenderSheetRenumberMarkdown(SheetRenumberPlan plan, string file)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Sheet Renumber Plan");
        writer.WriteLine();
        writer.WriteLine($"- Plan: {InlineCode(Path.GetFullPath(file))}");
        writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
        writer.WriteLine($"- Selector: {InlineCode(plan.Selector)}");
        writer.WriteLine($"- Rule: {InlineCode(plan.RulePath)}");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();

        if (plan.Actions.Count == 0)
        {
            writer.WriteLine("No sheet renumber changes planned.");
        }
        else
        {
            writer.WriteLine("| Sheet | Old number | New number |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var action in plan.Actions.Take(50))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{action.SheetNumber} {action.SheetName}".Trim())} | {EscapeTableCell(action.OldNumber)} | {EscapeTableCell(action.NewNumber)} |");
            }
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Sheet | Reason | Message |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var skipped in plan.Skipped.Take(50))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{skipped.SheetNumber} {skipped.SheetName}".Trim())} | `{EscapeTableCell(skipped.Reason)}` | {EscapeTableCell(skipped.Message)} |");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static async Task<int> ExecuteShowRoomNumberingAsync(string file, string outputFormat, TextWriter output)
    {
        RoomNumberingPlan plan;
        try
        {
            plan = RoomNumberingPlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(plan, RoomNumberingPlanStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderRoomNumberingMarkdown(plan, file));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: {plan.Type} {plan.Summary.ActionCount} action(s), scope=\"{plan.Scope}\", rule=\"{plan.RulePath}\"");
        await output.WriteLineAsync($"Skipped: {plan.Summary.SkippedCount}");
        foreach (var action in plan.Actions.Take(20))
            await output.WriteLineAsync($"  [{action.RoomId}] {action.RoomName}: \"{action.OldNumber}\" -> \"{action.NewNumber}\"");
        if (plan.Actions.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Actions.Count - 20} more.");
        foreach (var skipped in plan.Skipped.Take(20))
            await output.WriteLineAsync($"  skipped [{skipped.RoomId}] {skipped.RoomName}: {skipped.Message}");
        await output.WriteLineAsync($"Dry-run apply: {plan.Commands.DryRunApply}");
        await output.WriteLineAsync($"Apply: {plan.Commands.Apply}");
        return 0;
    }

    private static string RenderRoomNumberingMarkdown(RoomNumberingPlan plan, string file)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Room Numbering Plan");
        writer.WriteLine();
        writer.WriteLine($"- Plan: {InlineCode(Path.GetFullPath(file))}");
        writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
        writer.WriteLine($"- Scope: {InlineCode(plan.Scope)}");
        writer.WriteLine($"- Rule: {InlineCode(plan.RulePath)}");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();

        if (plan.Actions.Count > 0)
        {
            writer.WriteLine("## Actions");
            writer.WriteLine();
            writer.WriteLine("| Room | Old number | New number | Group | Sort |");
            writer.WriteLine("| --- | --- | --- | --- | --- |");
            foreach (var action in plan.Actions.Take(100))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{action.RoomName} [{action.RoomId}]")} | {EscapeTableCell(action.OldNumber)} | {EscapeTableCell(action.NewNumber)} | {EscapeTableCell(action.GroupKey)} | {EscapeTableCell(action.SortKey)} |");
            }
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Room | Reason | Message |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var skipped in plan.Skipped.Take(100))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{skipped.RoomName} [{skipped.RoomId}]")} | `{EscapeTableCell(skipped.Reason)}` | {EscapeTableCell(skipped.Message)} |");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Commands");
        writer.WriteLine();
        writer.WriteLine($"- Dry-run apply: {InlineCode(plan.Commands.DryRunApply)}");
        writer.WriteLine($"- Apply: {InlineCode(plan.Commands.Apply)}");
        return writer.ToString().TrimEnd();
    }

    private static async Task<int> ExecuteShowMarkAssignmentAsync(string file, string outputFormat, TextWriter output)
    {
        MarkAssignmentPlan plan;
        try
        {
            plan = MarkAssignmentPlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(plan, MarkAssignmentPlanStore.JsonOptions));
            return 0;
        }

        if (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderMarkAssignmentMarkdown(plan, file));
            return 0;
        }

        await output.WriteLineAsync(
            $"Plan: {plan.Type} {plan.Summary.ActionCount} action(s), category=\"{plan.Category}\", rule=\"{plan.RulePath}\"");
        await output.WriteLineAsync($"Skipped: {plan.Summary.SkippedCount}");
        foreach (var action in plan.Actions.Take(20))
            await output.WriteLineAsync($"  [{action.ElementId}] {action.ElementName}: \"{action.OldMark}\" -> \"{action.NewMark}\"");
        if (plan.Actions.Count > 20)
            await output.WriteLineAsync($"  ... and {plan.Actions.Count - 20} more.");
        foreach (var skipped in plan.Skipped.Take(20))
            await output.WriteLineAsync($"  skipped [{skipped.ElementId}] {skipped.ElementName}: {skipped.Message}");
        await output.WriteLineAsync($"Dry-run apply: {plan.Commands.DryRunApply}");
        await output.WriteLineAsync($"Apply: {plan.Commands.Apply}");
        return 0;
    }

    private static async Task<int> ExecuteShowSchemaPlanAsync(
        string file,
        string schemaVersion,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        using (document)
        {
            var summary = CreateSchemaPlanSummary(file, schemaVersion, document.RootElement);
            var preview = CreateSchemaPlanPreview(schemaVersion, document.RootElement);
            if (normalized == "json")
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(summary, TerminalJsonOptions.PrettyCamel));
                return 0;
            }

            if (normalized == "markdown")
            {
                await output.WriteLineAsync(RenderMarkdownSummary(summary, preview));
                return 0;
            }

            var schemaSummary = (SchemaPlanSummary)summary.Summary;
            await output.WriteLineAsync(
                $"Plan {summary.Type}: actions={schemaSummary.ActionCount}, issues={summary.Issues.Count}, risk={summary.Risk.Level}, file={Path.GetFullPath(file)}");
            await output.WriteLineAsync($"Show: {summary.Commands.Show}");
            return 0;
        }
    }

    private static PlanShowOutput CreateSchemaPlanSummary(string file, string schemaVersion, JsonElement root)
    {
        var issues = ReadSchemaPlanIssues(root);
        var actionCount = ReadInt(root, "summary", "actionCount");
        var issueCount = Math.Max(ReadInt(root, "summary", "issueCount"), CountArray(root, "issues"));
        if (actionCount == 0)
            issues.Add(new PlanSummaryIssue("info", "schema-plan-no-actions", "Plan has no actions."));

        var planType = SchemaPlanType(schemaVersion);
        var summary = new SchemaPlanSummary(
            planType,
            SchemaPlanSource(root, schemaVersion),
            SchemaPlanTarget(root, schemaVersion),
            ReadInt(root, "summary", "specCount"),
            ReadInt(root, "summary", "candidateCount"),
            ReadInt(root, "summary", "sourceCount"),
            ReadInt(root, "summary", "existingCount"),
            actionCount,
            Math.Max(issueCount, issues.Count(issue => !string.Equals(issue.Severity, "info", StringComparison.OrdinalIgnoreCase))),
            ReadInt(root, "summary", "baselineCount"),
            ReadInt(root, "summary", "placedOnSheetCount") + ReadInt(root, "summary", "placedSourceCount"));

        return new PlanShowOutput(
            "plan-summary.v1",
            true,
            IsValid(issues),
            planType,
            TryGetString(root, "planPath") ?? Path.GetFullPath(file),
            summary,
            CreateSchemaPlanRisk(actionCount, issues),
            CreateSchemaPlanCommands(file, schemaVersion),
            issues,
            root);
    }

    private static List<PlanSummaryIssue> ReadSchemaPlanIssues(JsonElement root)
    {
        var issues = new List<PlanSummaryIssue>();
        if (!root.TryGetProperty("issues", out var issuesElement) || issuesElement.ValueKind != JsonValueKind.Array)
            return issues;

        foreach (var issue in issuesElement.EnumerateArray())
        {
            var severity = TryGetString(issue, "severity") ?? "error";
            var code = TryGetString(issue, "code") ?? "schema-plan-issue";
            var message = TryGetString(issue, "message") ?? code;
            issues.Add(new PlanSummaryIssue(severity, code, message));
        }

        return issues;
    }

    private static PlanRisk CreateSchemaPlanRisk(int actionCount, IReadOnlyList<PlanSummaryIssue> issues)
    {
        var hasError = issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var level = hasError
            ? "blocked"
            : actionCount switch
            {
                >= 50 => "high",
                >= 10 => "medium",
                _ => "low"
            };
        var notes = new List<string>
        {
            "Schema plan is review-only in this RevitCli build; regenerate it from the originating command after model changes."
        };
        if (hasError)
            notes.Add("Plan has blocking issues and cannot be applied as-is.");

        return new PlanRisk(level, actionCount, false, true, false, false, notes);
    }

    private static SetPlanCommands CreateSchemaPlanCommands(string file, string schemaVersion)
    {
        var fullPath = Path.GetFullPath(file);
        if (schemaVersion is "link-repair-plan.v1" or "model-map-fix-plan.v1")
        {
            return new SetPlanCommands
            {
                Show = $"revitcli plan show {QuoteArgument(fullPath)} --output markdown",
                DryRunApply = $"revitcli plan apply {QuoteArgument(fullPath)} --dry-run",
                Apply = $"revitcli plan apply {QuoteArgument(fullPath)} --yes"
            };
        }

        var applyNotice = $"review-only: {schemaVersion} has no plan apply path in this build";
        return new SetPlanCommands
        {
            Show = $"revitcli plan show {QuoteArgument(fullPath)} --output markdown",
            DryRunApply = applyNotice,
            Apply = applyNotice
        };
    }

    private static IReadOnlyList<SchemaPlanPreviewItem> CreateSchemaPlanPreview(string schemaVersion, JsonElement root)
    {
        if (!root.TryGetProperty("actions", out var actionsElement) || actionsElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<SchemaPlanPreviewItem>();

        return actionsElement.EnumerateArray()
            .Select(action => schemaVersion switch
            {
                "view-template-plan.v1" => new SchemaPlanPreviewItem(
                    TryGetLong(action, "viewId") ?? 0,
                    TryGetString(action, "viewName") ?? "",
                    "template",
                    TryGetString(action, "newTemplateName") ?? ""),
                "view-clone-plan.v1" => new SchemaPlanPreviewItem(
                    TryGetLong(action, "sourceViewId") ?? 0,
                    TryGetString(action, "sourceName") ?? "",
                    "clone",
                    TryGetString(action, "targetName") ?? ""),
                "schedule-ensure-plan.v1" => new SchemaPlanPreviewItem(
                    0,
                    TryGetString(action, "name") ?? "",
                    TryGetString(action, "action") ?? "ensure",
                    TryGetString(action, "category") ?? ""),
                "link-repair-plan.v1" => new SchemaPlanPreviewItem(
                    TryGetLong(action, "linkId") ?? 0,
                    TryGetString(action, "linkName") ?? "",
                    "repair-link",
                    TryGetString(action, "newPath") ?? ""),
                "model-map-fix-plan.v1" => new SchemaPlanPreviewItem(
                    TryGetLong(action, "elementId") ?? 0,
                    TryGetString(action, "elementName") ?? "",
                    TryGetString(action, "field") ?? "map-fix",
                    TryGetString(action, "newValue") ?? ""),
                _ => new SchemaPlanPreviewItem(0, "", "action", "")
            })
            .ToArray();
    }

    private static string SchemaPlanType(string schemaVersion) =>
        schemaVersion switch
        {
            "schedule-ensure-plan.v1" => "schedule-ensure",
            "view-template-plan.v1" => "view-template",
            "view-clone-plan.v1" => "view-clone",
            "link-repair-plan.v1" => "link-repair",
            "model-map-fix-plan.v1" => "model-map-fix",
            _ => "schema-plan"
        };

    private static string SchemaPlanSource(JsonElement root, string schemaVersion) =>
        schemaVersion switch
        {
            "schedule-ensure-plan.v1" => TryGetString(root, "specPath") ?? "",
            "view-template-plan.v1" => TryGetString(root, "selector") ?? "",
            "view-clone-plan.v1" => TryGetString(root, "fromSet") ?? "",
            "link-repair-plan.v1" => TryGetString(root, "mapPath") ?? "",
            "model-map-fix-plan.v1" => TryGetString(root, "rulesPath") ?? "",
            _ => ""
        };

    private static string SchemaPlanTarget(JsonElement root, string schemaVersion) =>
        schemaVersion switch
        {
            "schedule-ensure-plan.v1" => TryGetString(root, "mode") ?? "",
            "view-template-plan.v1" => TryGetString(root, "templateName") ?? "",
            "view-clone-plan.v1" => TryGetString(root, "toPrefix") ?? "",
            "link-repair-plan.v1" => ReadInt(root, "summary", "actionCount").ToString(CultureInfo.InvariantCulture),
            "model-map-fix-plan.v1" => TryGetString(root, "scope") ?? "",
            _ => ""
        };

    private static string ReadSchemaVersion(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return TryGetString(document.RootElement, "schemaVersion") ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return "";
        }
    }

    private static int ReadInt(JsonElement root, string objectProperty, string numberProperty)
    {
        if (!root.TryGetProperty(objectProperty, out var obj) ||
            !obj.TryGetProperty(numberProperty, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed))
        {
            return 0;
        }

        return parsed;
    }

    private static int CountArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return 0;

        return value.GetArrayLength();
    }

    private static string? TryGetString(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetLong(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private static string RenderMarkAssignmentMarkdown(MarkAssignmentPlan plan, string file)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Mark Assignment Plan");
        writer.WriteLine();
        writer.WriteLine($"- Plan: {InlineCode(Path.GetFullPath(file))}");
        writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
        writer.WriteLine($"- Category: {InlineCode(plan.Category)}");
        writer.WriteLine($"- Rule: {InlineCode(plan.RulePath)}");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();
        writer.WriteLine("| Element | Old Mark | New Mark | Sort |");
        writer.WriteLine("| --- | --- | --- | --- |");
        foreach (var action in plan.Actions.Take(100))
        {
            writer.WriteLine(
                $"| {EscapeTableCell($"{action.ElementName} [{action.ElementId}]")} | {EscapeTableCell(action.OldMark)} | {EscapeTableCell(action.NewMark)} | {EscapeTableCell(action.SortKey)} |");
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Element | Reason | Message |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var skipped in plan.Skipped.Take(100))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{skipped.ElementName} [{skipped.ElementId}]")} | `{EscapeTableCell(skipped.Reason)}` | {EscapeTableCell(skipped.Message)} |");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Commands");
        writer.WriteLine();
        writer.WriteLine($"- Dry-run apply: {InlineCode(plan.Commands.DryRunApply)}");
        writer.WriteLine($"- Apply: {InlineCode(plan.Commands.Apply)}");
        return writer.ToString().TrimEnd();
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

        if (type.Equals("sheet-issue", StringComparison.OrdinalIgnoreCase))
            return await ExecuteApplySheetIssueAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

        if (type.Equals("sheet-renumber", StringComparison.OrdinalIgnoreCase))
            return await ExecuteApplySheetRenumberAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

        if (type.Equals("room-numbering", StringComparison.OrdinalIgnoreCase))
            return await ExecuteApplyRoomNumberingAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

        if (type.Equals("mark-assignment", StringComparison.OrdinalIgnoreCase))
            return await ExecuteApplyMarkAssignmentAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

        var schemaVersion = ReadSchemaVersion(file);
        if (schemaVersion == "link-repair-plan.v1")
            return await ExecuteApplyLinkRepairAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

        if (schemaVersion == "model-map-fix-plan.v1")
            return await ExecuteApplyModelMapFixAsync(
                client, file, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact, output);

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

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
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

    private static async Task<int> ExecuteApplySheetIssueAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        SheetIssuePlan plan;
        try
        {
            plan = SheetIssuePlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var actionCount = plan.Actions.Count;
        if (actionCount == 0)
        {
            await output.WriteLineAsync("Plan has no sheet issue metadata actions. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (actionCount > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(actionCount, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        if (!dryRun)
        {
            var validation = await ValidateSheetIssuePlanCurrentStateAsync(client, plan);
            if (!validation.Success)
            {
                await output.WriteLineAsync($"Error: {validation.Message}");
                return 1;
            }
        }

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
                return 1;
        }

        var (affected, previews, failures, rollbackActions) = await ApplySheetIssueGroupsAsync(client, plan, dryRun);
        if (dryRun)
        {
            if (failures.Count > 0)
            {
                await output.WriteLineAsync($"Error: dry-run failed for {failures.Count} sheet issue group(s):");
                foreach (var failure in failures)
                    await output.WriteLineAsync(
                        $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {affected} sheet issue metadata value(s) would be modified from plan.");
            foreach (var item in previews.Take(20))
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            if (previews.Count > 20)
                await output.WriteLineAsync($"  ... and {previews.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync(
            $"Applied sheet issue plan: modified {affected} sheet issue metadata value(s) across {CreateSheetIssueGroups(plan).Count} group(s).");
        if (failures.Count > 0)
        {
            await output.WriteLineAsync($"Failed: {failures.Count} group(s):");
            foreach (var failure in failures)
                await output.WriteLineAsync(
                    $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
        }

        var receipt = CreatePlanReceipt(
            file,
            "sheet-issue",
            affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = failures.Count == 0;
        receipt.GroupCount = CreateSheetIssueGroups(plan).Count;
        receipt.ElementWrites = actionCount;
        receipt.PlanActionCount = plan.Actions.Count;
        receipt.SkippedCount = plan.Skipped.Count;
        receipt.Param = "sheet issue metadata";
        receipt.Value = $"{plan.IssueCode} / {plan.IssueDate}";
        receipt.Preview = previews;
        receipt.Failures = failures;
        receipt.RollbackActions = rollbackActions;
        receipt.AffectedElementIds = receipt.RollbackActions.Count > 0
            ? DistinctSorted(receipt.RollbackActions.Select(action => action.ElementId))
            : DistinctSorted(plan.Actions.Select(action => action.SheetId));
        receipt.RequiresRollback = receipt.RollbackActions.Count > 0;
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return failures.Count == 0 ? 0 : 2;
    }

    private static async Task<(bool Success, string Message)> ValidateSheetIssuePlanCurrentStateAsync(
        RevitClient client,
        SheetIssuePlan plan)
    {
        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false
        });

        if (!snapshotResult.Success)
            return (false, $"failed to validate sheet issue plan: {snapshotResult.Error}");

        var snapshot = snapshotResult.Data ?? new ModelSnapshot();
        var fingerprintError = ValidateSheetIssueModelFingerprint(plan.ModelFingerprint, snapshot);
        if (!string.IsNullOrWhiteSpace(fingerprintError))
            return (false, fingerprintError);

        var sheetsById = snapshot.Sheets.ToDictionary(sheet => sheet.ViewId);
        var conflicts = new List<string>();
        foreach (var action in plan.Actions)
        {
            if (!sheetsById.TryGetValue(action.SheetId, out var sheet))
            {
                conflicts.Add($"sheet {action.SheetNumber} ({action.SheetId}) was not found in the current model");
                continue;
            }

            var parameter = sheet.Parameters.Keys.FirstOrDefault(key =>
                string.Equals(key, action.Parameter, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(parameter))
            {
                conflicts.Add($"sheet {action.SheetNumber} parameter {action.Parameter} was not found in the current model");
                continue;
            }

            var currentValue = sheet.Parameters[parameter];
            if (!string.Equals(currentValue ?? "", action.OldValue ?? "", StringComparison.Ordinal))
            {
                conflicts.Add(
                    $"sheet {action.SheetNumber} parameter {action.Parameter} expected \"{action.OldValue}\" but current value is \"{currentValue}\"");
            }
        }

        if (conflicts.Count == 0)
            return (true, "");

        var preview = string.Join("; ", conflicts.Take(5));
        if (conflicts.Count > 5)
            preview += $"; and {conflicts.Count - 5} more";
        return (false, $"sheet issue plan is stale: {preview}");
    }

    private static async Task<int> ExecuteApplySheetRenumberAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        SheetRenumberPlan plan;
        try
        {
            plan = SheetRenumberPlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var actionCount = plan.Actions.Count;
        if (actionCount == 0)
        {
            await output.WriteLineAsync("Plan has no sheet renumber actions. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (actionCount > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(actionCount, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        if (!dryRun)
        {
            var validation = await ValidateSheetRenumberPlanCurrentStateAsync(client, plan);
            if (!validation.Success)
            {
                await output.WriteLineAsync($"Error: {validation.Message}");
                return 1;
            }
        }

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
                return 1;
        }

        var (affected, previews, failures, rollbackActions) = await ApplySheetRenumberGroupsAsync(client, plan, dryRun);
        if (dryRun)
        {
            if (failures.Count > 0)
            {
                await output.WriteLineAsync($"Error: dry-run failed for {failures.Count} sheet renumber group(s):");
                foreach (var failure in failures)
                    await output.WriteLineAsync(
                        $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {affected} sheet number(s) would be modified from plan.");
            foreach (var item in previews.Take(20))
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            if (previews.Count > 20)
                await output.WriteLineAsync($"  ... and {previews.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync(
            $"Applied sheet renumber plan: modified {affected} sheet number(s) across {CreateSheetRenumberGroups(plan).Count} group(s).");
        if (failures.Count > 0)
        {
            await output.WriteLineAsync($"Failed: {failures.Count} group(s):");
            foreach (var failure in failures)
                await output.WriteLineAsync(
                    $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
        }

        var receipt = CreatePlanReceipt(
            file,
            "sheet-renumber",
            affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = failures.Count == 0;
        receipt.GroupCount = CreateSheetRenumberGroups(plan).Count;
        receipt.ElementWrites = actionCount;
        receipt.PlanActionCount = plan.Actions.Count;
        receipt.SkippedCount = plan.Skipped.Count;
        receipt.Param = SheetRenumberPlanner.SheetNumberParameter;
        receipt.Value = $"{actionCount} sheet number change(s)";
        receipt.Preview = previews;
        receipt.Failures = failures;
        receipt.RollbackActions = rollbackActions;
        receipt.AffectedElementIds = receipt.RollbackActions.Count > 0
            ? DistinctSorted(receipt.RollbackActions.Select(action => action.ElementId))
            : DistinctSorted(plan.Actions.Select(action => action.SheetId));
        receipt.RequiresRollback = receipt.RollbackActions.Count > 0;
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return failures.Count == 0 ? 0 : 2;
    }

    private static async Task<(bool Success, string Message)> ValidateSheetRenumberPlanCurrentStateAsync(
        RevitClient client,
        SheetRenumberPlan plan)
    {
        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false
        });

        if (!snapshotResult.Success)
            return (false, $"failed to validate sheet renumber plan: {snapshotResult.Error}");

        var snapshot = snapshotResult.Data ?? new ModelSnapshot();
        var fingerprintError = ValidateSheetIssueModelFingerprint(plan.ModelFingerprint, snapshot);
        if (!string.IsNullOrWhiteSpace(fingerprintError))
            return (false, fingerprintError.Replace("sheet issue plan", "sheet renumber plan", StringComparison.OrdinalIgnoreCase));

        var sheetsById = snapshot.Sheets.ToDictionary(sheet => sheet.ViewId);
        var targetIds = plan.Actions.Select(action => action.SheetId).ToHashSet();
        var conflicts = new List<string>();
        foreach (var action in plan.Actions)
        {
            if (!sheetsById.TryGetValue(action.SheetId, out var sheet))
            {
                conflicts.Add($"sheet {action.SheetNumber} ({action.SheetId}) was not found in the current model");
                continue;
            }

            if (!string.Equals(sheet.Number ?? "", action.OldNumber ?? "", StringComparison.Ordinal))
            {
                conflicts.Add(
                    $"sheet {action.SheetNumber} expected number \"{action.OldNumber}\" but current number is \"{sheet.Number}\"");
            }
        }

        var duplicateTargets = plan.Actions
            .GroupBy(action => action.NewNumber, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var duplicate in duplicateTargets)
            conflicts.Add($"target sheet number {duplicate} appears more than once in the plan");

        var unselectedCollision = snapshot.Sheets
            .Where(sheet => !targetIds.Contains(sheet.ViewId))
            .Where(sheet => plan.Actions.Any(action => string.Equals(action.NewNumber, sheet.Number, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToArray();
        foreach (var sheet in unselectedCollision)
            conflicts.Add($"target sheet number {sheet.Number} is already used by unselected sheet {sheet.Name} ({sheet.ViewId})");

        var selectedCollision = snapshot.Sheets
            .Where(sheet => targetIds.Contains(sheet.ViewId))
            .Where(sheet => plan.Actions.Any(action =>
                action.SheetId != sheet.ViewId &&
                string.Equals(action.NewNumber, sheet.Number, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToArray();
        foreach (var sheet in selectedCollision)
        {
            conflicts.Add(
                $"target sheet number {sheet.Number} is still used by selected sheet {sheet.Name} ({sheet.ViewId}); regenerate after freeing that number");
        }

        if (conflicts.Count == 0)
            return (true, "");

        var preview = string.Join("; ", conflicts.Take(5));
        if (conflicts.Count > 5)
            preview += $"; and {conflicts.Count - 5} more";
        return (false, $"sheet renumber plan is stale: {preview}");
    }

    private static async Task<int> ExecuteApplyRoomNumberingAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        RoomNumberingPlan plan;
        try
        {
            plan = RoomNumberingPlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var actionCount = plan.Actions.Count;
        if (actionCount == 0)
        {
            await output.WriteLineAsync("Plan has no room numbering actions. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (actionCount > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(actionCount, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        if (!dryRun)
        {
            var validation = await ValidateRoomNumberingPlanCurrentStateAsync(client, plan);
            if (!validation.Success)
            {
                await output.WriteLineAsync($"Error: {validation.Message}");
                return 1;
            }
        }

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
                return 1;
        }

        var (affected, previews, failures, rollbackActions) = await ApplyRoomNumberingGroupsAsync(client, plan, dryRun);
        if (dryRun)
        {
            if (failures.Count > 0)
            {
                await output.WriteLineAsync($"Error: dry-run failed for {failures.Count} room numbering group(s):");
                foreach (var failure in failures)
                    await output.WriteLineAsync(
                        $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {affected} room number(s) would be modified from plan.");
            foreach (var item in previews.Take(20))
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            if (previews.Count > 20)
                await output.WriteLineAsync($"  ... and {previews.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync(
            $"Applied room numbering plan: modified {affected} room number(s) across {RoomNumberingPlanner.CreateGroups(plan).Count} group(s).");
        if (failures.Count > 0)
        {
            await output.WriteLineAsync($"Failed: {failures.Count} group(s):");
            foreach (var failure in failures)
                await output.WriteLineAsync(
                    $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
        }

        var receipt = CreatePlanReceipt(
            file,
            "room-numbering",
            affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = failures.Count == 0;
        receipt.GroupCount = RoomNumberingPlanner.CreateGroups(plan).Count;
        receipt.ElementWrites = actionCount;
        receipt.RulePath = plan.RulePath;
        receipt.PlanActionCount = plan.Actions.Count;
        receipt.SkippedCount = plan.Skipped.Count;
        receipt.Param = plan.Parameter;
        receipt.Value = $"{actionCount} room number change(s)";
        receipt.Preview = previews;
        receipt.Failures = failures;
        receipt.Groups = RoomNumberingPlanner.CreateGroups(plan);
        receipt.RollbackActions = rollbackActions;
        receipt.AffectedElementIds = receipt.RollbackActions.Count > 0
            ? DistinctSorted(receipt.RollbackActions.Select(action => action.ElementId))
            : DistinctSorted(plan.Actions.Select(action => action.RoomId));
        receipt.RequiresRollback = receipt.RollbackActions.Count > 0;
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return failures.Count == 0 ? 0 : 2;
    }

    private static async Task<(bool Success, string Message)> ValidateRoomNumberingPlanCurrentStateAsync(
        RevitClient client,
        RoomNumberingPlan plan)
    {
        var query = await client.QueryElementsAsync("rooms", filter: null);
        if (!query.Success)
            return (false, $"failed to validate room numbering plan: {query.Error}");

        var rooms = query.Data ?? Array.Empty<ElementInfo>();
        var byId = rooms.ToDictionary(room => room.Id);
        var plannedIds = plan.Actions.Select(action => action.RoomId).ToHashSet();
        var conflicts = new List<string>();

        foreach (var action in plan.Actions)
        {
            if (!byId.TryGetValue(action.RoomId, out var room))
            {
                conflicts.Add($"room {action.RoomName} ({action.RoomId}) was not found in the current model");
                continue;
            }

            var current = GetElementParameter(room, action.Parameter);
            if (!string.Equals(current ?? "", action.OldNumber ?? "", StringComparison.Ordinal))
            {
                conflicts.Add(
                    $"room {action.RoomName} expected {action.Parameter} \"{action.OldNumber}\" but current value is \"{current}\"");
            }
        }

        var duplicateTargets = plan.Actions
            .GroupBy(action => action.NewNumber, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicate in duplicateTargets)
            conflicts.Add($"target room number {duplicate} appears more than once in the plan");

        foreach (var room in rooms.Where(room => !plannedIds.Contains(room.Id)))
        {
            var current = GetElementParameter(room, plan.Parameter);
            if (string.IsNullOrWhiteSpace(current))
                continue;
            var holder = plan.Actions.FirstOrDefault(action =>
                string.Equals(action.NewNumber, current, StringComparison.OrdinalIgnoreCase));
            if (holder != null)
                conflicts.Add($"target room number {holder.NewNumber} is already used by unselected room {room.Name} ({room.Id})");
        }

        var selectedCollision = rooms
            .Where(room => plannedIds.Contains(room.Id))
            .Where(room => plan.Actions.Any(action =>
                action.RoomId != room.Id &&
                string.Equals(action.NewNumber, GetElementParameter(room, plan.Parameter), StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToArray();
        foreach (var room in selectedCollision)
        {
            var current = GetElementParameter(room, plan.Parameter);
            conflicts.Add(
                $"target room number {current} is still used by selected room {room.Name} ({room.Id}); regenerate after freeing that number");
        }

        if (conflicts.Count == 0)
            return (true, "");

        var preview = string.Join("; ", conflicts.Take(5));
        if (conflicts.Count > 5)
            preview += $"; and {conflicts.Count - 5} more";
        return (false, $"room numbering plan is stale: {preview}");
    }

    private static async Task<int> ExecuteApplyMarkAssignmentAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        MarkAssignmentPlan plan;
        try
        {
            plan = MarkAssignmentPlanStore.Load(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var actionCount = plan.Actions.Count;
        if (actionCount == 0)
        {
            await output.WriteLineAsync("Plan has no mark assignment actions. Nothing to apply.");
            return 0;
        }

        if (maxChanges < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (actionCount > maxChanges)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && IsHighImpact(actionCount, highImpactThreshold) && !confirmHighImpact)
        {
            await output.WriteLineAsync(
                $"Error: plan has {actionCount} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a saved plan.");
            return 1;
        }

        if (!dryRun)
        {
            var validation = await ValidateMarkAssignmentPlanCurrentStateAsync(client, plan);
            if (!validation.Success)
            {
                await output.WriteLineAsync($"Error: {validation.Message}");
                return 1;
            }
        }

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
                return 1;
        }

        var (affected, previews, failures, rollbackActions) = await ApplyMarkAssignmentGroupsAsync(client, plan, dryRun);
        if (dryRun)
        {
            if (failures.Count > 0)
            {
                await output.WriteLineAsync($"Error: dry-run failed for {failures.Count} mark assignment group(s):");
                foreach (var failure in failures)
                    await output.WriteLineAsync(
                        $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {affected} Mark value(s) would be modified from plan.");
            foreach (var item in previews.Take(20))
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            if (previews.Count > 20)
                await output.WriteLineAsync($"  ... and {previews.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync(
            $"Applied mark assignment plan: modified {affected} Mark value(s) across {MarkNumberingPlanner.CreateGroups(plan).Count} group(s).");
        if (failures.Count > 0)
        {
            await output.WriteLineAsync($"Failed: {failures.Count} group(s):");
            foreach (var failure in failures)
                await output.WriteLineAsync(
                    $"  - {failure.Param}={failure.Value} (ids={string.Join(",", failure.ElementIds)}): {failure.Message}");
        }

        var receipt = CreatePlanReceipt(
            file,
            "mark-assignment",
            affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = failures.Count == 0;
        receipt.GroupCount = MarkNumberingPlanner.CreateGroups(plan).Count;
        receipt.ElementWrites = actionCount;
        receipt.RulePath = plan.RulePath;
        receipt.Sort = plan.Sort.ToList();
        receipt.PlanActionCount = plan.Actions.Count;
        receipt.SkippedCount = plan.Skipped.Count;
        receipt.Param = plan.Parameter;
        receipt.Value = $"{actionCount} Mark assignment change(s)";
        receipt.Preview = previews;
        receipt.Failures = failures;
        receipt.Groups = MarkNumberingPlanner.CreateGroups(plan);
        receipt.RollbackActions = rollbackActions;
        receipt.AffectedElementIds = receipt.RollbackActions.Count > 0
            ? DistinctSorted(receipt.RollbackActions.Select(action => action.ElementId))
            : DistinctSorted(plan.Actions.Select(action => action.ElementId));
        receipt.RequiresRollback = receipt.RollbackActions.Count > 0;
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return failures.Count == 0 ? 0 : 2;
    }

    private static async Task<(bool Success, string Message)> ValidateMarkAssignmentPlanCurrentStateAsync(
        RevitClient client,
        MarkAssignmentPlan plan)
    {
        var query = await client.QueryElementsAsync(plan.Category, filter: null);
        if (!query.Success)
            return (false, $"failed to validate mark assignment plan: {query.Error}");

        var elements = query.Data ?? Array.Empty<ElementInfo>();
        var byId = elements.ToDictionary(element => element.Id);
        var plannedIds = plan.Actions.Select(action => action.ElementId).ToHashSet();
        var conflicts = new List<string>();

        foreach (var action in plan.Actions)
        {
            if (!byId.TryGetValue(action.ElementId, out var element))
            {
                conflicts.Add($"element {action.ElementName} ({action.ElementId}) was not found in the current model");
                continue;
            }

            var current = MarkNumberingPlanner.GetParameter(element, action.Parameter);
            if (!string.Equals(current ?? "", action.OldMark ?? "", StringComparison.Ordinal))
            {
                conflicts.Add(
                    $"element {action.ElementName} expected {action.Parameter} \"{action.OldMark}\" but current value is \"{current}\"");
            }
        }

        var duplicateTargets = plan.Actions
            .GroupBy(action => action.NewMark, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicate in duplicateTargets)
            conflicts.Add($"target Mark {duplicate} appears more than once in the plan");

        foreach (var element in elements.Where(element => !plannedIds.Contains(element.Id)))
        {
            var current = MarkNumberingPlanner.GetParameter(element, plan.Parameter);
            if (string.IsNullOrWhiteSpace(current))
                continue;
            var holder = plan.Actions.FirstOrDefault(action =>
                string.Equals(action.NewMark, current, StringComparison.OrdinalIgnoreCase));
            if (holder != null)
                conflicts.Add($"target Mark {holder.NewMark} is already used by unselected element {element.Name} ({element.Id})");
        }

        var selectedCollision = elements
            .Where(element => plannedIds.Contains(element.Id))
            .Where(element => plan.Actions.Any(action =>
                action.ElementId != element.Id &&
                string.Equals(action.NewMark, MarkNumberingPlanner.GetParameter(element, plan.Parameter), StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToArray();
        foreach (var element in selectedCollision)
        {
            var current = MarkNumberingPlanner.GetParameter(element, plan.Parameter);
            conflicts.Add(
                $"target Mark {current} is still used by selected element {element.Name} ({element.Id}); regenerate after freeing that Mark");
        }

        if (conflicts.Count == 0)
            return (true, "");

        var preview = string.Join("; ", conflicts.Take(5));
        if (conflicts.Count > 5)
            preview += $"; and {conflicts.Count - 5} more";
        return (false, $"mark assignment plan is stale: {preview}");
    }

    private static async Task<int> ExecuteApplyLinkRepairAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        LinksCommand.LinkRepairPlan plan;
        try
        {
            plan = LoadSchemaPlan<LinksCommand.LinkRepairPlan>(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var blockingIssues = plan.Issues
            .Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (blockingIssues.Length > 0)
        {
            await output.WriteLineAsync($"Error: link repair plan has {blockingIssues.Length} blocking issue(s); regenerate or fix the map before apply.");
            return 1;
        }

        var safety = ValidatePlanApplySafety("link repair", plan.Actions.Count, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact);
        if (!safety.Success)
        {
            await output.WriteLineAsync(safety.Message);
            return safety.ExitCode;
        }

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
                return 1;
        }

        var response = await client.ApplyLinkRepairAsync(CreateLinkRepairRequest(plan, dryRun));
        if (!response.Success || response.Data == null)
        {
            await output.WriteLineAsync($"Error: {response.Error ?? "link repair apply failed"}");
            return 1;
        }

        var data = response.Data;
        if (dryRun)
        {
            if (data.Failures.Count > 0)
            {
                await WriteCoordinationFailuresAsync(output, "link repair dry-run", data.Failures);
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {data.Affected} link repair action(s) would be applied from plan.");
            foreach (var item in data.Preview.Take(20))
                await output.WriteLineAsync($"  [{item.LinkTypeId ?? item.LinkId}] {item.LinkName}: path \"{item.OldPath}\" -> \"{item.NewPath}\", loaded {item.OldLoaded} -> {item.NewLoaded}");
            if (data.Preview.Count > 20)
                await output.WriteLineAsync($"  ... and {data.Preview.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync($"Applied link repair plan: modified {data.Affected} link type(s).");
        if (data.Failures.Count > 0)
            await WriteCoordinationFailuresAsync(output, "link repair apply", data.Failures);

        var receipt = CreatePlanReceipt(
            file,
            "link-repair",
            data.Affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = data.Failures.Count == 0;
        receipt.GroupCount = data.Preview.Count;
        receipt.ElementWrites = plan.Actions.Count;
        receipt.Param = "link path/load";
        receipt.Value = $"{data.Preview.Count} link repair action(s)";
        receipt.Failures = ConvertCoordinationFailures(data.Failures);
        receipt.LinkRepairActions = data.Preview.Select(ToReceiptLinkRepairAction).ToList();
        receipt.AffectedElementIds = DistinctSorted(receipt.LinkRepairActions.Select(action => action.LinkTypeId ?? action.LinkId));
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return data.Failures.Count == 0 ? 0 : 2;
    }

    private static async Task<int> ExecuteApplyModelMapFixAsync(
        RevitClient client,
        string file,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact,
        TextWriter output)
    {
        ModelCommand.ModelMapFixPlan plan;
        try
        {
            plan = LoadSchemaPlan<ModelCommand.ModelMapFixPlan>(file);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var blockingIssues = plan.Issues
            .Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (blockingIssues.Length > 0)
        {
            await output.WriteLineAsync($"Error: model map-fix plan has {blockingIssues.Length} blocking issue(s); regenerate or fix mapping rules before apply.");
            return 1;
        }

        if (plan.Actions.Any(action => !action.CanWrite))
        {
            await output.WriteLineAsync("Error: model map-fix plan contains blocked actions; regenerate after resolving unwritable targets.");
            return 1;
        }

        var safety = ValidatePlanApplySafety("model map-fix", plan.Actions.Count, yes, dryRun, maxChanges, highImpactThreshold, confirmHighImpact);
        if (!safety.Success)
        {
            await output.WriteLineAsync(safety.Message);
            return safety.ExitCode;
        }

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
                return 1;
        }

        var response = await client.ApplyModelMapFixAsync(CreateModelMapFixRequest(plan, dryRun));
        if (!response.Success || response.Data == null)
        {
            await output.WriteLineAsync($"Error: {response.Error ?? "model map-fix apply failed"}");
            return 1;
        }

        var data = response.Data;
        if (dryRun)
        {
            if (data.Failures.Count > 0)
            {
                await WriteCoordinationFailuresAsync(output, "model map-fix dry-run", data.Failures);
                return 1;
            }

            await output.WriteLineAsync($"Dry run: {data.Affected} model map value(s) would be modified from plan.");
            foreach (var item in data.Preview.Take(20))
                await output.WriteLineAsync($"  [{item.ElementId}] {item.ElementName}: {item.Field} \"{item.OldValue}\" -> \"{item.NewValue}\"");
            if (data.Preview.Count > 20)
                await output.WriteLineAsync($"  ... and {data.Preview.Count - 20} more.");
            return 0;
        }

        await output.WriteLineAsync($"Applied model map-fix plan: modified {data.Affected} value(s).");
        if (data.Failures.Count > 0)
            await WriteCoordinationFailuresAsync(output, "model map-fix apply", data.Failures);

        var receipt = CreatePlanReceipt(
            file,
            "model-map-fix",
            data.Affected,
            maxChanges,
            highImpactThreshold: highImpactThreshold,
            confirmHighImpact: confirmHighImpact,
            metadata: metadata);
        receipt.Success = data.Failures.Count == 0;
        receipt.GroupCount = data.Preview.Count;
        receipt.ElementWrites = plan.Actions.Count;
        receipt.Param = "workset/phase mapping";
        receipt.Value = $"{data.Preview.Count} model map action(s)";
        receipt.Failures = ConvertCoordinationFailures(data.Failures);
        receipt.ModelMapActions = data.Preview.Select(ToReceiptModelMapAction).ToList();
        receipt.AffectedElementIds = DistinctSorted(receipt.ModelMapActions.Select(action => action.ElementId));
        var receiptPath = SetPlanFileStore.SaveReceipt(file, receipt);
        await output.WriteLineAsync($"Receipt saved to {receiptPath}");
        return data.Failures.Count == 0 ? 0 : 2;
    }

    private static string GetElementParameter(ElementInfo element, string parameter)
    {
        var key = element.Parameters.Keys.FirstOrDefault(candidate =>
            string.Equals(candidate, parameter, StringComparison.OrdinalIgnoreCase));
        return key == null ? "" : element.Parameters[key] ?? "";
    }

    private static string ValidateSheetIssueModelFingerprint(
        SheetIssueModelFingerprint fingerprint,
        ModelSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(fingerprint.FileHash) &&
            !string.IsNullOrWhiteSpace(snapshot.Model.FileHash) &&
            !string.Equals(fingerprint.FileHash, snapshot.Model.FileHash, StringComparison.OrdinalIgnoreCase))
        {
            return $"sheet issue plan targets model hash {fingerprint.FileHash}, but current model hash is {snapshot.Model.FileHash}.";
        }

        if (!string.IsNullOrWhiteSpace(fingerprint.DocumentPath) &&
            !string.IsNullOrWhiteSpace(snapshot.Revit.DocumentPath) &&
            !string.Equals(fingerprint.DocumentPath, snapshot.Revit.DocumentPath, StringComparison.OrdinalIgnoreCase))
        {
            return $"sheet issue plan targets document path {fingerprint.DocumentPath}, but current document path is {snapshot.Revit.DocumentPath}.";
        }

        if (!string.IsNullOrWhiteSpace(fingerprint.Document) &&
            !string.IsNullOrWhiteSpace(snapshot.Revit.Document) &&
            !string.Equals(fingerprint.Document, snapshot.Revit.Document, StringComparison.OrdinalIgnoreCase))
        {
            return $"sheet issue plan targets document {fingerprint.Document}, but current document is {snapshot.Revit.Document}.";
        }

        return "";
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

        PlanReceiptMetadata? metadata = null;
        if (!dryRun)
        {
            metadata = await RequireReceiptMetadataAsync(client, output);
            if (metadata == null)
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

        var metadata = CreateReceiptMetadata(snapshotResult.Data);
        if (!HasReceiptDocumentIdentity(metadata))
        {
            await output.WriteLineAsync("Error: failed to capture receipt model identity: baseline snapshot does not include a model path or document name.");
            return 1;
        }

        try
        {
            var baselineDir = Path.GetDirectoryName(Path.GetFullPath(baselinePath));
            if (!string.IsNullOrWhiteSpace(baselineDir))
                Directory.CreateDirectory(baselineDir);

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
                JsonSerializer.Serialize(journal, TerminalJsonOptions.Pretty),
                default);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to write fix journal: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Applied fix plan: modified {modified} element parameter(s).");
        await output.WriteLineAsync($"Rollback: revitcli rollback {baselinePath} --yes");
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
            PlanHash = ComputeSha256Hex(file),
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

    private static string ComputeSha256Hex(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task<PlanReceiptMetadata?> RequireReceiptMetadataAsync(
        RevitClient client,
        TextWriter output)
    {
        ApiResponse<StatusInfo>? status;
        try
        {
            status = await client.GetStatusAsync();
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to capture receipt model identity: {ex.Message}");
            return null;
        }

        if (status == null || !status.Success || status.Data == null)
        {
            await output.WriteLineAsync($"Error: failed to capture receipt model identity: {status?.Error ?? "status unavailable"}");
            return null;
        }

        var metadata = CreateReceiptMetadata(status.Data);
        if (!HasReceiptDocumentIdentity(metadata))
        {
            await output.WriteLineAsync("Error: failed to capture receipt model identity: status did not include a model path or document name.");
            return null;
        }

        return metadata;
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

    private static bool HasReceiptDocumentIdentity(PlanReceiptMetadata? metadata) =>
        metadata != null &&
        (!string.IsNullOrWhiteSpace(metadata.ModelPath) ||
         !string.IsNullOrWhiteSpace(metadata.DocumentName));

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
                OldValue = item.OldValue ?? string.Empty,
                NewValue = item.NewValue,
                Source = source
            })
            .ToList();
    }

    private static List<SheetIssueApplyGroup> CreateSheetIssueGroups(SheetIssuePlan plan)
    {
        return plan.Actions
            .Where(action => action.SheetId > 0 && !string.IsNullOrWhiteSpace(action.Parameter))
            .GroupBy(
                action => new { action.Parameter, action.NewValue },
                (key, actions) => new SheetIssueApplyGroup(
                    key.Parameter,
                    key.NewValue,
                    DistinctSorted(actions.Select(action => action.SheetId))))
            .OrderBy(group => group.Param, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SheetIssueApplyGroup> CreateSheetRenumberGroups(SheetRenumberPlan plan)
    {
        return plan.Actions
            .Where(action => action.SheetId > 0 && !string.IsNullOrWhiteSpace(action.Parameter))
            .GroupBy(
                action => new { action.Parameter, Value = action.NewNumber },
                (key, actions) => new SheetIssueApplyGroup(
                    key.Parameter,
                    key.Value,
                    DistinctSorted(actions.Select(action => action.SheetId))))
            .OrderBy(group => group.Param, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SheetIssueApplyGroup> CreateRoomNumberingGroups(RoomNumberingPlan plan)
    {
        return RoomNumberingPlanner.CreateGroups(plan)
            .Select(group => new SheetIssueApplyGroup(group.Param, group.Value, group.ElementIds))
            .ToList();
    }

    private static List<SheetIssueApplyGroup> CreateMarkAssignmentGroups(MarkAssignmentPlan plan)
    {
        return MarkNumberingPlanner.CreateGroups(plan)
            .Select(group => new SheetIssueApplyGroup(group.Param, group.Value, group.ElementIds))
            .ToList();
    }

    private static string QuoteArgument(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
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
            case SchemaPlanSummary schema:
                lines.Add($"- Operation: {InlineCode(schema.Operation)}");
                if (!string.IsNullOrWhiteSpace(schema.Source))
                    lines.Add($"- Source: {InlineCode(schema.Source)}");
                if (!string.IsNullOrWhiteSpace(schema.Target))
                    lines.Add($"- Target: {InlineCode(schema.Target)}");
                lines.Add($"- Actions: {schema.ActionCount}");
                if (schema.IssueCount > 0)
                    lines.Add($"- Issues: {schema.IssueCount}");
                if (schema.SpecCount > 0)
                    lines.Add($"- Specs: {schema.SpecCount}");
                if (schema.CandidateCount > 0)
                    lines.Add($"- Candidates: {schema.CandidateCount}");
                if (schema.SourceCount > 0)
                    lines.Add($"- Sources: {schema.SourceCount}");
                if (schema.ExistingCount > 0)
                    lines.Add($"- Existing: {schema.ExistingCount}");
                if (schema.BaselineCount > 0)
                    lines.Add($"- Baselines: {schema.BaselineCount}");
                if (schema.PlacedOnSheetCount > 0)
                    lines.Add($"- Placed on sheet: {schema.PlacedOnSheetCount}");
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
            case IReadOnlyList<SchemaPlanPreviewItem> items when items.Count > 0:
                foreach (var item in items.Take(10))
                {
                    var id = item.Id == 0
                        ? "-"
                        : item.Id.ToString(CultureInfo.InvariantCulture);
                    lines.Add(
                        $"- {InlineCode(item.Action)} {EscapeMarkdownText(item.Name)} [{InlineCode(id)}] -> {InlineCode(item.Target)}");
                }
                if (items.Count > 10)
                    lines.Add($"- ... and {items.Count - 10} more action(s).");
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

    private static string EscapeTableCell(string? value) =>
        EscapeMarkdownText(value).Replace("|", "\\|", StringComparison.Ordinal);

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

    private static async Task<(
        int Affected,
        List<SetPreviewItem> Previews,
        List<PlanApplyFailure> Failures,
        List<PlanReceiptRollbackAction> RollbackActions)> ApplySheetIssueGroupsAsync(
        RevitClient client,
        SheetIssuePlan plan,
        bool dryRun)
    {
        var failures = new List<PlanApplyFailure>();
        var previews = new List<SetPreviewItem>();
        var rollbackActions = new List<PlanReceiptRollbackAction>();
        var affected = 0;

        foreach (var group in CreateSheetIssueGroups(plan))
        {
            var response = await client.SetParameterAsync(new SetRequest
            {
                ElementIds = group.ElementIds,
                Param = group.Param,
                Value = group.Value,
                DryRun = dryRun
            });

            if (!response.Success)
            {
                failures.Add(new PlanApplyFailure
                {
                    Param = group.Param,
                    Value = group.Value,
                    ElementIds = group.ElementIds,
                    Message = response.Error ?? "unknown"
                });
                continue;
            }

            var responsePreviews = response.Data?.Preview ?? new List<SetPreviewItem>();
            affected += response.Data?.Affected ?? 0;
            previews.AddRange(responsePreviews);
            if (!dryRun)
                rollbackActions.AddRange(CreateRollbackActions(group.Param, responsePreviews, "sheet-issue"));
        }

        return (affected, previews, failures, rollbackActions);
    }

    private static async Task<(
        int Affected,
        List<SetPreviewItem> Previews,
        List<PlanApplyFailure> Failures,
        List<PlanReceiptRollbackAction> RollbackActions)> ApplySheetRenumberGroupsAsync(
        RevitClient client,
        SheetRenumberPlan plan,
        bool dryRun)
    {
        var failures = new List<PlanApplyFailure>();
        var previews = new List<SetPreviewItem>();
        var rollbackActions = new List<PlanReceiptRollbackAction>();
        var affected = 0;

        foreach (var group in CreateSheetRenumberGroups(plan))
        {
            var response = await client.SetParameterAsync(new SetRequest
            {
                ElementIds = group.ElementIds,
                Param = group.Param,
                Value = group.Value,
                DryRun = dryRun
            });

            if (!response.Success)
            {
                failures.Add(new PlanApplyFailure
                {
                    Param = group.Param,
                    Value = group.Value,
                    ElementIds = group.ElementIds,
                    Message = response.Error ?? "unknown"
                });
                continue;
            }

            var responsePreviews = response.Data?.Preview ?? new List<SetPreviewItem>();
            affected += response.Data?.Affected ?? 0;
            previews.AddRange(responsePreviews);
            if (!dryRun)
                rollbackActions.AddRange(CreateRollbackActions(group.Param, responsePreviews, "sheet-renumber"));
        }

        return (affected, previews, failures, rollbackActions);
    }

    private static async Task<(
        int Affected,
        List<SetPreviewItem> Previews,
        List<PlanApplyFailure> Failures,
        List<PlanReceiptRollbackAction> RollbackActions)> ApplyRoomNumberingGroupsAsync(
        RevitClient client,
        RoomNumberingPlan plan,
        bool dryRun)
    {
        var failures = new List<PlanApplyFailure>();
        var previews = new List<SetPreviewItem>();
        var rollbackActions = new List<PlanReceiptRollbackAction>();
        var affected = 0;

        foreach (var group in CreateRoomNumberingGroups(plan))
        {
            var response = await client.SetParameterAsync(new SetRequest
            {
                ElementIds = group.ElementIds,
                Param = group.Param,
                Value = group.Value,
                DryRun = dryRun
            });

            if (!response.Success)
            {
                failures.Add(new PlanApplyFailure
                {
                    Param = group.Param,
                    Value = group.Value,
                    ElementIds = group.ElementIds,
                    Message = response.Error ?? "unknown"
                });
                continue;
            }

            var responsePreviews = response.Data?.Preview ?? new List<SetPreviewItem>();
            affected += response.Data?.Affected ?? 0;
            previews.AddRange(responsePreviews);
            if (!dryRun)
                rollbackActions.AddRange(CreateRollbackActions(group.Param, responsePreviews, "room-numbering"));
        }

        return (affected, previews, failures, rollbackActions);
    }

    private static async Task<(
        int Affected,
        List<SetPreviewItem> Previews,
        List<PlanApplyFailure> Failures,
        List<PlanReceiptRollbackAction> RollbackActions)> ApplyMarkAssignmentGroupsAsync(
        RevitClient client,
        MarkAssignmentPlan plan,
        bool dryRun)
    {
        var failures = new List<PlanApplyFailure>();
        var previews = new List<SetPreviewItem>();
        var rollbackActions = new List<PlanReceiptRollbackAction>();
        var affected = 0;

        foreach (var group in CreateMarkAssignmentGroups(plan))
        {
            var response = await client.SetParameterAsync(new SetRequest
            {
                ElementIds = group.ElementIds,
                Param = group.Param,
                Value = group.Value,
                DryRun = dryRun
            });

            if (!response.Success)
            {
                failures.Add(new PlanApplyFailure
                {
                    Param = group.Param,
                    Value = group.Value,
                    ElementIds = group.ElementIds,
                    Message = response.Error ?? "unknown"
                });
                continue;
            }

            var responsePreviews = response.Data?.Preview ?? new List<SetPreviewItem>();
            affected += response.Data?.Affected ?? 0;
            previews.AddRange(responsePreviews);
            if (!dryRun)
                rollbackActions.AddRange(CreateRollbackActions(group.Param, responsePreviews, "mark-assignment"));
        }

        return (affected, previews, failures, rollbackActions);
    }

    private static T LoadSchemaPlan<T>(string file)
    {
        var json = File.ReadAllText(file);
        return JsonSerializer.Deserialize<T>(json, SchemaPlanReadOptions)
            ?? throw new InvalidOperationException("Plan file is empty or invalid JSON.");
    }

    private static (bool Success, string Message, int ExitCode) ValidatePlanApplySafety(
        string planName,
        int actionCount,
        bool yes,
        bool dryRun,
        int maxChanges,
        int? highImpactThreshold,
        bool confirmHighImpact)
    {
        if (actionCount == 0)
            return (false, $"Plan has no {planName} actions. Nothing to apply.", 0);
        if (maxChanges < 1)
            return (false, "Error: --max-changes must be at least 1.", 1);
        if (actionCount > maxChanges)
            return (false, $"Error: plan has {actionCount} change(s), exceeds --max-changes {maxChanges}.", 1);
        if (!dryRun && IsHighImpact(actionCount, highImpactThreshold) && !confirmHighImpact)
            return (false, $"Error: plan has {actionCount} change(s), at or above high-impact threshold {highImpactThreshold!.Value}. Re-run with --confirm-high-impact after review.", 1);
        if (!dryRun && !yes)
            return (false, "Error: use --yes to apply a saved plan.", 1);
        return (true, "", 0);
    }

    private static LinkRepairRequest CreateLinkRepairRequest(LinksCommand.LinkRepairPlan plan, bool dryRun) =>
        new()
        {
            DryRun = dryRun,
            Actions = plan.Actions.Select(action => new LinkRepairOperation
            {
                LinkId = action.LinkId,
                LinkTypeId = action.LinkTypeId,
                LinkName = action.LinkName,
                TypeName = action.TypeName,
                OldPath = action.OldPath,
                NewPath = action.NewPath,
                OldLoaded = action.OldLoaded,
                NewLoaded = action.NewLoaded,
                OldPathExists = action.OldPathExists,
                NewPathExists = action.NewPathExists,
                OldPathLastWriteTimeUtc = action.OldPathLastWriteTimeUtc,
                NewPathLastWriteTimeUtc = action.NewPathLastWriteTimeUtc,
                OldPathSizeBytes = action.OldPathSizeBytes,
                NewPathSizeBytes = action.NewPathSizeBytes
            }).ToList()
        };

    private static ModelMapFixRequest CreateModelMapFixRequest(ModelCommand.ModelMapFixPlan plan, bool dryRun) =>
        new()
        {
            DryRun = dryRun,
            Actions = plan.Actions.Select(action => new ModelMapFixOperation
            {
                ElementId = action.ElementId,
                ElementName = action.ElementName,
                Category = action.Category,
                Field = action.Field,
                OldValue = action.OldValue,
                NewValue = action.NewValue
            }).ToList()
        };

    private static PlanReceiptLinkRepairAction ToReceiptLinkRepairAction(LinkRepairOperation action) =>
        new()
        {
            LinkId = action.LinkId,
            LinkTypeId = action.LinkTypeId,
            LinkName = action.LinkName,
            TypeName = action.TypeName,
            OldPath = action.OldPath,
            NewPath = action.NewPath,
            OldLoaded = action.OldLoaded,
            NewLoaded = action.NewLoaded,
            OldPathExists = action.OldPathExists,
            NewPathExists = action.NewPathExists,
            OldPathLastWriteTimeUtc = action.OldPathLastWriteTimeUtc,
            NewPathLastWriteTimeUtc = action.NewPathLastWriteTimeUtc,
            OldPathSizeBytes = action.OldPathSizeBytes,
            NewPathSizeBytes = action.NewPathSizeBytes
        };

    private static PlanReceiptModelMapAction ToReceiptModelMapAction(ModelMapFixOperation action) =>
        new()
        {
            ElementId = action.ElementId,
            ElementName = action.ElementName,
            Category = action.Category,
            Field = action.Field,
            OldValue = action.OldValue,
            NewValue = action.NewValue
        };

    private static List<PlanApplyFailure> ConvertCoordinationFailures(IEnumerable<CoordinationRepairFailure> failures) =>
        failures.Select(failure => new PlanApplyFailure
        {
            Param = failure.Code,
            Value = failure.Name,
            ElementIds = failure.Id > 0 ? new List<long> { failure.Id } : new List<long>(),
            Message = failure.Message
        }).ToList();

    private static async Task WriteCoordinationFailuresAsync(
        TextWriter output,
        string label,
        IEnumerable<CoordinationRepairFailure> failures)
    {
        var items = failures.ToArray();
        await output.WriteLineAsync($"Error: {label} failed for {items.Length} action(s):");
        foreach (var failure in items)
            await output.WriteLineAsync($"  - [{failure.Id}] {failure.Name}: {failure.Message}");
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

    private sealed record SchemaPlanSummary(
        string Operation,
        string Source,
        string Target,
        int SpecCount,
        int CandidateCount,
        int SourceCount,
        int ExistingCount,
        int ActionCount,
        int IssueCount,
        int BaselineCount,
        int PlacedOnSheetCount);

    private sealed record SchemaPlanPreviewItem(
        long Id,
        string Name,
        string Action,
        string Target);

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

    private sealed record SheetIssueApplyGroup(
        string Param,
        string Value,
        List<long> ElementIds);
}
