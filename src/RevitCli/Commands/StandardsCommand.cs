using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Standards;

namespace RevitCli.Commands;

public static class StandardsCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Command Create()
    {
        var command = new Command("standards", "Install and validate local office standards requirements");
        command.AddCommand(CreateValidateCommand());
        command.AddCommand(CreateInstallCommand());
        return command;
    }

    private static Command CreateInstallCommand()
    {
        var sourceArg = new Argument<string>(
            "path-or-git-url",
            "Local standards pack path or git URL");
        var dirOpt = new Option<string?>("--dir", "Project directory to install into; defaults to current directory");
        var refOpt = new Option<string?>("--ref", "Branch, tag, or commit SHA for git sources");
        var subPathOpt = new Option<string?>("--subpath", "Path inside the source pack or git repository");
        var forceOpt = new Option<bool>("--force", "Overwrite existing standards/profile/workflow files");
        var dryRunOpt = new Option<bool>("--dry-run", "Show files and directories that would be installed");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("install", "Install an approved standards pack into the local project")
        {
            sourceArg,
            dirOpt,
            refOpt,
            subPathOpt,
            forceOpt,
            dryRunOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string source,
            string? dir,
            string? refSpec,
            string? subPath,
            bool force,
            bool dryRun,
            string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteInstallAsync(
                source,
                dir,
                refSpec,
                subPath,
                force,
                dryRun,
                outputFormat,
                Console.Out);
        }, sourceArg, dirOpt, refOpt, subPathOpt, forceOpt, dryRunOpt, outputOpt);

        return command;
    }

    private static Command CreateValidateCommand()
    {
        var manifestOpt = new Option<string?>(
            "--manifest",
            $"Standards manifest path (default: {StandardsValidator.DefaultManifestPath})");
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var command = new Command("validate", "Validate local profile, workflow, output, schedule, and family-rule standards")
        {
            manifestOpt,
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string? manifest, string? dir, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteValidateAsync(manifest, dir, outputFormat, Console.Out);
        }, manifestOpt, dirOpt, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteInstallAsync(
        string source,
        string? projectDirectory,
        string? refSpec,
        string? subPath,
        bool force,
        bool dryRun,
        string outputFormat,
        TextWriter output)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);

        StandardsInstallResult result;
        try
        {
            result = await StandardsInstaller.InstallAsync(
                source,
                projectRoot,
                refSpec,
                subPath,
                force,
                dryRun);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or LibGit2Sharp.LibGit2SharpException
                                       or YamlDotNet.Core.YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOpts));
        }
        else if (string.Equals(outputFormat, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderInstallMarkdown(result));
        }
        else
        {
            await output.WriteLineAsync(RenderInstallTable(result));
        }

        return result.Validation?.Valid == false ? 1 : 0;
    }

    public static async Task<int> ExecuteValidateAsync(
        string? manifestPath,
        string? projectDirectory,
        string outputFormat,
        TextWriter output)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var resolvedManifest = string.IsNullOrWhiteSpace(manifestPath)
            ? Path.Combine(projectRoot, StandardsValidator.DefaultManifestPath)
            : ResolvePath(projectRoot, manifestPath!);

        var report = StandardsValidator.Validate(resolvedManifest, projectRoot);
        if (string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOpts));
        }
        else if (string.Equals(outputFormat, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(RenderMarkdown(report));
        }
        else
        {
            await output.WriteLineAsync(RenderTable(report));
        }

        return report.Valid ? 0 : 1;
    }

    private static string RenderTable(StandardsValidationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Standards validation");
        sb.AppendLine($"Manifest: {report.ManifestPath}");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        if (!string.IsNullOrWhiteSpace(report.Name))
        {
            sb.AppendLine($"Name: {report.Name}");
        }
        if (!string.IsNullOrWhiteSpace(report.PackVersion))
        {
            sb.AppendLine($"Pack version: {report.PackVersion}");
        }
        if (!string.IsNullOrWhiteSpace(report.Compatibility.RevitCli)
            || report.Compatibility.RevitYears.Count > 0)
        {
            var revitCli = string.IsNullOrWhiteSpace(report.Compatibility.RevitCli)
                ? "(unspecified)"
                : report.Compatibility.RevitCli;
            var years = report.Compatibility.RevitYears.Count == 0
                ? "(unspecified)"
                : string.Join(", ", report.Compatibility.RevitYears);
            sb.AppendLine($"Compatibility: RevitCli {revitCli}; Revit {years}");
        }
        foreach (var note in report.Compatibility.Notes.Where(note => !string.IsNullOrWhiteSpace(note)).Take(3))
        {
            sb.AppendLine($"Compatibility note: {note}");
        }

        sb.AppendLine($"Status: {(report.Valid ? "OK" : "FAIL")}");

        if (report.Issues.Count == 0)
        {
            sb.AppendLine("No issues.");
            return sb.ToString().TrimEnd();
        }

        foreach (var issue in report.Issues
                     .OrderByDescending(issue => issue.Severity)
                     .ThenBy(issue => issue.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderMarkdown(StandardsValidationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Standards Validation");
        sb.AppendLine();
        sb.AppendLine($"- Manifest: `{EscapeInlineCode(report.ManifestPath)}`");
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        if (!string.IsNullOrWhiteSpace(report.Name))
            sb.AppendLine($"- Name: `{EscapeInlineCode(report.Name)}`");
        if (!string.IsNullOrWhiteSpace(report.PackVersion))
            sb.AppendLine($"- Pack version: `{EscapeInlineCode(report.PackVersion)}`");
        sb.AppendLine($"- Status: {(report.Valid ? "`OK`" : "`FAIL`")}");
        sb.AppendLine($"- RevitCli version: `{EscapeInlineCode(report.CliVersion)}`");
        AppendCompatibilityMarkdown(sb, report.Compatibility);

        sb.AppendLine();
        sb.AppendLine("## Issues");
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues
                         .OrderByDescending(issue => issue.Severity)
                         .ThenBy(issue => issue.Path, StringComparer.Ordinal))
            {
                sb.AppendLine(
                    $"- `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderInstallTable(StandardsInstallResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.DryRun ? "Standards install plan" : "Standards install");
        sb.AppendLine($"Source: {result.Source}");
        sb.AppendLine($"Project: {result.ProjectDirectory}");

        if (result.Changes.Count == 0)
        {
            sb.AppendLine("No files or directories found to install.");
        }
        else
        {
            foreach (var change in result.Changes
                         .OrderBy(change => change.Kind, StringComparer.Ordinal)
                         .ThenBy(change => change.Target, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {change.Action.ToUpperInvariant(),-9} {change.Kind,-9} {change.Target}");
            }
        }

        if (result.Validation != null)
        {
            sb.AppendLine($"Validation: {(result.Validation.Valid ? "OK" : "FAIL")}");
            foreach (var issue in result.Validation.Issues
                         .OrderByDescending(issue => issue.Severity)
                         .ThenBy(issue => issue.Path, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {issue.Severity.ToString().ToUpperInvariant()} {issue.Path}: {issue.Message}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderInstallMarkdown(StandardsInstallResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.DryRun ? "# Standards Install Plan" : "# Standards Install");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{EscapeInlineCode(result.Source)}`");
        sb.AppendLine($"- Project: `{EscapeInlineCode(result.ProjectDirectory)}`");
        sb.AppendLine($"- Dry run: {(result.DryRun ? "yes" : "no")}");

        sb.AppendLine();
        sb.AppendLine("## Changes");
        if (result.Changes.Count == 0)
        {
            sb.AppendLine("- No files or directories found to install.");
        }
        else
        {
            foreach (var change in result.Changes
                         .OrderBy(change => change.Kind, StringComparer.Ordinal)
                         .ThenBy(change => change.Target, StringComparer.Ordinal))
            {
                sb.AppendLine(
                    $"- `{EscapeInlineCode(change.Action.ToUpperInvariant())}` `{EscapeInlineCode(change.Kind)}` `{EscapeInlineCode(change.Target)}`");
            }
        }

        if (result.Validation != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Validation");
            sb.AppendLine($"- Status: {(result.Validation.Valid ? "`OK`" : "`FAIL`")}");
            if (result.Validation.Issues.Count == 0)
            {
                sb.AppendLine("- Issues: none.");
            }
            else
            {
                foreach (var issue in result.Validation.Issues
                             .OrderByDescending(issue => issue.Severity)
                             .ThenBy(issue => issue.Path, StringComparer.Ordinal))
                {
                    sb.AppendLine(
                        $"- `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendCompatibilityMarkdown(StringBuilder sb, StandardsCompatibility compatibility)
    {
        if (string.IsNullOrWhiteSpace(compatibility.RevitCli)
            && compatibility.RevitYears.Count == 0
            && compatibility.Notes.Count == 0)
        {
            return;
        }

        sb.AppendLine("- Compatibility:");
        if (!string.IsNullOrWhiteSpace(compatibility.RevitCli))
            sb.AppendLine($"  - RevitCli: `{EscapeInlineCode(compatibility.RevitCli)}`");
        if (compatibility.RevitYears.Count > 0)
            sb.AppendLine($"  - Revit years: `{string.Join(", ", compatibility.RevitYears)}`");
        foreach (var note in compatibility.Notes.Where(note => !string.IsNullOrWhiteSpace(note)).Take(3))
            sb.AppendLine($"  - Note: {EscapeMarkdownText(note)}");
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

    private static string ResolvePath(string projectRoot, string path) =>
        Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));
}
