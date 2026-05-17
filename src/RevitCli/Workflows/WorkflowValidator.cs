using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCli.Workflows;

public static class WorkflowValidator
{
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
        else if (WorkflowCommandLine.ContainsShellOperator(words))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                $"{prefix}.run",
                "workflow steps may not use shell operators, pipes, redirects, command substitution, or backticks."));
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
                WorkflowValidationSeverity.Warning,
                $"{prefix}.requiresApproval",
                "mutating workflow steps should declare requiresApproval: true."));
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
            return !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        }

        if (IsAny(command, "schedule") && words.Count >= 3 && IsAny(words[2], "create"))
        {
            return true;
        }

        if (IsAny(command, "history") && words.Count >= 3 && IsAny(words[2], "capture", "prune"))
        {
            return !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        }

        if (IsAny(command, "journal") && words.Count >= 3 && IsAny(words[2], "sign"))
        {
            return true;
        }

        if (IsAny(command, "plan") && words.Count >= 3 && IsAny(words[2], "apply"))
        {
            return !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        }

        if (IsAny(command, "deliverables") && words.Count >= 3 && IsAny(words[2], "bundle"))
        {
            return !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        }

        if (IsAny(command, "standards") && words.Count >= 3 && IsAny(words[2], "install"))
        {
            return !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        }

        if (IsAny(command, "sheets") && words.Count >= 4 && IsAny(words[2], "index") && IsAny(words[3], "init"))
        {
            return true;
        }

        if (IsAny(command, "family") && words.Count >= 3 && IsAny(words[2], "purge"))
        {
            return words.Contains("--apply", StringComparer.OrdinalIgnoreCase) &&
                !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        }

        if (IsAny(command, "workflow") && words.Count >= 3 && IsAny(words[2], "init"))
        {
            return true;
        }

        if (IsAny(command, "workflow") && words.Count >= 3 && IsAny(words[2], "run"))
        {
            return !words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
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
                "snapshot",
                "diff",
                "ci",
                "workflow",
                "deliverables",
                "report",
                "release",
                "sheets",
                "standards"))
        {
            return true;
        }

        if (IsAny(command, "check", "config", "family", "profile") ||
            (IsAny(command, "schedule") && words.Count >= 3 && IsAny(words[2], "list", "export")) ||
            (IsAny(command, "history") && words.Count >= 3 && IsAny(words[2], "list", "diff", "trend")) ||
            (IsAny(command, "journal") && words.Count >= 3 && IsAny(words[2], "show", "stats", "review", "verify")) ||
            (IsAny(command, "plan") && words.Count >= 3 && IsAny(words[2], "show")))
        {
            return true;
        }

        return false;
    }

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

}
