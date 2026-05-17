using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Journal;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class WorkflowCommand
{
    private static readonly (string Name, string File, string Description)[] Templates =
    {
        ("pre-issue", "pre-issue.yml", "Pre-issue checks, dry-run publish, and history capture"),
        ("weekly-health", "weekly-health.yml", "Weekly model-health snapshot, trend, and journal review"),
        ("export-package", "export-package.yml", "Export readiness, approved publish, deliverable review, and journal verify"),
        ("family-cleanup", "family-cleanup.yml", "Unused-family review, validation, and purge dry-run"),
    };

    private static readonly WorkflowAcceptanceExample[] AcceptanceExamples =
    {
        new(
            "pre-issue",
            "帮我做出图前检查，先 dry-run，不要改模型。",
            "Confirm issue readiness before any write, export, or history capture.",
            new[]
            {
                "revitcli workflow init pre-issue",
                "revitcli workflow validate .revitcli/workflows/pre-issue.yml",
                "revitcli workflow simulate .revitcli/workflows/pre-issue.yml",
                "revitcli inspect schedules --issues-only",
                "revitcli sheets verify --issues-only",
                "revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run"
            },
            new[]
            {
                "revitcli workflow run .revitcli/workflows/pre-issue.yml --yes"
            },
            new[]
            {
                "check blockers",
                "schedule export blockers",
                "sheet issues",
                "sheet-frame verify issues",
                "publish dry-run result",
                "history capture receipt when approved"
            }),
        new(
            "export-package",
            "把本次 issue 的交付包准备好，先告诉我哪些图纸能导出。",
            "Review export candidates, dry-run publish, then create a traceable handoff package.",
            new[]
            {
                "revitcli workflow init export-package",
                "revitcli inspect sheets --ready-only",
                "revitcli inspect schedules --ready-only",
                "revitcli workflow simulate .revitcli/workflows/export-package.yml",
                "revitcli workflow run .revitcli/workflows/export-package.yml --dry-run"
            },
            new[]
            {
                "revitcli workflow run .revitcli/workflows/export-package.yml --yes",
                "revitcli deliverables verify --output markdown",
                "revitcli deliverables bundle --dry-run --output markdown",
                "revitcli workflow receipts --output markdown"
            },
            new[]
            {
                "export-ready sheets",
                "publish receipt",
                "delivery manifest",
                "workflow-run-receipt.v1",
                "bundle receipt after approved packaging"
            }),
        new(
            "weekly-health",
            "生成本周模型健康报告，看趋势和最近谁改了什么。",
            "Capture/review local health, trend, diff, and journal evidence for weekly handoff.",
            new[]
            {
                "revitcli workflow init weekly-health",
                "revitcli workflow simulate .revitcli/workflows/weekly-health.yml",
                "revitcli report weekly --window 14d --output markdown",
                "revitcli journal review --output markdown"
            },
            new[]
            {
                "revitcli workflow run .revitcli/workflows/weekly-health.yml --yes"
            },
            new[]
            {
                "score trend",
                "history snapshot",
                "journal risk review",
                "weekly Markdown report"
            }),
        new(
            "family-cleanup",
            "检查模型里有没有可清理的族，先预览，不要直接 purge。",
            "Find unused/risky families and review purge candidates before any destructive cleanup.",
            new[]
            {
                "revitcli workflow init family-cleanup",
                "revitcli workflow simulate .revitcli/workflows/family-cleanup.yml",
                "revitcli family ls --unused",
                "revitcli family validate --output table",
                "revitcli family purge --dry-run --report .revitcli/reports/family-purge.json"
            },
            new[]
            {
                "revitcli workflow run .revitcli/workflows/family-cleanup.yml --yes"
            },
            new[]
            {
                "unused families",
                "family validation findings",
                "family-purge-report.v1 JSON",
                "history capture when approved"
            }),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Command Create()
    {
        var command = new Command("workflow", "Create, validate, run, and review terminal workflow YAML files");
        command.AddCommand(CreateInitCommand());
        command.AddCommand(CreateValidateCommand());
        command.AddCommand(CreateSimulateCommand());
        command.AddCommand(CreateRunCommand());
        command.AddCommand(CreateSuggestCommand());
        command.AddCommand(CreateExamplesCommand());
        command.AddCommand(CreateReceiptsCommand());
        return command;
    }

    private static Command CreateInitCommand()
    {
        var templateArg = new Argument<string?>(
            "template",
            () => null,
            $"Workflow template: {string.Join(", ", Templates.Select(template => template.Name))}, all");
        var dirOpt = new Option<string?>("--dir", "Project directory where .revitcli/workflows will be created");
        var forceOpt = new Option<bool>("--force", "Overwrite existing workflow files");

        var command = new Command("init", "Create workflow YAML from built-in templates")
        {
            templateArg,
            dirOpt,
            forceOpt,
        };

        command.SetHandler(async (string? template, string? dir, bool force) =>
        {
            Environment.ExitCode = await ExecuteInitAsync(template, dir, force, Console.Out);
        }, templateArg, dirOpt, forceOpt);

        return command;
    }

    private static Command CreateExamplesCommand()
    {
        var templateArg = new Argument<string?>(
            "template",
            () => null,
            $"Workflow template: {string.Join(", ", AcceptanceExamples.Select(example => example.Workflow))}, all");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("examples", "Show architect prompts and acceptance command paths for workflow templates")
        {
            templateArg,
            outputOpt,
        };

        command.SetHandler(async (string? template, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteExamplesAsync(template, outputFormat, Console.Out);
        }, templateArg, outputOpt);

        return command;
    }

    private static Command CreateValidateCommand()
    {
        var fileArg = new Argument<string?>(
            "file",
            () => null,
            "Workflow YAML file or directory; defaults to .revitcli/workflows/*.yml");
        var dirOpt = new Option<string?>("--dir", "Base directory for default workflow discovery");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("validate", "Validate workflow YAML without running commands")
        {
            fileArg,
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string? file, string? dir, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteValidateAsync(file, dir, outputFormat, Console.Out);
        }, fileArg, dirOpt, outputOpt);

        return command;
    }

    private static Command CreateRunCommand()
    {
        var fileArg = new Argument<string>("file", "Workflow YAML file to run");
        var dirOpt = new Option<string?>("--dir", "Base directory for relative workflow paths");
        var dryRunOpt = new Option<bool>("--dry-run", "Print the execution plan without running steps");
        var yesOpt = new Option<bool>("--yes", "Allow approved mutating steps to run");
        var continueOpt = new Option<bool>("--continue-on-error", "Continue after a step exits non-zero");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("run", "Run workflow steps after validation")
        {
            fileArg,
            dirOpt,
            dryRunOpt,
            yesOpt,
            continueOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string file,
            string? dir,
            bool dryRun,
            bool yes,
            bool continueOnError,
            string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteRunAsync(
                file,
                dir,
                dryRun,
                yes,
                continueOnError,
                outputFormat,
                Console.Out);
        }, fileArg, dirOpt, dryRunOpt, yesOpt, continueOpt, outputOpt);

        return command;
    }

    private static Command CreateSimulateCommand()
    {
        var fileArg = new Argument<string>("file", "Workflow YAML file to simulate");
        var dirOpt = new Option<string?>("--dir", "Base directory for relative workflow paths");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("simulate", "Print workflow steps and risk modes without running commands")
        {
            fileArg,
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string file, string? dir, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteSimulateAsync(file, dir, outputFormat, Console.Out);
        }, fileArg, dirOpt, outputOpt);

        return command;
    }

    private static Command CreateSuggestCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var journalOpt = new Option<string?>("--journal", "Override .revitcli/journal.jsonl path");
        var minCountOpt = new Option<int>("--min-count", () => 2, "Minimum repeated sequence count");
        var maxStepsOpt = new Option<int>("--max-steps", () => 5, "Maximum command sequence length to consider");
        var limitOpt = new Option<int>("--limit", () => 3, "Maximum suggestions to show");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | yaml");

        var command = new Command("suggest", "Suggest workflow YAML from repeated journal command sequences")
        {
            dirOpt,
            journalOpt,
            minCountOpt,
            maxStepsOpt,
            limitOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string? dir,
            string? journal,
            int minCount,
            int maxSteps,
            int limit,
            string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteSuggestAsync(
                dir,
                journal,
                minCount,
                maxSteps,
                limit,
                outputFormat,
                Console.Out);
        }, dirOpt, journalOpt, minCountOpt, maxStepsOpt, limitOpt, outputOpt);

        return command;
    }

    private static Command CreateReceiptsCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var limitOpt = new Option<int>("--limit", () => 10, "Maximum workflow receipts to show");
        var failedOnlyOpt = new Option<bool>("--failed-only", "Only show failed workflow runs");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("receipts", "Review local workflow-run receipts")
        {
            dirOpt,
            limitOpt,
            failedOnlyOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string? dir,
            int limit,
            bool failedOnly,
            string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteReceiptsAsync(dir, limit, failedOnly, outputFormat, Console.Out);
        }, dirOpt, limitOpt, failedOnlyOpt, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteValidateAsync(
        string? fileOrDirectory,
        string? baseDirectory,
        string outputFormat,
        TextWriter output)
    {
        IReadOnlyList<string> files;
        try
        {
            files = WorkflowLoader.Discover(fileOrDirectory, baseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var reports = files.Select(ValidateFile).ToList();
        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(reports, JsonOpts));
        }
        else if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderValidationMarkdown(reports));
        }
        else
        {
            await output.WriteLineAsync(RenderValidationTable(reports));
        }

        return reports.Any(report => report.Issues.Any(issue => issue.Severity == WorkflowValidationSeverity.Error))
            ? 1
            : 0;
    }

    public static async Task<int> ExecuteSuggestAsync(
        string? projectDirectory,
        string? journalPath,
        int minCount,
        int maxSteps,
        int limit,
        string outputFormat,
        TextWriter output)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var resolvedJournal = string.IsNullOrWhiteSpace(journalPath)
            ? Path.Combine(projectRoot, ".revitcli", "journal.jsonl")
            : ResolvePath(projectRoot, journalPath!);

        WorkflowSuggestionResult result;
        try
        {
            var journal = JournalReader.Read(resolvedJournal);
            result = WorkflowSuggester.Suggest(journal, minCount, maxSteps, limit);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var format = (outputFormat ?? "table").Trim().ToLowerInvariant();
        switch (format)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOpts));
                break;
            case "yaml":
                await output.WriteLineAsync(result.Suggestions.Count == 0
                    ? "# No repeated journal command sequences found."
                    : result.Suggestions[0].Yaml);
                break;
            case "table":
                await output.WriteLineAsync(RenderSuggestionTable(result));
                break;
            default:
                await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'yaml'.");
                return 1;
        }

        return 0;
    }

    public static async Task<int> ExecuteExamplesAsync(
        string? template,
        string outputFormat,
        TextWriter output)
    {
        var examples = ResolveAcceptanceExamples(template);
        if (examples.Count == 0)
        {
            await output.WriteLineAsync($"Error: unknown workflow example '{template}'.");
            await output.WriteLineAsync($"Available: {string.Join(", ", AcceptanceExamples.Select(item => item.Workflow))}, all");
            return 1;
        }

        var format = (outputFormat ?? "table").Trim().ToLowerInvariant();
        switch (format)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(examples, JsonOpts));
                return 0;
            case "markdown":
                await output.WriteLineAsync(RenderExamplesMarkdown(examples));
                return 0;
            case "table":
                await output.WriteLineAsync(RenderExamplesTable(examples));
                return 0;
            default:
                await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
                return 1;
        }
    }

    public static async Task<int> ExecuteReceiptsAsync(
        string? projectDirectory,
        int limit,
        bool failedOnly,
        string outputFormat,
        TextWriter output)
    {
        var format = (outputFormat ?? "table").Trim().ToLowerInvariant();
        if (format is not ("table" or "json" or "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (limit < 1)
        {
            await output.WriteLineAsync("Error: --limit must be at least 1.");
            return 1;
        }

        WorkflowReceiptListReport report;
        try
        {
            report = WorkflowReceiptReader.Read(projectDirectory, limit, failedOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await output.WriteLineAsync($"Error: failed to read workflow receipts: {ex.Message}");
            return 1;
        }

        switch (format)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOpts));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderReceiptsMarkdown(report));
                break;
            default:
                await output.WriteLineAsync(RenderReceiptsTable(report));
                break;
        }

        return report.Success ? 0 : 1;
    }

    public static async Task<int> ExecuteInitAsync(
        string? template,
        string? baseDirectory,
        bool force,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            await output.WriteLineAsync("Available workflow templates:");
            foreach (var item in Templates)
            {
                await output.WriteLineAsync($"  {item.Name,-15} {item.Description}");
            }

            await output.WriteLineAsync("  all             Install every workflow template");
            await output.WriteLineAsync();
            await output.WriteLineAsync("Run: revitcli workflow init <template>");
            return 0;
        }

        var selected = ResolveTemplates(template);
        if (selected.Count == 0)
        {
            await output.WriteLineAsync($"Error: unknown workflow template '{template}'.");
            await output.WriteLineAsync($"Available: {string.Join(", ", Templates.Select(item => item.Name))}, all");
            return 1;
        }

        var templatesDir = FindWorkflowTemplatesDir();
        if (templatesDir == null)
        {
            await output.WriteLineAsync("Error: workflow templates directory not found. Install RevitCli properly.");
            return 1;
        }

        var projectDir = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory!);
        var targetDir = Path.Combine(projectDir, WorkflowLoader.DefaultDirectory);
        var conflicts = selected
            .Select(item => Path.Combine(targetDir, item.File))
            .Where(File.Exists)
            .ToList();
        if (conflicts.Count > 0 && !force)
        {
            await output.WriteLineAsync(
                $"Error: workflow file already exists: {Path.GetRelativePath(projectDir, conflicts[0])}");
            await output.WriteLineAsync("Use --force to overwrite it.");
            return 1;
        }

        Directory.CreateDirectory(targetDir);
        foreach (var item in selected)
        {
            var sourcePath = Path.Combine(templatesDir, item.File);
            if (!File.Exists(sourcePath))
            {
                await output.WriteLineAsync($"Error: workflow template file not found: {sourcePath}");
                return 1;
            }

            var targetPath = Path.Combine(targetDir, item.File);
            File.Copy(sourcePath, targetPath, overwrite: force);
            await output.WriteLineAsync($"Created {Path.GetRelativePath(projectDir, targetPath)} from '{item.Name}'.");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Next steps:");
        await output.WriteLineAsync("  revitcli workflow validate");
        await output.WriteLineAsync($"  revitcli workflow simulate .revitcli/workflows/{selected[0].File}");
        await output.WriteLineAsync($"  revitcli workflow run .revitcli/workflows/{selected[0].File} --dry-run");
        return 0;
    }

    public static async Task<int> ExecuteSimulateAsync(
        string file,
        string? baseDirectory,
        string outputFormat,
        TextWriter output)
    {
        LoadedWorkflow loaded;
        try
        {
            var files = WorkflowLoader.Discover(file, baseDirectory);
            loaded = WorkflowLoader.Load(files[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var report = WorkflowValidator.Simulate(loaded);
        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOpts));
        }
        else if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderSimulationMarkdown(report));
        }
        else
        {
            await output.WriteLineAsync(RenderSimulationTable(report));
        }

        return report.CanRun ? 0 : 1;
    }

    public static async Task<int> ExecuteRunAsync(
        string file,
        string? baseDirectory,
        bool dryRun,
        bool yes,
        bool continueOnError,
        string outputFormat,
        TextWriter output,
        Func<WorkflowStepSimulation, TextWriter, Task<int>>? runner = null)
    {
        LoadedWorkflow loaded;
        try
        {
            var files = WorkflowLoader.Discover(file, baseDirectory);
            loaded = WorkflowLoader.Load(files[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var simulation = WorkflowValidator.Simulate(loaded);
        var startedAt = DateTime.UtcNow.ToString("o");
        var report = new WorkflowRunReport
        {
            Path = simulation.Path,
            Name = simulation.Name,
            Command = BuildWorkflowRunCommand(loaded.Path, dryRun, yes, continueOnError, outputFormat),
            StartedAtUtc = startedAt,
            Operator = Environment.UserName,
            Machine = Environment.MachineName,
            DryRun = dryRun,
            CanRun = simulation.CanRun,
            Issues = simulation.Issues.ToList(),
        };

        AddRunGateIssues(simulation, dryRun, yes, report.Issues);

        if (report.Issues.Any(issue => issue.Severity == WorkflowValidationSeverity.Error))
        {
            report.CanRun = false;
            report.ExitCode = 1;
            foreach (var step in simulation.Steps)
            {
                report.Steps.Add(ToRunStepResult(step, dryRun ? "planned" : "blocked", null));
            }

            CompleteRunReport(report);
            await WriteRunReportAsync(report, outputFormat, output);
            return report.ExitCode;
        }

        if (dryRun)
        {
            report.ExitCode = 0;
            foreach (var step in simulation.Steps)
            {
                report.Steps.Add(ToRunStepResult(step, "planned", null));
            }

            CompleteRunReport(report);
            await WriteRunReportAsync(report, outputFormat, output);
            return 0;
        }

        var stepRunner = runner ?? RunProcessAsync;
        foreach (var step in simulation.Steps)
        {
            var exitCode = await stepRunner(step, output);
            var status = exitCode == 0 ? "ok" : "failed";
            report.Steps.Add(ToRunStepResult(step, status, exitCode));

            if (exitCode != 0)
            {
                report.ExitCode = exitCode;
                if (!continueOnError)
                {
                    foreach (var skipped in simulation.Steps.Skip(step.Index))
                    {
                        report.Steps.Add(ToRunStepResult(skipped, "skipped", null));
                    }

                    break;
                }
            }
        }

        if (report.ExitCode == 0 && report.Steps.Any(step => step.ExitCode is > 0))
        {
            report.ExitCode = 1;
        }

        CompleteRunReport(report);
        TrySaveWorkflowRunReceipt(loaded.Path, baseDirectory, report);
        await WriteRunReportAsync(report, outputFormat, output);
        return report.ExitCode;
    }

    private static WorkflowValidationFileReport ValidateFile(string path)
    {
        try
        {
            var loaded = WorkflowLoader.Load(path);
            var issues = WorkflowValidator.Validate(loaded).ToList();
            return new WorkflowValidationFileReport
            {
                Path = loaded.Path,
                Name = loaded.Workflow.Name,
                StepCount = loaded.Workflow.Steps.Count,
                Issues = issues,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            return new WorkflowValidationFileReport
            {
                Path = Path.GetFullPath(path),
                Issues =
                {
                    new WorkflowValidationIssue(
                        WorkflowValidationSeverity.Error,
                        "file",
                        ex.Message)
                }
            };
        }
    }

    private static string RenderValidationTable(IReadOnlyList<WorkflowValidationFileReport> reports)
    {
        var writer = new StringWriter();
        writer.WriteLine("Workflow validation");
        foreach (var report in reports)
        {
            var hasErrors = report.Issues.Any(issue => issue.Severity == WorkflowValidationSeverity.Error);
            var status = hasErrors ? "FAIL" : "OK";
            var name = string.IsNullOrWhiteSpace(report.Name) ? Path.GetFileNameWithoutExtension(report.Path) : report.Name;
            writer.WriteLine($"{status} {name} ({ShortPath(report.Path)}) steps={report.StepCount}");
            foreach (var issue in report.Issues)
            {
                writer.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderValidationMarkdown(IReadOnlyList<WorkflowValidationFileReport> reports)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Workflow Validation");
        foreach (var report in reports)
        {
            var hasErrors = report.Issues.Any(issue => issue.Severity == WorkflowValidationSeverity.Error);
            var status = hasErrors ? "FAIL" : "OK";
            var name = string.IsNullOrWhiteSpace(report.Name) ? Path.GetFileNameWithoutExtension(report.Path) : report.Name;
            writer.WriteLine();
            writer.WriteLine($"## {EscapeMarkdownText(name)}");
            writer.WriteLine();
            writer.WriteLine($"- Status: `{status}`");
            writer.WriteLine($"- Path: `{EscapeInlineCode(report.Path)}`");
            writer.WriteLine($"- Steps: {report.StepCount}");
            writer.WriteLine("- Issues:");
            if (report.Issues.Count == 0)
            {
                writer.WriteLine("  - None.");
            }
            else
            {
                foreach (var issue in report.Issues)
                {
                    writer.WriteLine(
                        $"  - `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
                }
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderSuggestionTable(WorkflowSuggestionResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("Workflow suggestions");
        writer.WriteLine($"Journal: {result.JournalPath}");
        writer.WriteLine($"Command entries: {result.CommandEntryCount} of {result.EntryCount}");

        if (result.Suggestions.Count == 0)
        {
            writer.WriteLine("No repeated journal command sequences found.");
            return writer.ToString().TrimEnd();
        }

        foreach (var suggestion in result.Suggestions)
        {
            writer.WriteLine();
            writer.WriteLine($"{suggestion.Name}: repeated {suggestion.Count} time(s), first line {suggestion.FirstLine}");
            foreach (var step in suggestion.Steps)
            {
                var approval = step.RequiresApproval ? " approval" : "";
                writer.WriteLine($"  {step.Index}. [{step.Mode}{approval}] {step.Run}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("Suggested YAML (review before saving):");
        writer.WriteLine(result.Suggestions[0].Yaml);
        return writer.ToString().TrimEnd();
    }

    private static string RenderExamplesTable(IReadOnlyList<WorkflowAcceptanceExample> examples)
    {
        var writer = new StringWriter();
        writer.WriteLine("Workflow acceptance examples");
        foreach (var example in examples)
        {
            writer.WriteLine();
            writer.WriteLine($"{example.Workflow}: {example.Prompt}");
            writer.WriteLine($"  Goal: {example.Goal}");
            writer.WriteLine("  Preview:");
            foreach (var command in example.PreviewCommands)
                writer.WriteLine($"    {command}");
            writer.WriteLine("  Approval:");
            foreach (var command in example.ApprovalCommands)
                writer.WriteLine($"    {command}");
            writer.WriteLine($"  Evidence: {string.Join(", ", example.Evidence)}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderExamplesMarkdown(IReadOnlyList<WorkflowAcceptanceExample> examples)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Workflow Acceptance Examples");
        foreach (var example in examples)
        {
            writer.WriteLine();
            writer.WriteLine($"## {example.Workflow}");
            writer.WriteLine();
            writer.WriteLine($"Prompt: {example.Prompt}");
            writer.WriteLine();
            writer.WriteLine($"Goal: {example.Goal}");
            writer.WriteLine();
            writer.WriteLine("Preview commands:");
            writer.WriteLine();
            writer.WriteLine("```powershell");
            foreach (var command in example.PreviewCommands)
                writer.WriteLine(command);
            writer.WriteLine("```");
            writer.WriteLine();
            writer.WriteLine("Approval commands:");
            writer.WriteLine();
            writer.WriteLine("```powershell");
            foreach (var command in example.ApprovalCommands)
                writer.WriteLine(command);
            writer.WriteLine("```");
            writer.WriteLine();
            writer.WriteLine($"Evidence: {string.Join(", ", example.Evidence)}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderSimulationTable(WorkflowSimulationReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"Workflow simulation: {report.Name}");
        writer.WriteLine($"Can run: {(report.CanRun ? "yes" : "no")}");
        writer.WriteLine($"Steps: {report.StepCount}");
        if (report.ModeCounts.Count > 0)
        {
            writer.WriteLine("Modes:");
            foreach (var mode in report.ModeCounts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteLine($"  {mode.Key}: {mode.Value}");
            }
        }

        if (report.Issues.Count > 0)
        {
            writer.WriteLine("Issues:");
            foreach (var issue in report.Issues)
            {
                writer.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
            }
        }

        writer.WriteLine("Plan:");
        foreach (var step in report.Steps)
        {
            var approval = step.RequiresApproval ? " approval" : "";
            writer.WriteLine($"  {step.Index}. [{step.Mode}{approval}] {step.Run}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderSimulationMarkdown(WorkflowSimulationReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"# Workflow Simulation: {EscapeMarkdownText(report.Name)}");
        writer.WriteLine();
        writer.WriteLine($"- Can run: {(report.CanRun ? "yes" : "no")}");
        writer.WriteLine($"- Steps: {report.StepCount}");
        if (!string.IsNullOrWhiteSpace(report.Description))
            writer.WriteLine($"- Description: {EscapeMarkdownText(report.Description)}");
        if (report.ModeCounts.Count > 0)
        {
            writer.WriteLine("- Modes:");
            foreach (var mode in report.ModeCounts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                writer.WriteLine($"  - `{EscapeInlineCode(mode.Key)}`: {mode.Value}");
        }

        writer.WriteLine();
        writer.WriteLine("## Issues");
        if (report.Issues.Count == 0)
        {
            writer.WriteLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues)
            {
                writer.WriteLine(
                    $"- `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Plan");
        foreach (var step in report.Steps)
        {
            var approval = step.RequiresApproval ? ", approval required" : "";
            writer.WriteLine($"- {step.Index}. `{EscapeInlineCode(step.Mode)}`{approval}: `{EscapeInlineCode(step.Run)}`");
        }

        return writer.ToString().TrimEnd();
    }

    private static void AddRunGateIssues(
        WorkflowSimulationReport simulation,
        bool dryRun,
        bool yes,
        List<WorkflowValidationIssue> issues)
    {
        foreach (var step in simulation.Steps.Where(step =>
                     !dryRun &&
                     string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase)))
        {
            if (!step.RequiresApproval)
            {
                issues.Add(new WorkflowValidationIssue(
                    WorkflowValidationSeverity.Error,
                    $"steps[{step.Index - 1}].requiresApproval",
                    "mutating steps must declare requiresApproval: true before workflow run can execute them."));
            }
        }

        if (!dryRun && !yes && simulation.Steps.Any(step =>
                string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Error,
                "run.--yes",
                "workflow contains mutating steps; rerun with --yes after reviewing workflow simulate output."));
        }
    }

    private static WorkflowRunStepResult ToRunStepResult(
        WorkflowStepSimulation step,
        string status,
        int? exitCode) =>
        new()
        {
            Index = step.Index,
            Name = step.Name,
            Mode = step.Mode,
            Run = step.Run,
            RequiresApproval = step.RequiresApproval,
            Status = status,
            ExitCode = exitCode,
        };

    private static async Task WriteRunReportAsync(
        WorkflowRunReport report,
        string outputFormat,
        TextWriter output)
    {
        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOpts));
        }
        else if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderRunMarkdown(report));
        }
        else
        {
            await output.WriteLineAsync(RenderRunTable(report));
        }
    }

    private static string RenderRunTable(WorkflowRunReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"Workflow run: {report.Name}");
        writer.WriteLine($"Mode: {(report.DryRun ? "dry-run" : "execute")}");
        writer.WriteLine($"Can run: {(report.CanRun ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(report.ReceiptPath))
            writer.WriteLine($"Receipt: {report.ReceiptPath}");
        if (report.Issues.Count > 0)
        {
            writer.WriteLine("Issues:");
            foreach (var issue in report.Issues)
            {
                writer.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
            }
        }

        writer.WriteLine("Steps:");
        foreach (var step in report.Steps)
        {
            var exitText = step.ExitCode.HasValue ? $" exit={step.ExitCode.Value}" : "";
            writer.WriteLine($"  {step.Index}. {step.Status.ToUpperInvariant()} [{step.Mode}] {step.Run}{exitText}");
        }

        writer.WriteLine($"Exit code: {report.ExitCode}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderRunMarkdown(WorkflowRunReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"# Workflow Run: {EscapeMarkdownText(report.Name)}");
        writer.WriteLine();
        writer.WriteLine($"- Mode: {(report.DryRun ? "dry-run" : "execute")}");
        writer.WriteLine($"- Can run: {(report.CanRun ? "yes" : "no")}");
        writer.WriteLine($"- Success: {(report.Success ? "yes" : "no")}");
        writer.WriteLine($"- Exit code: {report.ExitCode}");
        if (!string.IsNullOrWhiteSpace(report.ReceiptPath))
            writer.WriteLine($"- Receipt: `{EscapeInlineCode(report.ReceiptPath)}`");

        writer.WriteLine();
        writer.WriteLine("## Issues");
        if (report.Issues.Count == 0)
        {
            writer.WriteLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues)
            {
                writer.WriteLine(
                    $"- `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Steps");
        foreach (var step in report.Steps)
        {
            var exitText = step.ExitCode.HasValue ? $", exit={step.ExitCode.Value}" : "";
            writer.WriteLine(
                $"- {step.Index}. `{EscapeInlineCode(step.Status.ToUpperInvariant())}` `{EscapeInlineCode(step.Mode)}`{exitText}: `{EscapeInlineCode(step.Run)}`");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderReceiptsTable(WorkflowReceiptListReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Workflow receipts");
        writer.WriteLine($"Directory: {report.ReceiptDirectory}");
        writer.WriteLine($"Filter: {(report.FailedOnly ? "failed-only" : "all")}");
        writer.WriteLine($"Receipts: {report.ReturnedCount} of {report.ReceiptCount}");

        if (!report.Exists)
        {
            writer.WriteLine("No workflow receipt directory found.");
        }
        else if (report.Receipts.Count == 0)
        {
            writer.WriteLine("No workflow receipts found.");
        }
        else
        {
            writer.WriteLine("Runs:");
            foreach (var receipt in report.Receipts)
            {
                var status = receipt.Success ? "OK" : "FAIL";
                writer.WriteLine(
                    $"  {status,-4} exit={receipt.ExitCode,-3} steps={receipt.StepCount,-3} failed={receipt.FailedStepCount,-3} issues={receipt.IssueCount,-3} {FormatReceiptTimestamp(receipt)} {receipt.Name} ({ShortPath(receipt.Path)})");
            }
        }

        AppendReceiptIssuesTable(writer, report.Issues);
        return writer.ToString().TrimEnd();
    }

    private static string RenderReceiptsMarkdown(WorkflowReceiptListReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Workflow Receipts");
        writer.WriteLine();
        writer.WriteLine($"- Status: `{(report.Success ? "OK" : "FAIL")}`");
        writer.WriteLine($"- Directory: `{EscapeInlineCode(report.ReceiptDirectory)}`");
        writer.WriteLine($"- Filter: `{(report.FailedOnly ? "failed-only" : "all")}`");
        writer.WriteLine($"- Receipts: `{report.ReturnedCount}` of `{report.ReceiptCount}`");
        writer.WriteLine();

        writer.WriteLine("## Runs");
        if (!report.Exists)
        {
            writer.WriteLine("- No workflow receipt directory found.");
        }
        else if (report.Receipts.Count == 0)
        {
            writer.WriteLine("- No workflow receipts found.");
        }
        else
        {
            writer.WriteLine();
            writer.WriteLine("| Completed | Status | Exit | Steps | Failed | Issues | Workflow | Receipt |");
            writer.WriteLine("|---|---|---:|---:|---:|---:|---|---|");
            foreach (var receipt in report.Receipts)
            {
                var status = receipt.Success ? "OK" : "FAIL";
                writer.WriteLine(
                    $"| {EscapeTableCell(FormatReceiptTimestamp(receipt))} | {status} | {receipt.ExitCode} | {receipt.StepCount} | {receipt.FailedStepCount} | {receipt.IssueCount} | {EscapeTableCell(receipt.Name)} | {EscapeTableCell(ShortPath(receipt.Path))} |");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Issues");
        if (report.Issues.Count == 0)
        {
            writer.WriteLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues)
            {
                writer.WriteLine(
                    $"- `{EscapeInlineCode(issue.Severity.ToUpperInvariant())}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static void AppendReceiptIssuesTable(
        TextWriter writer,
        IReadOnlyList<WorkflowReceiptIssue> issues)
    {
        if (issues.Count == 0)
            return;

        writer.WriteLine("Issues:");
        foreach (var issue in issues)
        {
            writer.WriteLine(
                $"  {issue.Severity.ToUpperInvariant()} {issue.Path}: {issue.Message}");
        }
    }

    private static string FormatReceiptTimestamp(WorkflowReceiptSummary receipt)
    {
        if (!string.IsNullOrWhiteSpace(receipt.CompletedAtUtc))
            return receipt.CompletedAtUtc;
        if (!string.IsNullOrWhiteSpace(receipt.StartedAtUtc))
            return receipt.StartedAtUtc;
        return "-";
    }

    private static void CompleteRunReport(WorkflowRunReport report)
    {
        report.CompletedAtUtc = DateTime.UtcNow.ToString("o");
        report.Success = report.CanRun && report.ExitCode == 0 &&
            report.Issues.All(issue => issue.Severity != WorkflowValidationSeverity.Error);
    }

    private static void TrySaveWorkflowRunReceipt(
        string workflowPath,
        string? baseDirectory,
        WorkflowRunReport report)
    {
        try
        {
            var projectRoot = ResolveWorkflowProjectRoot(workflowPath, baseDirectory);
            var receiptDir = Path.Combine(projectRoot, ".revitcli", "workflows", "receipts");
            Directory.CreateDirectory(receiptDir);

            var slug = Slugify(string.IsNullOrWhiteSpace(report.Name)
                ? Path.GetFileNameWithoutExtension(workflowPath)
                : report.Name);
            var receiptPath = Path.Combine(receiptDir, $"{slug}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.json");
            report.ReceiptPath = Path.GetFullPath(receiptPath);
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(report, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            report.ReceiptPath = null;
            report.Issues.Add(new WorkflowValidationIssue(
                WorkflowValidationSeverity.Warning,
                "receipt",
                $"workflow receipt write failed: {ex.Message}"));
        }
    }

    private static string ResolveWorkflowProjectRoot(string workflowPath, string? baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(baseDirectory))
            return Path.GetFullPath(baseDirectory!);

        var fullPath = Path.GetFullPath(workflowPath);
        var workflowDir = Path.GetDirectoryName(fullPath);
        if (workflowDir == null)
            return Directory.GetCurrentDirectory();

        var dirInfo = new DirectoryInfo(workflowDir);
        if (string.Equals(dirInfo.Name, "workflows", StringComparison.OrdinalIgnoreCase)
            && dirInfo.Parent != null
            && string.Equals(dirInfo.Parent.Name, ".revitcli", StringComparison.OrdinalIgnoreCase)
            && dirInfo.Parent.Parent != null)
        {
            return dirInfo.Parent.Parent.FullName;
        }

        return workflowDir;
    }

    private static string BuildWorkflowRunCommand(
        string workflowPath,
        bool dryRun,
        bool yes,
        bool continueOnError,
        string outputFormat)
    {
        var parts = new List<string>
        {
            "revitcli",
            "workflow",
            "run",
            QuoteArgument(Path.GetFullPath(workflowPath))
        };

        if (dryRun)
            parts.Add("--dry-run");
        if (yes)
            parts.Add("--yes");
        if (continueOnError)
            parts.Add("--continue-on-error");
        if (!string.Equals(outputFormat, "table", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("--output");
            parts.Add(outputFormat);
        }

        return string.Join(" ", parts);
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'
                ? char.ToLowerInvariant(ch)
                : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "workflow" : slug;
    }

    internal static async Task<int> RunProcessAsync(WorkflowStepSimulation step, TextWriter output)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = WorkflowCommandLine.Tokenize(step.Run);
        }
        catch (FormatException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (tokens.Count == 0)
        {
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tokens[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in tokens.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                await output.WriteLineAsync($"Error: failed to start workflow step {step.Index}: {step.Run}");
                return 1;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                await output.WriteLineAsync(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                await output.WriteLineAsync(stderr.TrimEnd());
            }

            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await output.WriteLineAsync($"Error: failed to run workflow step {step.Index}: {ex.Message}");
            return 1;
        }
    }

    private static bool IsJson(string outputFormat) =>
        string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkdown(string outputFormat) =>
        string.Equals(outputFormat, "markdown", StringComparison.OrdinalIgnoreCase);

    private static string EscapeInlineCode(string? value)
    {
        return (value ?? string.Empty)
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeTableCell(string? value)
    {
        return EscapeMarkdownText(value)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string ResolvePath(string projectRoot, string path) =>
        Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));

    private static IReadOnlyList<(string Name, string File, string Description)> ResolveTemplates(string template)
    {
        if (string.Equals(template, "all", StringComparison.OrdinalIgnoreCase))
        {
            return Templates;
        }

        var match = Templates.FirstOrDefault(item =>
            string.Equals(item.Name, template, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(match.Name)
            ? Array.Empty<(string Name, string File, string Description)>()
            : new[] { match };
    }

    private static IReadOnlyList<WorkflowAcceptanceExample> ResolveAcceptanceExamples(string? template)
    {
        if (string.IsNullOrWhiteSpace(template) ||
            string.Equals(template, "all", StringComparison.OrdinalIgnoreCase))
        {
            return AcceptanceExamples;
        }

        return AcceptanceExamples
            .Where(example => string.Equals(example.Workflow, template, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string? FindWorkflowTemplatesDir()
    {
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (exeDir != null)
        {
            var candidate = Path.Combine(exeDir, "profiles", "workflows");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var dir = exeDir;
            for (var i = 0; i < 6; i++)
            {
                dir = Directory.GetParent(dir)?.FullName;
                if (dir == null)
                {
                    break;
                }

                candidate = Path.Combine(dir, "profiles", "workflows");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "profiles", "workflows");
        return Directory.Exists(cwdCandidate) ? cwdCandidate : null;
    }

    private static string ShortPath(string path)
    {
        var current = Directory.GetCurrentDirectory();
        var relative = Path.GetRelativePath(current, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    private sealed record WorkflowAcceptanceExample(
        [property: JsonPropertyName("workflow")] string Workflow,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("goal")] string Goal,
        [property: JsonPropertyName("previewCommands")] IReadOnlyList<string> PreviewCommands,
        [property: JsonPropertyName("approvalCommands")] IReadOnlyList<string> ApprovalCommands,
        [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence);
}

public sealed class WorkflowValidationFileReport
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("stepCount")]
    public int StepCount { get; set; }

    [JsonPropertyName("issues")]
    public List<WorkflowValidationIssue> Issues { get; set; } = new();
}
