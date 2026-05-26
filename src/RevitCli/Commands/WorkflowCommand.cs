using System;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Journal;
using RevitCli.Output;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class WorkflowCommand
{
    private const int WorkflowTimeoutExitCode = 124;
    private const long MaxWorkflowTimeoutMs = int.MaxValue;

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

    internal static IReadOnlyList<(string Name, string File, string Description)> BuiltInTemplates => Templates;

    internal static IReadOnlyList<string> BuiltInAcceptanceWorkflowNames =>
        AcceptanceExamples.Select(example => example.Workflow).ToArray();

    internal static string? FindBuiltInWorkflowTemplatesDirectory() => FindWorkflowTemplatesDir();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Command Create()
    {
        var command = new Command("workflow", "Create, validate, run, review, and index terminal workflow YAML files");
        command.AddCommand(CreateInitCommand());
        command.AddCommand(CreateValidateCommand());
        command.AddCommand(CreateSimulateCommand());
        command.AddCommand(CreateReviewCommand());
        command.AddCommand(CreateRegistryCommand());
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
        var timeoutOpt = new Option<long>("--timeout-ms", () => 0, "Maximum milliseconds per executed step; 0 disables timeouts");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("run", "Run workflow steps after validation")
        {
            fileArg,
            dirOpt,
            dryRunOpt,
            yesOpt,
            continueOpt,
            timeoutOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string file,
            string? dir,
            bool dryRun,
            bool yes,
            bool continueOnError,
            long timeoutMs,
            string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteRunAsync(
                file,
                dir,
                dryRun,
                yes,
                continueOnError,
                outputFormat,
                Console.Out,
                timeoutMs: timeoutMs);
        }, fileArg, dirOpt, dryRunOpt, yesOpt, continueOpt, timeoutOpt, outputOpt);

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

    private static Command CreateReviewCommand()
    {
        var fileArg = new Argument<string>("file", "Workflow YAML file to review");
        var dirOpt = new Option<string?>("--dir", "Base directory for relative workflow paths");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("review", "Review workflow readiness, approval gates, and handoff evidence")
        {
            fileArg,
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string file, string? dir, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteReviewAsync(file, dir, outputFormat, Console.Out);
        }, fileArg, dirOpt, outputOpt);

        return command;
    }

    private static Command CreateRegistryCommand()
    {
        var pathArg = new Argument<string?>(
            "path",
            () => null,
            "Workflow YAML file or directory; defaults to .revitcli/workflows/*.yml");
        var dirOpt = new Option<string?>("--dir", "Base directory for default workflow discovery");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("registry", "Index local workflow YAML contract fields without running commands")
        {
            pathArg,
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string? path, string? dir, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteRegistryAsync(path, dir, outputFormat, Console.Out);
        }, pathArg, dirOpt, outputOpt);

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
        var nameOpt = new Option<string?>("--name", "Only show receipts for a workflow name");
        var minDurationOpt = new Option<long>("--min-duration-ms", () => 0, "Only show receipts at or above a duration in milliseconds");
        var sortOpt = new Option<string>("--sort", () => "completed", "Sort receipts: completed | duration");
        var windowOpt = new Option<string?>("--window", "Only show receipts completed inside a recent window, e.g. 24h, 7d, 60m");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("receipts", "Review local workflow-run receipts")
        {
            dirOpt,
            limitOpt,
            failedOnlyOpt,
            nameOpt,
            minDurationOpt,
            sortOpt,
            windowOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string? dir,
            int limit,
            bool failedOnly,
            string? name,
            long minDurationMs,
            string sort,
            string? window,
            string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteReceiptsAsync(
                dir,
                limit,
                failedOnly,
                name,
                outputFormat,
                Console.Out,
                minDurationMs,
                sort,
                window);
        }, dirOpt, limitOpt, failedOnlyOpt, nameOpt, minDurationOpt, sortOpt, windowOpt, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteValidateAsync(
        string? fileOrDirectory,
        string? baseDirectory,
        string outputFormat,
        TextWriter output)
    {
        if (!IsWorkflowReportOutputFormat(outputFormat))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

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
        string? nameFilter,
        string outputFormat,
        TextWriter output,
        long? minDurationMs = null,
        string sort = "completed",
        string? window = null,
        DateTimeOffset? nowUtc = null)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var format, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (limit < 1)
        {
            await output.WriteLineAsync("Error: --limit must be at least 1.");
            return 1;
        }

        if (minDurationMs is < 0)
        {
            await output.WriteLineAsync("Error: --min-duration-ms must be at least 0.");
            return 1;
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sort)
            ? "completed"
            : sort.Trim().ToLowerInvariant();
        if (normalizedSort is not ("completed" or "duration"))
        {
            await output.WriteLineAsync("Error: --sort must be 'completed' or 'duration'.");
            return 1;
        }

        DateTimeOffset? sinceUtc = null;
        var normalizedWindow = string.IsNullOrWhiteSpace(window) ? null : window.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedWindow))
        {
            try
            {
                var span = HistoryCommand.ParseWindow(normalizedWindow);
                sinceUtc = (nowUtc ?? DateTimeOffset.UtcNow) - span;
            }
            catch (FormatException ex)
            {
                await output.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }
        }

        WorkflowReceiptListReport report;
        try
        {
            report = WorkflowReceiptReader.Read(
                projectDirectory,
                limit,
                failedOnly,
                nameFilter,
                minDurationMs,
                normalizedSort,
                sinceUtc,
                normalizedWindow);
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

    public static async Task<int> ExecuteRegistryAsync(
        string? fileOrDirectory,
        string? baseDirectory,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? nowUtc = null)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var format, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var report = BuildWorkflowRegistry(fileOrDirectory, baseDirectory, nowUtc ?? DateTimeOffset.UtcNow);
        switch (format)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOpts));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderRegistryMarkdown(report));
                break;
            default:
                await output.WriteLineAsync(RenderRegistryTable(report));
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
        if (!IsWorkflowReportOutputFormat(outputFormat))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

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

    public static async Task<int> ExecuteReviewAsync(
        string file,
        string? baseDirectory,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var format, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

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
        var review = BuildWorkflowReview(loaded, simulation, baseDirectory);
        switch (format)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(review, JsonOpts));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderReviewMarkdown(review));
                break;
            default:
                await output.WriteLineAsync(RenderReviewTable(review));
                break;
        }

        return review.CanRun ? 0 : 1;
    }

    private static WorkflowRegistryReport BuildWorkflowRegistry(
        string? fileOrDirectory,
        string? baseDirectory,
        DateTimeOffset generatedAtUtc)
    {
        var projectRoot = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory!);
        var discovery = DiscoverWorkflowRegistryFiles(fileOrDirectory, projectRoot);
        var report = new WorkflowRegistryReport
        {
            GeneratedAtUtc = generatedAtUtc.ToString("o"),
            ProjectDirectory = projectRoot,
            WorkflowRoot = discovery.Root,
            Exists = discovery.Exists,
            Issues = discovery.Issues.ToList(),
        };

        foreach (var file in discovery.Files)
        {
            report.Workflows.Add(BuildWorkflowRegistryEntry(file));
        }

        report.WorkflowCount = report.Workflows.Count;
        report.ValidWorkflowCount = report.Workflows.Count(workflow => workflow.CanRun);
        report.InvalidWorkflowCount = report.WorkflowCount - report.ValidWorkflowCount;
        report.ReadOnlyWorkflowCount = report.Workflows.Count(workflow =>
            string.Equals(workflow.RiskLevel, "read-only", StringComparison.OrdinalIgnoreCase));
        report.DryRunWorkflowCount = report.Workflows.Count(workflow =>
            string.Equals(workflow.RiskLevel, "dry-run", StringComparison.OrdinalIgnoreCase));
        report.MutatingWorkflowCount = report.Workflows.Count(workflow =>
            string.Equals(workflow.RiskLevel, "mutating", StringComparison.OrdinalIgnoreCase));
        report.ApprovalRequiredStepCount = report.Workflows.Sum(workflow => workflow.ApprovalRequiredCount);
        report.DryRunCommandCount = report.Workflows.Sum(workflow => workflow.DryRunCommands.Count);
        report.RollbackSupportedWorkflowCount = report.Workflows.Count(workflow => workflow.RollbackSupport);

        foreach (var workflowIssue in report.Workflows.SelectMany(workflow => workflow.Issues))
        {
            report.Issues.Add(workflowIssue);
        }

        report.Success = report.Issues.All(issue => issue.Severity != WorkflowValidationSeverity.Error);
        return report;
    }

    private static WorkflowRegistryDiscovery DiscoverWorkflowRegistryFiles(
        string? fileOrDirectory,
        string projectRoot)
    {
        var issues = new List<WorkflowValidationIssue>();
        var explicitPath = !string.IsNullOrWhiteSpace(fileOrDirectory);
        var root = explicitPath
            ? ResolvePath(projectRoot, fileOrDirectory!)
            : Path.Combine(projectRoot, WorkflowLoader.DefaultDirectory);

        if (File.Exists(root))
        {
            return new WorkflowRegistryDiscovery(
                Path.GetDirectoryName(root) ?? projectRoot,
                Exists: true,
                new[] { root },
                issues);
        }

        if (Directory.Exists(root))
        {
            var files = Directory.EnumerateFiles(root, "*.yml")
                .Concat(Directory.EnumerateFiles(root, "*.yaml"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
            {
                issues.Add(new WorkflowValidationIssue(
                    WorkflowValidationSeverity.Warning,
                    "workflowRoot",
                    $"no workflow YAML files found in {root}."));
            }

            return new WorkflowRegistryDiscovery(root, Exists: true, files, issues);
        }

        issues.Add(new WorkflowValidationIssue(
            explicitPath ? WorkflowValidationSeverity.Error : WorkflowValidationSeverity.Warning,
            explicitPath ? "source.missing" : "workflowRoot",
            explicitPath
                ? $"workflow path not found: {root}"
                : $"workflow directory not found: {root}; run 'revitcli workflow init <template>' or pass a workflow path."));

        return new WorkflowRegistryDiscovery(root, Exists: false, Array.Empty<string>(), issues);
    }

    private static WorkflowRegistryEntry BuildWorkflowRegistryEntry(string path)
    {
        try
        {
            var loaded = WorkflowLoader.Load(path);
            var simulation = WorkflowValidator.Simulate(loaded);
            var entry = BuildWorkflowRegistryEntry(loaded, simulation);
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            return new WorkflowRegistryEntry
            {
                Path = Path.GetFullPath(path),
                Name = Path.GetFileNameWithoutExtension(path),
                Version = 0,
                CanRun = false,
                RiskLevel = "invalid",
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

    private static WorkflowRegistryEntry BuildWorkflowRegistryEntry(
        LoadedWorkflow loaded,
        WorkflowSimulationReport simulation)
    {
        var steps = simulation.Steps;
        var readOnlyStepCount = steps.Count(step => string.Equals(step.Mode, "read-only", StringComparison.OrdinalIgnoreCase));
        var dryRunStepCount = steps.Count(step => string.Equals(step.Mode, "dry-run", StringComparison.OrdinalIgnoreCase));
        var mutatingStepCount = steps.Count(step => string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase));
        var approvalRequiredCount = steps.Count(step => step.RequiresApproval);
        var readOnlyWriteCapableStepCount = steps.Count(step =>
            string.Equals(step.Mode, "read-only", StringComparison.OrdinalIgnoreCase) &&
            WorkflowValidator.CommandLooksWriteCapable(step.Run));
        var dryRunCommands = steps
            .Where(step => string.Equals(step.Mode, "dry-run", StringComparison.OrdinalIgnoreCase) ||
                step.Run.Contains("--dry-run", StringComparison.OrdinalIgnoreCase))
            .Select(step => step.Run)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var approvalCommands = steps
            .Where(step => step.RequiresApproval)
            .Select(step => step.Run)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rollbackCommands = steps
            .Where(step => StartsWithCommand(NormalizeWorkflowCommand(step.Run), "rollback") ||
                NormalizeWorkflowCommand(step.Run).Contains(" rollback", StringComparison.OrdinalIgnoreCase))
            .Select(step => step.Run)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkflowRegistryEntry
        {
            Path = loaded.Path,
            Name = simulation.Name,
            Description = simulation.Description,
            Version = loaded.Workflow.Version,
            CanRun = simulation.CanRun,
            StepCount = simulation.StepCount,
            ReadOnlyStepCount = readOnlyStepCount,
            DryRunStepCount = dryRunStepCount,
            MutatingStepCount = mutatingStepCount,
            ApprovalRequiredCount = approvalRequiredCount,
            RiskLevel = DetermineWorkflowRiskLevel(readOnlyStepCount, dryRunStepCount, mutatingStepCount, readOnlyWriteCapableStepCount, simulation.CanRun),
            ReadWriteScope = BuildReadWriteScope(readOnlyStepCount, dryRunStepCount, mutatingStepCount, readOnlyWriteCapableStepCount),
            Inputs = InferWorkflowInputs(steps),
            Outputs = InferWorkflowOutputs(steps),
            DryRunCommands = dryRunCommands,
            ApprovalCommands = approvalCommands,
            RollbackCommands = rollbackCommands,
            RollbackSupport = rollbackCommands.Length > 0,
            ReceiptSchemas = InferReceiptSchemas(steps),
            AcceptanceEvidence = BuildAcceptanceEvidence(steps, simulation.Name),
            Issues = simulation.Issues.ToList()
        };
    }

    private static string DetermineWorkflowRiskLevel(
        int readOnlyStepCount,
        int dryRunStepCount,
        int mutatingStepCount,
        int readOnlyWriteCapableStepCount,
        bool canRun)
    {
        if (!canRun)
            return "invalid";
        if (mutatingStepCount > 0 || readOnlyWriteCapableStepCount > 0)
            return "mutating";
        if (dryRunStepCount > 0)
            return "dry-run";
        return readOnlyStepCount > 0 ? "read-only" : "empty";
    }

    private static IReadOnlyList<string> BuildReadWriteScope(
        int readOnlyStepCount,
        int dryRunStepCount,
        int mutatingStepCount,
        int readOnlyWriteCapableStepCount)
    {
        var scope = new List<string>();
        if (readOnlyStepCount > 0)
            scope.Add("read-only");
        if (dryRunStepCount > 0)
            scope.Add("dry-run");
        if (mutatingStepCount > 0 || readOnlyWriteCapableStepCount > 0)
            scope.Add("mutating");
        return scope;
    }

    private static IReadOnlyList<string> InferWorkflowInputs(IReadOnlyList<WorkflowStepSimulation> steps)
    {
        var inputs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "workflow YAML"
        };

        foreach (var step in steps)
        {
            foreach (var artifact in RequiredArtifactsForCommand(step.Run))
                inputs.Add(artifact);
            foreach (var artifact in ReferencedArtifactsForCommand(step.Run))
                inputs.Add(artifact);
        }

        return inputs.ToArray();
    }

    private static IReadOnlyList<string> InferWorkflowOutputs(IReadOnlyList<WorkflowStepSimulation> steps)
    {
        var outputs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "workflow-registry.v1",
            "workflow-review.v1",
            "workflow-run-receipt.v1"
        };

        foreach (var step in steps)
        {
            var command = NormalizeWorkflowCommand(step.Run);
            if (StartsWithCommand(command, "history capture"))
                outputs.Add("history snapshot");
            if (StartsWithCommand(command, "deliverables bundle"))
                outputs.Add("delivery bundle");
            if (StartsWithCommand(command, "issue package"))
                outputs.Add("issue package");
            if (StartsWithCommand(command, "publish"))
                outputs.Add("publish output");
            if (StartsWithCommand(command, "schedule export") ||
                StartsWithCommand(command, "schedules batch-export"))
            {
                outputs.Add("schedule export");
                outputs.Add("schedule-export-manifest.v1");
            }
        }

        return outputs.ToArray();
    }

    private static IReadOnlyList<string> InferReceiptSchemas(IReadOnlyList<WorkflowStepSimulation> steps)
    {
        var schemas = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "workflow-run-receipt.v1"
        };

        foreach (var step in steps)
        {
            var command = NormalizeWorkflowCommand(step.Run);
            if (StartsWithCommand(command, "plan apply"))
                schemas.Add("plan-receipt.v1");
            if (StartsWithCommand(command, "deliverables bundle"))
                schemas.Add("delivery-bundle-receipt.v1");
            if (StartsWithCommand(command, "issue package"))
                schemas.Add("issue-package-receipt.v1");
            if (StartsWithCommand(command, "publish"))
                schemas.Add("publish-receipt.v1");
        }

        return schemas.ToArray();
    }

    private static IReadOnlyList<string> BuildAcceptanceEvidence(
        IReadOnlyList<WorkflowStepSimulation> steps,
        string workflowName)
    {
        var evidence = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "workflow validate",
            "workflow simulate",
            "workflow review",
            "workflow receipts",
            "workbench verify"
        };

        var acceptance = AcceptanceExamples.FirstOrDefault(example =>
            string.Equals(example.Workflow, workflowName, StringComparison.OrdinalIgnoreCase));
        if (acceptance != null)
        {
            foreach (var item in acceptance.Evidence)
                evidence.Add(item);
        }

        if (steps.Any(step => StartsWithCommand(NormalizeWorkflowCommand(step.Run), "journal verify")))
            evidence.Add("journal verify");
        foreach (var schema in InferReceiptSchemas(steps))
            evidence.Add(schema);
        foreach (var step in steps)
        {
            var command = NormalizeWorkflowCommand(step.Run);
            if (StartsWithCommand(command, "schedule export") ||
                StartsWithCommand(command, "schedules batch-export"))
            {
                evidence.Add("schedule-export-manifest.v1");
            }
        }

        return evidence.ToArray();
    }

    private static WorkflowReviewReport BuildWorkflowReview(
        LoadedWorkflow loaded,
        WorkflowSimulationReport simulation,
        string? baseDirectory)
    {
        var path = ShortPath(loaded.Path);
        var projectRoot = ResolveWorkflowProjectRoot(loaded.Path, baseDirectory);
        var acceptance = AcceptanceExamples.FirstOrDefault(example =>
            string.Equals(example.Workflow, simulation.Name, StringComparison.OrdinalIgnoreCase));
        var mutatingCount = simulation.Steps.Count(step =>
            string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase));
        var dryRunCount = simulation.Steps.Count(step =>
            string.Equals(step.Mode, "dry-run", StringComparison.OrdinalIgnoreCase));
        var approvalCount = simulation.Steps.Count(step => step.RequiresApproval);
        var recommendedCommands = new List<string>
        {
            $"revitcli workflow validate {QuoteArgument(path)} --output markdown",
            $"revitcli workflow simulate {QuoteArgument(path)} --output markdown",
            $"revitcli workflow run {QuoteArgument(path)} --dry-run --output markdown"
        };
        if (mutatingCount > 0)
            recommendedCommands.Add($"revitcli workflow run {QuoteArgument(path)} --yes --output markdown");

        var projectDirOption = ProjectDirOption(baseDirectory);
        var preRunHandoffCommands = new List<string>
        {
            $"revitcli workbench verify{projectDirOption} --output json",
            $"revitcli workbench handoff{projectDirOption} --output markdown",
            $"revitcli inspect workflows{projectDirOption} --output markdown"
        };
        var workflowName = QuoteArgument(simulation.Name);
        var postRunReceiptCommands = new List<string>
        {
            $"revitcli workflow receipts --name {workflowName} --output markdown",
            $"revitcli workflow receipts --name {workflowName} --failed-only --output markdown",
            $"revitcli workflow receipts --name {workflowName} --window 24h --output markdown",
            $"revitcli workflow receipts --name {workflowName} --min-duration-ms 60000 --sort duration --output markdown"
        };

        var artifactReadiness = BuildWorkflowArtifactReadiness(projectRoot, simulation.Steps);
        var handoffNotes = new List<string>
        {
            "Review validation and simulation output before any approved run.",
            "Use --dry-run first; use --yes only after explicit human approval.",
            "After approved runs, use the post-run receipt commands to review failed, recent, or slow workflow executions."
        };
        if (simulation.Issues.Count > 0)
            handoffNotes.Add("Resolve workflow validation issues before reuse.");
        var incompleteArtifactCount = artifactReadiness.Count(artifact =>
            !string.Equals(artifact.Status, "present", StringComparison.OrdinalIgnoreCase));
        if (incompleteArtifactCount > 0)
            handoffNotes.Add($"{incompleteArtifactCount} inferred project artifact(s) are missing or empty; review project artifact readiness before approval.");
        if (approvalCount > 0)
            handoffNotes.Add($"{approvalCount} step(s) require explicit approval metadata.");

        return new WorkflowReviewReport
        {
            Path = loaded.Path,
            Name = simulation.Name,
            Description = simulation.Description,
            ProjectDirectory = projectRoot,
            CanRun = simulation.CanRun,
            StepCount = simulation.StepCount,
            MutatingStepCount = mutatingCount,
            DryRunStepCount = dryRunCount,
            ApprovalRequiredCount = approvalCount,
            ModeCounts = new Dictionary<string, int>(simulation.ModeCounts, StringComparer.OrdinalIgnoreCase),
            Issues = simulation.Issues.ToList(),
            Steps = simulation.Steps.ToList(),
            ArtifactReadiness = artifactReadiness,
            PreRunHandoffCommands = preRunHandoffCommands,
            RecommendedCommands = recommendedCommands,
            PostRunReceiptCommands = postRunReceiptCommands,
            Evidence = acceptance?.Evidence.ToArray() ?? Array.Empty<string>(),
            HandoffNotes = handoffNotes
        };
    }

    private static List<WorkflowArtifactReadiness> BuildWorkflowArtifactReadiness(
        string projectRoot,
        IReadOnlyList<WorkflowStepSimulation> steps)
    {
        var matched = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            foreach (var artifact in RequiredArtifactsForCommand(step.Run))
            {
                if (!matched.TryGetValue(artifact, out var indexes))
                {
                    indexes = new SortedSet<int>();
                    matched[artifact] = indexes;
                }

                indexes.Add(step.Index);
            }
        }

        return matched
            .OrderBy(item => ArtifactSortOrder(item.Key))
            .Select(item => CreateArtifactReadiness(projectRoot, item.Key, item.Value.ToArray()))
            .ToList();
    }

    private static IReadOnlyList<string> ReferencedArtifactsForCommand(string run)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = WorkflowCommandLine.Tokenize(run);
        }
        catch (FormatException)
        {
            return Array.Empty<string>();
        }

        if (tokens.Count == 0)
            return Array.Empty<string>();

        var commandStart = string.Equals(tokens[0], "revitcli", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (tokens.Count <= commandStart)
            return Array.Empty<string>();

        var references = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        AddOptionReference(tokens, "--profile", "profile", references);
        AddOptionReference(tokens, "--manifest", "manifest", references);
        AddOptionReference(tokens, "--spec", "spec", references);
        AddOptionReference(tokens, "--rule", "rule", references);
        AddOptionReference(tokens, "--against", "against", references);
        AddOptionReference(tokens, "--from", "from", references);
        AddOptionReference(tokens, "--to", "to", references);
        AddOptionReference(tokens, "--receipt-dir", "receipt-dir", references);

        var command = NormalizeWorkflowCommand(run);
        if (StartsWithCommand(command, "plan apply") &&
            TryGetPositionalAfter(tokens, commandStart + 2, out var planPath))
        {
            references.Add($"plan:{planPath}");
        }

        if (StartsWithCommand(command, "rollback") &&
            TryGetPositionalAfter(tokens, commandStart + 1, out var receiptPath))
        {
            references.Add($"receipt:{receiptPath}");
        }

        if ((StartsWithCommand(command, "workflow validate") ||
             StartsWithCommand(command, "workflow simulate") ||
             StartsWithCommand(command, "workflow review") ||
             StartsWithCommand(command, "workflow run")) &&
            TryGetPositionalAfter(tokens, commandStart + 2, out var workflowPath))
        {
            references.Add($"workflow:{workflowPath}");
        }

        return references.ToArray();
    }

    private static void AddOptionReference(
        IReadOnlyList<string> tokens,
        string option,
        string label,
        ISet<string> references)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("-", StringComparison.Ordinal))
                    references.Add($"{label}:{tokens[i + 1]}");
                continue;
            }

            var prefix = option + "=";
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                token.Length > prefix.Length)
            {
                references.Add($"{label}:{token[prefix.Length..]}");
            }
        }
    }

    private static bool TryGetPositionalAfter(
        IReadOnlyList<string> tokens,
        int start,
        out string value)
    {
        for (var i = Math.Max(0, start); i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                if (!token.Contains('=', StringComparison.Ordinal) &&
                    OptionUsuallyTakesValue(token) &&
                    i + 1 < tokens.Count &&
                    !tokens[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                }

                continue;
            }

            value = token;
            return true;
        }

        value = "";
        return false;
    }

    private static bool OptionUsuallyTakesValue(string option) =>
        option is
            "--against" or
            "--bundle-path" or
            "--category" or
            "--dir" or
            "--fields" or
            "--filter" or
            "--format" or
            "--from" or
            "--manifest" or
            "--max-changes" or
            "--name" or
            "--output" or
            "--output-dir" or
            "--plan-output" or
            "--profile" or
            "--receipt-dir" or
            "--rule" or
            "--set" or
            "--sort" or
            "--spec" or
            "--template" or
            "--timeout-ms" or
            "--to" or
            "--value" or
            "--window";

    private static IReadOnlyList<string> RequiredArtifactsForCommand(string run)
    {
        var command = NormalizeWorkflowCommand(run);
        var artifacts = new List<string>();
        if (StartsWithCommand(command, "profile") ||
            StartsWithCommand(command, "check") ||
            StartsWithCommand(command, "publish"))
        {
            artifacts.Add("profile");
        }

        if (StartsWithCommand(command, "standards"))
            artifacts.Add("standards");

        if (StartsWithCommand(command, "workflow receipts"))
        {
            artifacts.Add("workflow-receipts");
        }
        else if (StartsWithCommand(command, "workflow"))
        {
            artifacts.Add("workflows");
        }

        if (StartsWithCommand(command, "history"))
            artifacts.Add("history");

        if (StartsWithCommand(command, "journal"))
            artifacts.Add("journal");

        if (StartsWithCommand(command, "deliverables"))
        {
            artifacts.Add("delivery-manifest");
            artifacts.Add("delivery-receipts");
        }

        if (StartsWithCommand(command, "plan"))
            artifacts.Add("plans");

        if (StartsWithCommand(command, "report") && command.Contains(" --report", StringComparison.OrdinalIgnoreCase))
            artifacts.Add("reports");

        return artifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static WorkflowArtifactReadiness CreateArtifactReadiness(
        string projectRoot,
        string name,
        IReadOnlyList<int> matchedSteps)
    {
        var artifact = ArtifactMetadata(projectRoot, name);
        var status = ArtifactStatus(projectRoot, artifact.RelativePath, artifact.IsFile, artifact.Patterns, out var count);
        return new WorkflowArtifactReadiness
        {
            Name = name,
            Status = status,
            Count = count,
            RelativePath = NormalizeRelativePath(artifact.RelativePath),
            ReviewCommand = artifact.ReviewCommand,
            WorkingDirectory = projectRoot,
            MatchedSteps = matchedSteps.ToArray(),
            Notes = artifact.Notes
        };
    }

    private static WorkflowArtifactMetadata ArtifactMetadata(string projectRoot, string name)
    {
        var projectDirOption = ProjectDirOption(projectRoot);
        return name switch
        {
            "profile" => new(
                ".revitcli.yml",
                IsFile: true,
                Array.Empty<string>(),
                "revitcli profile validate",
                "Profile-driven checks, publish pipelines, and safety defaults."),
            "standards" => new(
                Path.Combine(".revitcli", "standards.yml"),
                IsFile: true,
                Array.Empty<string>(),
                $"revitcli standards validate{projectDirOption} --output markdown",
                "Installed local standards manifest."),
            "workflows" => new(
                Path.Combine(".revitcli", "workflows"),
                IsFile: false,
                new[] { "*.yml", "*.yaml" },
                $"revitcli inspect workflows{projectDirOption} --output markdown",
                "Reusable workflow YAML files."),
            "workflow-receipts" => new(
                Path.Combine(".revitcli", "workflows", "receipts"),
                IsFile: false,
                new[] { "*.json" },
                $"revitcli workflow receipts{projectDirOption} --output markdown",
                "Saved workflow-run receipts for deadline triage."),
            "history" => new(
                Path.Combine(".revitcli", "history"),
                IsFile: false,
                new[] { "snapshot-*.json.gz" },
                $"revitcli history list --dir {QuoteArgument(Path.Combine(projectRoot, ".revitcli", "history"))} --limit 5",
                "Local model snapshot timeline."),
            "journal" => new(
                Path.Combine(".revitcli", "journal.jsonl"),
                IsFile: true,
                Array.Empty<string>(),
                $"revitcli journal review{projectDirOption} --output markdown",
                "Local operation journal."),
            "delivery-manifest" => new(
                Path.Combine(".revitcli", "deliveries", "manifest.jsonl"),
                IsFile: true,
                Array.Empty<string>(),
                $"revitcli deliverables list{projectDirOption} --output markdown",
                "Delivery manifest entries for exported outputs."),
            "delivery-receipts" => new(
                Path.Combine(".revitcli", "receipts"),
                IsFile: false,
                new[] { "*.json" },
                $"revitcli deliverables verify{projectDirOption} --output markdown",
                "Publish and delivery receipts."),
            "plans" => new(
                Path.Combine(".revitcli", "plans"),
                IsFile: false,
                new[] { "*.json" },
                $"revitcli inspect plans{projectDirOption} --output markdown",
                "Saved mutation plans."),
            "reports" => new(
                Path.Combine(".revitcli", "reports"),
                IsFile: false,
                new[] { "*.*" },
                $"revitcli report knowledge{projectDirOption} --output markdown",
                "Saved local reports and review handoffs."),
            _ => new(name, IsFile: false, new[] { "*.*" }, "", "")
        };
    }

    private static string ArtifactStatus(
        string projectRoot,
        string relativePath,
        bool isFile,
        IReadOnlyList<string> patterns,
        out int count)
    {
        var path = Path.Combine(projectRoot, relativePath);
        if (isFile)
        {
            if (!File.Exists(path))
            {
                count = 0;
                return "missing";
            }

            count = relativePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? CountNonEmptyLines(path)
                : 1;
            return count == 0 ? "empty" : "present";
        }

        if (!Directory.Exists(path))
        {
            count = 0;
            return "missing";
        }

        count = patterns
            .SelectMany(pattern => Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return count == 0 ? "empty" : "present";
    }

    private static int CountNonEmptyLines(string path)
    {
        try
        {
            return File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return 0;
        }
    }

    private static string NormalizeWorkflowCommand(string run)
    {
        var command = (run ?? string.Empty).Trim();
        if (command.StartsWith("revitcli ", StringComparison.OrdinalIgnoreCase))
            command = command["revitcli ".Length..].TrimStart();
        return command;
    }

    private static bool StartsWithCommand(string command, string expected) =>
        command.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase);

    private static int ArtifactSortOrder(string name) => name switch
    {
        "profile" => 0,
        "standards" => 1,
        "workflows" => 2,
        "workflow-receipts" => 3,
        "history" => 4,
        "journal" => 5,
        "delivery-manifest" => 6,
        "delivery-receipts" => 7,
        "plans" => 8,
        "reports" => 9,
        _ => 100
    };

    private static string NormalizeRelativePath(string value) =>
        value.Replace('\\', '/');

    public static async Task<int> ExecuteRunAsync(
        string file,
        string? baseDirectory,
        bool dryRun,
        bool yes,
        bool continueOnError,
        string outputFormat,
        TextWriter output,
        long timeoutMs = 0,
        Func<WorkflowStepSimulation, TextWriter, Task<int>>? runner = null)
    {
        if (!IsWorkflowReportOutputFormat(outputFormat))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (timeoutMs < 0)
        {
            await output.WriteLineAsync("Error: --timeout-ms must be at least 0.");
            return 1;
        }

        if (timeoutMs > MaxWorkflowTimeoutMs)
        {
            await output.WriteLineAsync($"Error: --timeout-ms must be no more than {MaxWorkflowTimeoutMs}.");
            return 1;
        }

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
            Command = BuildWorkflowRunCommand(loaded.Path, dryRun, yes, continueOnError, timeoutMs, outputFormat),
            StartedAtUtc = startedAt,
            Operator = Environment.UserName,
            Machine = Environment.MachineName,
            DryRun = dryRun,
            TimeoutMs = timeoutMs,
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

        foreach (var step in simulation.Steps)
        {
            var stepStartedAt = DateTimeOffset.UtcNow;
            var execution = await RunWorkflowStepAsync(step, output, timeoutMs, runner);
            var stepCompletedAt = DateTimeOffset.UtcNow;
            var status = execution.TimedOut ? "timed-out" : execution.ExitCode == 0 ? "ok" : "failed";
            report.Steps.Add(ToRunStepResult(
                step,
                status,
                execution.ExitCode,
                stepStartedAt,
                stepCompletedAt,
                execution.TimedOut));

            if (execution.TimedOut)
            {
                report.Issues.Add(new WorkflowValidationIssue(
                    WorkflowValidationSeverity.Error,
                    $"steps[{step.Index}].timeout",
                    $"workflow step {step.Index} exceeded --timeout-ms {timeoutMs}."));
            }

            if (execution.ExitCode != 0)
            {
                report.ExitCode = execution.ExitCode;
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

    private static string RenderReviewTable(WorkflowReviewReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"Workflow review: {report.Name}");
        writer.WriteLine($"Project: {report.ProjectDirectory}");
        writer.WriteLine($"Can run: {(report.CanRun ? "yes" : "no")}");
        writer.WriteLine($"Steps: {report.StepCount}; dry-run: {report.DryRunStepCount}; mutating: {report.MutatingStepCount}; approvals: {report.ApprovalRequiredCount}");
        if (report.Issues.Count > 0)
        {
            writer.WriteLine("Issues:");
            foreach (var issue in report.Issues)
                writer.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
        }

        writer.WriteLine("Pre-run handoff commands:");
        foreach (var command in report.PreRunHandoffCommands)
            writer.WriteLine($"  {command}");

        writer.WriteLine("Project artifact readiness:");
        if (report.ArtifactReadiness.Count == 0)
        {
            writer.WriteLine("  (no local project artifacts inferred from workflow steps)");
        }
        else
        {
            foreach (var artifact in report.ArtifactReadiness)
            {
                writer.WriteLine(
                    $"  {artifact.Status.ToUpperInvariant(),-7} {artifact.Name,-18} steps={string.Join(",", artifact.MatchedSteps)} review=\"{artifact.ReviewCommand}\"");
            }
        }

        writer.WriteLine("Recommended commands:");
        foreach (var command in report.RecommendedCommands)
            writer.WriteLine($"  {command}");

        writer.WriteLine("Post-run receipt triage:");
        foreach (var command in report.PostRunReceiptCommands)
            writer.WriteLine($"  {command}");

        if (report.Evidence.Count > 0)
            writer.WriteLine($"Evidence: {string.Join(", ", report.Evidence)}");

        writer.WriteLine("Handoff notes:");
        foreach (var note in report.HandoffNotes)
            writer.WriteLine($"  - {note}");

        return writer.ToString().TrimEnd();
    }

    private static string RenderReviewMarkdown(WorkflowReviewReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"# Workflow Review: {EscapeMarkdownText(report.Name)}");
        writer.WriteLine();
        writer.WriteLine($"- Can run: {(report.CanRun ? "yes" : "no")}");
        writer.WriteLine($"- Project directory: `{EscapeInlineCode(report.ProjectDirectory)}`");
        writer.WriteLine($"- Steps: {report.StepCount}");
        writer.WriteLine($"- Dry-run steps: {report.DryRunStepCount}");
        writer.WriteLine($"- Mutating steps: {report.MutatingStepCount}");
        writer.WriteLine($"- Approval-required steps: {report.ApprovalRequiredCount}");
        if (!string.IsNullOrWhiteSpace(report.Description))
            writer.WriteLine($"- Description: {EscapeMarkdownText(report.Description)}");

        writer.WriteLine();
        writer.WriteLine("## Pre-run Handoff");
        writer.WriteLine();
        writer.WriteLine("```powershell");
        foreach (var command in report.PreRunHandoffCommands)
            writer.WriteLine(command);
        writer.WriteLine("```");

        writer.WriteLine();
        writer.WriteLine("## Project Artifact Readiness");
        writer.WriteLine();
        if (report.ArtifactReadiness.Count == 0)
        {
            writer.WriteLine("- No local project artifacts were inferred from workflow steps.");
        }
        else
        {
            writer.WriteLine("| Artifact | Status | Count | Path | Matched steps | Review | Notes |");
            writer.WriteLine("|---|---|---:|---|---|---|---|");
            foreach (var artifact in report.ArtifactReadiness)
            {
                writer.WriteLine(
                    $"| `{artifact.Name}` | `{artifact.Status}` | {artifact.Count} | `{artifact.RelativePath}` | `{string.Join(", ", artifact.MatchedSteps)}` | `{artifact.ReviewCommand}` | {EscapeMarkdownText(artifact.Notes)} |");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Recommended Commands");
        writer.WriteLine();
        writer.WriteLine("```powershell");
        foreach (var command in report.RecommendedCommands)
            writer.WriteLine(command);
        writer.WriteLine("```");

        writer.WriteLine();
        writer.WriteLine("## Post-run Receipt Triage");
        writer.WriteLine();
        writer.WriteLine("```powershell");
        foreach (var command in report.PostRunReceiptCommands)
            writer.WriteLine(command);
        writer.WriteLine("```");

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
        writer.WriteLine("## Evidence");
        if (report.Evidence.Count == 0)
        {
            writer.WriteLine("- No built-in acceptance evidence matched this workflow name.");
        }
        else
        {
            foreach (var evidence in report.Evidence)
                writer.WriteLine($"- {EscapeMarkdownText(evidence)}");
        }

        writer.WriteLine();
        writer.WriteLine("## Handoff Notes");
        foreach (var note in report.HandoffNotes)
            writer.WriteLine($"- {EscapeMarkdownText(note)}");

        return writer.ToString().TrimEnd();
    }

    private static string RenderRegistryTable(WorkflowRegistryReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Workflow registry");
        writer.WriteLine($"Project: {report.ProjectDirectory}");
        writer.WriteLine($"Workflow root: {report.WorkflowRoot}");
        writer.WriteLine($"Schema: {report.SchemaVersion}");
        writer.WriteLine($"Workflows: {report.WorkflowCount}; valid: {report.ValidWorkflowCount}; invalid: {report.InvalidWorkflowCount}; mutating: {report.MutatingWorkflowCount}; dryRunCommands={report.DryRunCommandCount}; approvalRequired={report.ApprovalRequiredStepCount}; rollbackSupported={report.RollbackSupportedWorkflowCount}");

        if (report.Workflows.Count == 0)
        {
            writer.WriteLine(report.Exists
                ? "No local workflow YAML files found."
                : "No local workflow directory found.");
        }
        else
        {
            writer.WriteLine("Entries:");
            foreach (var workflow in report.Workflows)
            {
                writer.WriteLine(
                    $"  {(workflow.CanRun ? "OK" : "FAIL"),-4} {workflow.Name} risk={workflow.RiskLevel} steps={workflow.StepCount} scope={string.Join(",", workflow.ReadWriteScope)} rollback={(workflow.RollbackSupport ? "yes" : "no")} dryRuns={workflow.DryRunCommands.Count} approvals={workflow.ApprovalCommands.Count} receipts={string.Join(",", workflow.ReceiptSchemas)}");
                writer.WriteLine($"       inputs={string.Join(",", workflow.Inputs)} outputs={string.Join(",", workflow.Outputs)}");
                foreach (var dryRunCommand in workflow.DryRunCommands)
                    writer.WriteLine($"       dry-run command: {dryRunCommand}");
                foreach (var approvalCommand in workflow.ApprovalCommands)
                    writer.WriteLine($"       approval command: {approvalCommand}");
                writer.WriteLine($"       acceptance evidence={string.Join(",", workflow.AcceptanceEvidence)}");
            }
        }

        if (report.Issues.Count > 0)
        {
            writer.WriteLine("Issues:");
            foreach (var issue in report.Issues)
                writer.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderRegistryMarkdown(WorkflowRegistryReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Workflow Registry");
        writer.WriteLine();
        writer.WriteLine($"- Schema: `{EscapeInlineCode(report.SchemaVersion)}`");
        writer.WriteLine($"- Project directory: `{EscapeInlineCode(report.ProjectDirectory)}`");
        writer.WriteLine($"- Workflow root: `{EscapeInlineCode(report.WorkflowRoot)}`");
        writer.WriteLine($"- Exists: {(report.Exists ? "yes" : "no")}");
        writer.WriteLine($"- Success: {(report.Success ? "yes" : "no")}");
        writer.WriteLine($"- Workflows: {report.WorkflowCount}");
        writer.WriteLine($"- Valid workflows: {report.ValidWorkflowCount}");
        writer.WriteLine($"- Invalid workflows: {report.InvalidWorkflowCount}");
        writer.WriteLine($"- Dry-run commands: {report.DryRunCommandCount}");
        writer.WriteLine($"- Approval-required steps: {report.ApprovalRequiredStepCount}");
        writer.WriteLine($"- Rollback-supported workflows: {report.RollbackSupportedWorkflowCount}");
        writer.WriteLine();

        writer.WriteLine("## Workflows");
        if (report.Workflows.Count == 0)
        {
            writer.WriteLine(report.Exists
                ? "- No local workflow YAML files found."
                : "- No local workflow directory found.");
        }
        else
        {
            writer.WriteLine("| Workflow | Status | Risk level | Read/write scope | Inputs | Outputs | Dry-run commands | Approval commands | Rollback support | Receipt schema | Acceptance evidence |");
            writer.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var workflow in report.Workflows)
            {
                writer.WriteLine(
                    $"| {EscapeTableCell(workflow.Name)} | {(workflow.CanRun ? "OK" : "FAIL")} | `{EscapeInlineCode(workflow.RiskLevel)}` | `{EscapeInlineCode(string.Join(", ", workflow.ReadWriteScope))}` | {EscapeTableCell(string.Join(", ", workflow.Inputs))} | {EscapeTableCell(string.Join(", ", workflow.Outputs))} | {EscapeTableCell(string.Join("<br>", workflow.DryRunCommands))} | {EscapeTableCell(string.Join("<br>", workflow.ApprovalCommands))} | {(workflow.RollbackSupport ? "yes" : "no")} | `{EscapeInlineCode(string.Join(", ", workflow.ReceiptSchemas))}` | {EscapeTableCell(string.Join(", ", workflow.AcceptanceEvidence))} |");
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
                    $"- `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
            }
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
        int? exitCode,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? completedAtUtc = null,
        bool timedOut = false)
    {
        var result = new WorkflowRunStepResult
        {
            Index = step.Index,
            Name = step.Name,
            Mode = step.Mode,
            Run = step.Run,
            RequiresApproval = step.RequiresApproval,
            Status = status,
            ExitCode = exitCode,
            TimedOut = timedOut,
        };

        if (startedAtUtc.HasValue && completedAtUtc.HasValue)
        {
            result.StartedAtUtc = startedAtUtc.Value.ToString("o");
            result.CompletedAtUtc = completedAtUtc.Value.ToString("o");
            result.DurationMs = Math.Max(0, (long)(completedAtUtc.Value - startedAtUtc.Value).TotalMilliseconds);
        }

        return result;
    }

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
        writer.WriteLine($"Duration: {FormatDurationMs(report.DurationMs)}");
        if (report.TimeoutMs > 0)
            writer.WriteLine($"Step timeout: {FormatDurationMs(report.TimeoutMs)}");
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
            var durationText = step.DurationMs.HasValue ? $" duration={FormatDurationMs(step.DurationMs)}" : "";
            writer.WriteLine($"  {step.Index}. {step.Status.ToUpperInvariant()} [{step.Mode}] {step.Run}{exitText}{durationText}");
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
        writer.WriteLine($"- Duration: `{FormatDurationMs(report.DurationMs)}`");
        if (report.TimeoutMs > 0)
            writer.WriteLine($"- Step timeout: `{FormatDurationMs(report.TimeoutMs)}`");
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
            var durationText = step.DurationMs.HasValue ? $", duration={FormatDurationMs(step.DurationMs)}" : "";
            writer.WriteLine(
                $"- {step.Index}. `{EscapeInlineCode(step.Status.ToUpperInvariant())}` `{EscapeInlineCode(step.Mode)}`{exitText}{durationText}: `{EscapeInlineCode(step.Run)}`");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderReceiptsTable(WorkflowReceiptListReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Workflow receipts");
        writer.WriteLine($"Directory: {report.ReceiptDirectory}");
        writer.WriteLine($"Filter: {(report.FailedOnly ? "failed-only" : "all")}");
        if (!string.IsNullOrWhiteSpace(report.NameFilter))
            writer.WriteLine($"Workflow: {report.NameFilter}");
        if (report.MinDurationMs.HasValue)
            writer.WriteLine($"Min duration: {FormatDurationMs(report.MinDurationMs.Value)}");
        if (!string.IsNullOrWhiteSpace(report.Window))
            writer.WriteLine($"Window: {report.Window} since {report.SinceUtc}");
        writer.WriteLine($"Sort: {report.Sort}");
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
                    $"  {status,-4} exit={receipt.ExitCode,-3} steps={receipt.StepCount,-3} failed={receipt.FailedStepCount,-3} issues={receipt.IssueCount,-3} dur={FormatDurationMs(receipt.DurationMs),-8} {FormatReceiptTimestamp(receipt)} {receipt.Name} ({ShortPath(receipt.Path)})");
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
        if (!string.IsNullOrWhiteSpace(report.NameFilter))
            writer.WriteLine($"- Workflow: `{EscapeInlineCode(report.NameFilter)}`");
        if (report.MinDurationMs.HasValue)
            writer.WriteLine($"- Min duration: `{FormatDurationMs(report.MinDurationMs.Value)}`");
        if (!string.IsNullOrWhiteSpace(report.Window))
            writer.WriteLine($"- Window: `{EscapeInlineCode(report.Window)}` since `{EscapeInlineCode(report.SinceUtc)}`");
        writer.WriteLine($"- Sort: `{EscapeInlineCode(report.Sort)}`");
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
            writer.WriteLine("| Completed | Duration | Status | Exit | Steps | Failed | Issues | Workflow | Receipt |");
            writer.WriteLine("|---|---:|---|---:|---:|---:|---:|---|---|");
            foreach (var receipt in report.Receipts)
            {
                var status = receipt.Success ? "OK" : "FAIL";
                writer.WriteLine(
                    $"| {EscapeTableCell(FormatReceiptTimestamp(receipt))} | {FormatDurationMs(receipt.DurationMs)} | {status} | {receipt.ExitCode} | {receipt.StepCount} | {receipt.FailedStepCount} | {receipt.IssueCount} | {EscapeTableCell(receipt.Name)} | {EscapeTableCell(ShortPath(receipt.Path))} |");
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
        var completedAt = DateTimeOffset.UtcNow;
        report.CompletedAtUtc = completedAt.ToString("o");
        if (DateTimeOffset.TryParse(report.StartedAtUtc, out var startedAt))
            report.DurationMs = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);
        report.Success = report.CanRun && report.ExitCode == 0 &&
            report.Issues.All(issue => issue.Severity != WorkflowValidationSeverity.Error);
    }

    private static string FormatDurationMs(long? durationMs) =>
        durationMs.HasValue ? FormatDurationMs(durationMs.Value) : "-";

    private static string FormatDurationMs(long durationMs) =>
        $"{Math.Max(0, durationMs)}ms";

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
        long timeoutMs,
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
        if (timeoutMs > 0)
        {
            parts.Add("--timeout-ms");
            parts.Add(timeoutMs.ToString(CultureInfo.InvariantCulture));
        }

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

    private static string ProjectDirOption(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return string.Empty;
        }

        var projectDirectory = Path.GetFullPath(baseDirectory);
        var currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        return string.Equals(projectDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" --dir {QuoteArgument(projectDirectory)}";
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

    private static async Task<WorkflowStepExecutionResult> RunWorkflowStepAsync(
        WorkflowStepSimulation step,
        TextWriter output,
        long timeoutMs,
        Func<WorkflowStepSimulation, TextWriter, Task<int>>? runner)
    {
        if (runner == null)
            return await RunProcessWithTimeoutAsync(step, output, timeoutMs);

        if (timeoutMs <= 0)
            return new WorkflowStepExecutionResult(await runner(step, output), TimedOut: false);

        var runnerTask = runner(step, output);
        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(timeoutMs));
        var completed = await Task.WhenAny(runnerTask, timeoutTask);
        if (completed == runnerTask)
            return new WorkflowStepExecutionResult(await runnerTask, TimedOut: false);

        return new WorkflowStepExecutionResult(WorkflowTimeoutExitCode, TimedOut: true);
    }

    internal static async Task<int> RunProcessAsync(WorkflowStepSimulation step, TextWriter output)
    {
        var result = await RunProcessWithTimeoutAsync(step, output, timeoutMs: 0);
        return result.ExitCode;
    }

    private static async Task<WorkflowStepExecutionResult> RunProcessWithTimeoutAsync(
        WorkflowStepSimulation step,
        TextWriter output,
        long timeoutMs)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = WorkflowCommandLine.Tokenize(step.Run);
        }
        catch (FormatException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return new WorkflowStepExecutionResult(1, TimedOut: false);
        }

        if (tokens.Count == 0)
        {
            return new WorkflowStepExecutionResult(1, TimedOut: false);
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
                return new WorkflowStepExecutionResult(1, TimedOut: false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;
            try
            {
                if (timeoutMs > 0)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                    await process.WaitForExitAsync(cts.Token);
                }
                else
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between timeout and kill.
                }

                await process.WaitForExitAsync();
            }

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

            if (timedOut)
            {
                return new WorkflowStepExecutionResult(WorkflowTimeoutExitCode, TimedOut: true);
            }

            return new WorkflowStepExecutionResult(process.ExitCode, TimedOut: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await output.WriteLineAsync($"Error: failed to run workflow step {step.Index}: {ex.Message}");
            return new WorkflowStepExecutionResult(1, TimedOut: false);
        }
    }

    private static bool IsJson(string outputFormat) =>
        string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkdown(string outputFormat) =>
        string.Equals(outputFormat, "markdown", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkflowReportOutputFormat(string? outputFormat)
    {
        var normalized = (outputFormat ?? "table").Trim().ToLowerInvariant();
        return normalized is "table" or "json" or "markdown";
    }

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

    private sealed class WorkflowReviewReport
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "workflow-review.v1";

        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("projectDirectory")]
        public string ProjectDirectory { get; set; } = "";

        [JsonPropertyName("canRun")]
        public bool CanRun { get; set; }

        [JsonPropertyName("stepCount")]
        public int StepCount { get; set; }

        [JsonPropertyName("mutatingStepCount")]
        public int MutatingStepCount { get; set; }

        [JsonPropertyName("dryRunStepCount")]
        public int DryRunStepCount { get; set; }

        [JsonPropertyName("approvalRequiredCount")]
        public int ApprovalRequiredCount { get; set; }

        [JsonPropertyName("modeCounts")]
        public Dictionary<string, int> ModeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("issues")]
        public List<WorkflowValidationIssue> Issues { get; set; } = new();

        [JsonPropertyName("steps")]
        public List<WorkflowStepSimulation> Steps { get; set; } = new();

        [JsonPropertyName("artifactReadiness")]
        public List<WorkflowArtifactReadiness> ArtifactReadiness { get; set; } = new();

        [JsonPropertyName("preRunHandoffCommands")]
        public List<string> PreRunHandoffCommands { get; set; } = new();

        [JsonPropertyName("recommendedCommands")]
        public List<string> RecommendedCommands { get; set; } = new();

        [JsonPropertyName("postRunReceiptCommands")]
        public List<string> PostRunReceiptCommands { get; set; } = new();

        [JsonPropertyName("evidence")]
        public IReadOnlyList<string> Evidence { get; set; } = Array.Empty<string>();

        [JsonPropertyName("handoffNotes")]
        public List<string> HandoffNotes { get; set; } = new();
    }

    private sealed class WorkflowArtifactReadiness
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; } = "";

        [JsonPropertyName("reviewCommand")]
        public string ReviewCommand { get; set; } = "";

        [JsonPropertyName("workingDirectory")]
        public string WorkingDirectory { get; set; } = "";

        [JsonPropertyName("matchedSteps")]
        public IReadOnlyList<int> MatchedSteps { get; set; } = Array.Empty<int>();

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = "";
    }

    private sealed record WorkflowArtifactMetadata(
        string RelativePath,
        bool IsFile,
        IReadOnlyList<string> Patterns,
        string ReviewCommand,
        string Notes);

    private sealed record WorkflowRegistryDiscovery(
        string Root,
        bool Exists,
        IReadOnlyList<string> Files,
        IReadOnlyList<WorkflowValidationIssue> Issues);

    private sealed class WorkflowRegistryReport
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "workflow-registry.v1";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = "";

        [JsonPropertyName("projectDirectory")]
        public string ProjectDirectory { get; set; } = "";

        [JsonPropertyName("workflowRoot")]
        public string WorkflowRoot { get; set; } = "";

        [JsonPropertyName("exists")]
        public bool Exists { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("workflowCount")]
        public int WorkflowCount { get; set; }

        [JsonPropertyName("validWorkflowCount")]
        public int ValidWorkflowCount { get; set; }

        [JsonPropertyName("invalidWorkflowCount")]
        public int InvalidWorkflowCount { get; set; }

        [JsonPropertyName("readOnlyWorkflowCount")]
        public int ReadOnlyWorkflowCount { get; set; }

        [JsonPropertyName("dryRunWorkflowCount")]
        public int DryRunWorkflowCount { get; set; }

        [JsonPropertyName("mutatingWorkflowCount")]
        public int MutatingWorkflowCount { get; set; }

        [JsonPropertyName("approvalRequiredStepCount")]
        public int ApprovalRequiredStepCount { get; set; }

        [JsonPropertyName("dryRunCommandCount")]
        public int DryRunCommandCount { get; set; }

        [JsonPropertyName("rollbackSupportedWorkflowCount")]
        public int RollbackSupportedWorkflowCount { get; set; }

        [JsonPropertyName("workflows")]
        public List<WorkflowRegistryEntry> Workflows { get; set; } = new();

        [JsonPropertyName("issues")]
        public List<WorkflowValidationIssue> Issues { get; set; } = new();
    }

    private sealed class WorkflowRegistryEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("canRun")]
        public bool CanRun { get; set; }

        [JsonPropertyName("stepCount")]
        public int StepCount { get; set; }

        [JsonPropertyName("readOnlyStepCount")]
        public int ReadOnlyStepCount { get; set; }

        [JsonPropertyName("dryRunStepCount")]
        public int DryRunStepCount { get; set; }

        [JsonPropertyName("mutatingStepCount")]
        public int MutatingStepCount { get; set; }

        [JsonPropertyName("approvalRequiredCount")]
        public int ApprovalRequiredCount { get; set; }

        [JsonPropertyName("riskLevel")]
        public string RiskLevel { get; set; } = "";

        [JsonPropertyName("readWriteScope")]
        public IReadOnlyList<string> ReadWriteScope { get; set; } = Array.Empty<string>();

        [JsonPropertyName("inputs")]
        public IReadOnlyList<string> Inputs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("outputs")]
        public IReadOnlyList<string> Outputs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("dryRunCommands")]
        public IReadOnlyList<string> DryRunCommands { get; set; } = Array.Empty<string>();

        [JsonPropertyName("approvalCommands")]
        public IReadOnlyList<string> ApprovalCommands { get; set; } = Array.Empty<string>();

        [JsonPropertyName("rollbackSupport")]
        public bool RollbackSupport { get; set; }

        [JsonPropertyName("rollbackCommands")]
        public IReadOnlyList<string> RollbackCommands { get; set; } = Array.Empty<string>();

        [JsonPropertyName("receiptSchemas")]
        public IReadOnlyList<string> ReceiptSchemas { get; set; } = Array.Empty<string>();

        [JsonPropertyName("acceptanceEvidence")]
        public IReadOnlyList<string> AcceptanceEvidence { get; set; } = Array.Empty<string>();

        [JsonPropertyName("issues")]
        public List<WorkflowValidationIssue> Issues { get; set; } = new();
    }

    private sealed record WorkflowAcceptanceExample(
        [property: JsonPropertyName("workflow")] string Workflow,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("goal")] string Goal,
        [property: JsonPropertyName("previewCommands")] IReadOnlyList<string> PreviewCommands,
        [property: JsonPropertyName("approvalCommands")] IReadOnlyList<string> ApprovalCommands,
        [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence);

    private sealed record WorkflowStepExecutionResult(int ExitCode, bool TimedOut);
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
