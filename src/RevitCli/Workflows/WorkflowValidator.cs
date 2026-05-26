using System;
using System.Collections.Generic;
using System.Linq;
using RevitCli.Commands;

namespace RevitCli.Workflows;

public static class WorkflowValidator
{
    private static readonly HashSet<string> KnownTopLevelCommands = new(
        CliCommandCatalog.TopLevelCommandNames,
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, HashSet<string>> KnownSubcommands =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ci"] = Values("doctor"),
            ["config"] = Values("show", "set"),
            ["dashboard"] = Values("serve", "build"),
            ["deliverables"] = Values("list", "stats", "verify", "plan", "bundle"),
            ["family"] = Values("ls", "validate", "purge", "export"),
            ["history"] = Values("init", "capture", "list", "prune", "diff", "trend"),
            ["inspect"] = Values("categories", "params", "schedules", "sheets", "workflows", "plans"),
            ["issue"] = Values("preflight", "diff", "package"),
            ["journal"] = Values("show", "stats", "review", "sign", "verify"),
            ["ledger"] = Values("append", "replay", "query", "validate", "stats", "timeline"),
            ["links"] = Values("audit", "repair"),
            ["marks"] = Values("assign", "verify"),
            ["model"] = Values("map-check", "map-fix"),
            ["plan"] = Values("show", "apply"),
            ["profile"] = Values("validate", "show", "diff", "install", "simulate"),
            ["release"] = Values("verify"),
            ["report"] = Values("weekly", "knowledge"),
            ["rooms"] = Values("renumber"),
            ["schedule"] = Values("list", "export", "create"),
            ["sheets"] = Values("verify", "issue-meta", "renumber", "index"),
            ["standards"] = Values("install", "validate"),
            ["schedules"] = Values("ensure", "batch-export", "compare"),
            ["views"] = Values("audit", "template-apply", "clone-set"),
            ["workbench"] = Values(
                "contract",
                "verify",
                "receipts",
                "paths",
                "exits",
                "extensions",
                "outputs",
                "safeguards",
                "project",
                "handoff"),
            ["workflow"] = Values("init", "validate", "simulate", "review", "registry", "run", "suggest", "examples", "receipts"),
        };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> KnownNestedSubcommands =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sheets index"] = Values("init", "show"),
        };

    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "read-only",
        "dry-run",
        "mutating",
    };

    public static IReadOnlyList<WorkflowValidationIssue> Validate(LoadedWorkflow loaded)
    {
        var issues = new List<WorkflowValidationIssue>();
        var workflow = loaded.Workflow;

        if (workflow.Version != 1)
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                "version",
                $"workflow version must be 1, got {workflow.Version}."));
        }

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                "name",
                "workflow name is required."));
        }

        if (workflow.Steps.Count == 0)
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                "steps",
                "workflow must contain at least one step."));
        }

        for (var i = 0; i < workflow.Steps.Count; i++)
        {
            ValidateStep(workflow.Steps[i], i, issues);
        }

        return issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToList();
    }

    public static WorkflowSimulationReport Simulate(LoadedWorkflow loaded)
    {
        var issues = Validate(loaded).ToList();
        var workflow = loaded.Workflow;
        var report = new WorkflowSimulationReport
        {
            Path = loaded.Path,
            Name = workflow.Name,
            Description = workflow.Description,
            StepCount = workflow.Steps.Count,
            CanRun = issues.All(issue => issue.Severity != WorkflowValidationSeverity.Error),
            Issues = issues,
        };

        for (var i = 0; i < workflow.Steps.Count; i++)
        {
            var step = workflow.Steps[i];
            var mode = string.IsNullOrWhiteSpace(step.Mode) ? "(missing)" : step.Mode;
            report.Steps.Add(new WorkflowStepSimulation(
                i + 1,
                string.IsNullOrWhiteSpace(step.Name) ? $"step {i + 1}" : step.Name!,
                mode,
                step.Run,
                step.RequiresApproval));

            if (!report.ModeCounts.TryGetValue(mode, out var count))
            {
                count = 0;
            }

            report.ModeCounts[mode] = count + 1;
        }

        return report;
    }

    private static void ValidateStep(
        WorkflowStep step,
        int index,
        List<WorkflowValidationIssue> issues)
    {
        var prefix = $"steps[{index}]";

        if (step == null)
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                prefix,
                "step must be an object."));
            return;
        }

        if (string.IsNullOrWhiteSpace(step.Run))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.run",
                "step run command is required."));
        }
        else if (!TryTokenize(step.Run, $"{prefix}.run", issues, out var words))
        {
            return;
        }
        else if (!StartsWithRevitCli(words))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.run",
                "workflow steps may only call existing RevitCli commands and must start with 'revitcli'."));
        }
        else if (WorkflowCommandLine.ContainsShellOperator(step.Run))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.run",
                "workflow steps may not use shell operators, pipes, redirects, command substitution, or backticks."));
        }
        else if (!HasKnownCommandShape(words, out var commandError))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.run",
                commandError));
        }
        else
        {
            ValidateCommandMode(step, words, prefix, issues);
        }

        if (string.IsNullOrWhiteSpace(step.Mode))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.mode",
                "step mode is required: read-only, dry-run, or mutating."));
        }
        else if (!ValidModes.Contains(step.Mode))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.mode",
                $"step mode must be read-only, dry-run, or mutating, got '{step.Mode}'."));
        }

        if (string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase) &&
            !step.RequiresApproval)
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.requiresApproval",
                "mutating workflow steps must declare requiresApproval: true."));
        }

        if (string.Equals(step.Mode, "dry-run", StringComparison.OrdinalIgnoreCase) &&
            !step.Run.Contains("--dry-run", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Warning,
                $"{prefix}.run",
                "dry-run steps should include --dry-run so simulation and real execution match."));
        }
    }

    private static void ValidateCommandMode(
        WorkflowStep step,
        IReadOnlyList<string> words,
        string prefix,
        List<WorkflowValidationIssue> issues)
    {
        var commandName = words.Count >= 2 ? words[1] : null;
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        if (!string.Equals(step.Mode, "read-only", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (LooksWritable(words))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Warning,
                $"{prefix}.mode",
                $"command '{string.Join(" ", words.Skip(1).Take(2))}' can write; declare dry-run or mutating unless this workflow is intentionally local-only."));
        }
        else if (!LooksReadOnly(words))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Warning,
                $"{prefix}.mode",
                $"command '{commandName}' is not known to be read-only; use dry-run or mutating if it can write."));
        }
    }

    private static bool StartsWithRevitCli(IReadOnlyList<string> words) =>
        words.Count > 0 && string.Equals(words[0], "revitcli", StringComparison.OrdinalIgnoreCase);

    private static bool HasKnownCommandShape(IReadOnlyList<string> words, out string error)
    {
        error = "";
        if (words.Count < 2)
        {
            error = "workflow steps must include a RevitCli command after 'revitcli'.";
            return false;
        }

        if (!KnownTopLevelCommands.Contains(words[1]))
        {
            error = $"unknown RevitCli command '{words[1]}'; workflows may only call existing CLI commands.";
            return false;
        }

        if (words.Count >= 3 &&
            !words[2].StartsWith("-", StringComparison.Ordinal) &&
            KnownSubcommands.TryGetValue(words[1], out var knownSubcommands) &&
            !knownSubcommands.Contains(words[2]))
        {
            error = $"unknown RevitCli command '{words[1]} {words[2]}'; workflows may only call existing CLI commands.";
            return false;
        }

        if (words.Count >= 3 &&
            !words[2].StartsWith("-", StringComparison.Ordinal) &&
            KnownNestedSubcommands.TryGetValue($"{words[1]} {words[2]}", out var knownNestedSubcommands))
        {
            if (words.Count < 4 || words[3].StartsWith("-", StringComparison.Ordinal))
            {
                error = $"RevitCli command '{words[1]} {words[2]}' requires one of: {string.Join(", ", knownNestedSubcommands)}.";
                return false;
            }

            if (!knownNestedSubcommands.Contains(words[3]))
            {
                error = $"unknown RevitCli command '{words[1]} {words[2]} {words[3]}'; workflows may only call existing CLI commands.";
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> Values(params string[] values) =>
        new(values, StringComparer.OrdinalIgnoreCase);

    internal static bool CommandLooksWriteCapable(string run)
    {
        if (string.IsNullOrWhiteSpace(run))
            return false;

        if (!TryTokenize(run, "run", new List<WorkflowValidationIssue>(), out var words))
            return false;

        return StartsWithRevitCli(words) && LooksWritable(words);
    }

    private static bool TryTokenize(
        string run,
        string path,
        List<WorkflowValidationIssue> issues,
        out IReadOnlyList<string> words)
    {
        try
        {
            words = WorkflowCommandLine.Tokenize(run);
            return true;
        }
        catch (FormatException ex)
        {
            words = Array.Empty<string>();
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                path,
                ex.Message));
            return false;
        }
    }

    private static bool LooksWritable(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            return false;
        }

        var command = words[1];
        if (IsAny(command, "set", "import", "export", "publish", "fix", "rollback"))
        {
            return true;
        }

        if (IsAny(command, "config") && words.Count >= 3 && IsAny(words[2], "set"))
        {
            return true;
        }

        if (IsAny(command, "schedule") && words.Count >= 3 && IsAny(words[2], "export", "create"))
        {
            return true;
        }

        if (IsAny(command, "history") && words.Count >= 3 && IsAny(words[2], "init", "capture", "prune"))
        {
            return true;
        }

        if (IsAny(command, "journal") && words.Count >= 3 && IsAny(words[2], "sign"))
        {
            return true;
        }

        if (IsAny(command, "ledger") && words.Count >= 3 && IsAny(words[2], "append"))
        {
            return true;
        }

        if (IsAny(command, "ledger") &&
            words.Count >= 3 &&
            IsAny(words[2], "replay") &&
            words.Contains("--apply", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsAny(command, "plan") && words.Count >= 3 && IsAny(words[2], "apply"))
        {
            return true;
        }

        if (IsAny(command, "deliverables") && words.Count >= 3 && IsAny(words[2], "bundle"))
        {
            return true;
        }

        if (IsAny(command, "profile") && words.Count >= 3 && IsAny(words[2], "install"))
        {
            return true;
        }

        if (IsAny(command, "standards") && words.Count >= 3 && IsAny(words[2], "install"))
        {
            return true;
        }

        if (IsAny(command, "sheets") && words.Count >= 3 && IsAny(words[2], "issue-meta", "renumber"))
        {
            return true;
        }

        if (IsAny(command, "sheets") && words.Count >= 4 && IsAny(words[2], "index") && IsAny(words[3], "init"))
        {
            return true;
        }

        if (IsAny(command, "rooms") && words.Count >= 3 && IsAny(words[2], "renumber"))
        {
            return true;
        }

        if (IsAny(command, "marks") && words.Count >= 3 && IsAny(words[2], "assign"))
        {
            return true;
        }

        if (IsAny(command, "schedules") && words.Count >= 3 && IsAny(words[2], "ensure", "batch-export"))
        {
            return true;
        }

        if (IsAny(command, "views") && words.Count >= 3 && IsAny(words[2], "template-apply", "clone-set"))
        {
            return true;
        }

        if (IsAny(command, "links") && words.Count >= 3 && IsAny(words[2], "repair"))
        {
            return true;
        }

        if (IsAny(command, "model") && words.Count >= 3 && IsAny(words[2], "map-fix"))
        {
            return true;
        }

        if (IsAny(command, "family") && words.Count >= 3 && IsAny(words[2], "purge"))
        {
            return true;
        }

        if (IsAny(command, "family") && words.Count >= 3 && IsAny(words[2], "export"))
        {
            return true;
        }

        if (IsAny(command, "workflow") && words.Count >= 3 && IsAny(words[2], "init"))
        {
            return true;
        }

        if (IsAny(command, "workflow") && words.Count >= 3 && IsAny(words[2], "run"))
        {
            return true;
        }

        return false;
    }

    private static bool LooksReadOnly(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            return false;
        }

        var command = words[1];
        if (IsAny(command,
                "status",
                "doctor",
                "query",
                "audit",
                "score",
                "coverage",
                "inspect",
                "examples",
                "workbench",
                "snapshot",
                "diff",
                "ci",
                "report",
                "release",
                "ledger"))
        {
            return true;
        }

        if (IsAny(command, "check") ||
            (IsAny(command, "config") && words.Count >= 3 && IsAny(words[2], "show")) ||
            (IsAny(command, "deliverables") && words.Count >= 3 && IsAny(words[2], "list", "stats", "verify", "plan")) ||
            (IsAny(command, "family") && words.Count >= 3 && IsAny(words[2], "ls", "validate")) ||
            (IsAny(command, "profile") && words.Count >= 3 && IsAny(words[2], "validate", "show", "diff", "simulate")) ||
            (IsAny(command, "schedule") && words.Count >= 3 && IsAny(words[2], "list")) ||
            (IsAny(command, "history") && words.Count >= 3 && IsAny(words[2], "list", "diff", "trend")) ||
            (IsAny(command, "journal") && words.Count >= 3 && IsAny(words[2], "show", "stats", "review", "verify")) ||
            (IsAny(command, "links") && words.Count >= 3 && IsAny(words[2], "audit")) ||
            (IsAny(command, "marks") && words.Count >= 3 && IsAny(words[2], "verify")) ||
            (IsAny(command, "model") && words.Count >= 3 && IsAny(words[2], "map-check")) ||
            (IsAny(command, "plan") && words.Count >= 3 && IsAny(words[2], "show")) ||
            (IsAny(command, "schedules") && words.Count >= 3 && IsAny(words[2], "compare")) ||
            (IsAny(command, "sheets") && words.Count >= 3 && IsAny(words[2], "verify")) ||
            (IsAny(command, "sheets") && words.Count >= 4 && IsAny(words[2], "index") && IsAny(words[3], "show")) ||
            (IsAny(command, "standards") && words.Count >= 3 && IsAny(words[2], "validate")) ||
            (IsAny(command, "views") && words.Count >= 3 && IsAny(words[2], "audit")) ||
            (IsAny(command, "workflow") && words.Count >= 3 && IsAny(words[2], "validate", "simulate", "review", "registry", "suggest", "examples", "receipts")))
        {
            return true;
        }

        return false;
    }

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

}
