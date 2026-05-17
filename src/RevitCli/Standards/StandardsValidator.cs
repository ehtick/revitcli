using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitCli.Diagnostics;
using RevitCli.Families;
using RevitCli.Profile;
using RevitCli.Workflows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Standards;

public static class StandardsValidator
{
    public static readonly string DefaultManifestPath = Path.Combine(".revitcli", "standards.yml");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static StandardsValidationReport Validate(string manifestPath, string projectDirectory)
    {
        var manifestFullPath = Path.GetFullPath(manifestPath);
        var projectRoot = Path.GetFullPath(projectDirectory);
        var report = new StandardsValidationReport
        {
            ManifestPath = manifestFullPath,
            ProjectDirectory = projectRoot,
            CliVersion = AssemblyVersionReader.CurrentCliVersion(),
        };

        if (!File.Exists(manifestFullPath))
        {
            report.Issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "manifest",
                $"standards manifest not found: {manifestFullPath}"));
            report.Valid = false;
            return report;
        }

        StandardsManifest manifest;
        try
        {
            manifest = LoadManifest(manifestFullPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            report.Issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "manifest",
                ex.Message));
            report.Valid = false;
            return report;
        }

        report.Name = manifest.Name;
        report.Version = manifest.Version;
        report.PackVersion = manifest.PackVersion;
        report.Compatibility = manifest.Compatibility ?? new StandardsCompatibility();
        ValidateManifestShape(manifest, report.CliVersion, report.Issues);

        ProjectProfile? defaultProfile = null;
        var defaultProfilePath = Path.Combine(projectRoot, ProfileLoader.FileName);
        if (File.Exists(defaultProfilePath))
        {
            try
            {
                defaultProfile = ProfileLoader.Load(defaultProfilePath);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                report.Issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    ProfileLoader.FileName,
                    $"failed to load project profile: {ex.Message}"));
            }
        }

        ValidateProfiles(projectRoot, manifest.Required.Profiles, report.Issues);
        ValidateWorkflows(projectRoot, manifest.Required.Workflows, report.Issues);
        ValidateOutputPaths(projectRoot, manifest.Required.OutputPaths, report.Issues);
        ValidateScheduleTemplates(defaultProfile, manifest.Required.ScheduleTemplates, report.Issues);
        ValidateFamilyRules(manifest.Required.FamilyRules, report.Issues);

        report.Valid = report.Issues.All(issue => issue.Severity != StandardsValidationSeverity.Error);
        return report;
    }

    internal static StandardsManifest LoadManifest(string manifestPath) =>
        Deserializer.Deserialize<StandardsManifest>(File.ReadAllText(manifestPath))
        ?? throw new InvalidOperationException($"Failed to parse standards manifest: {manifestPath}");

    private static void ValidateManifestShape(
        StandardsManifest manifest,
        string cliVersion,
        List<StandardsValidationIssue> issues)
    {
        if (manifest.Version != 1)
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "version",
                $"standards manifest version must be 1, got {manifest.Version}."));
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Warning,
                "name",
                "standards manifest should include a name."));
        }

        if (string.IsNullOrWhiteSpace(manifest.PackVersion))
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Warning,
                "packVersion",
                "standards pack should include packVersion metadata."));
        }

        ValidateCompatibility(manifest.Compatibility, cliVersion, issues);
    }

    private static void ValidateCompatibility(
        StandardsCompatibility? compatibility,
        string cliVersion,
        List<StandardsValidationIssue> issues)
    {
        if (compatibility == null)
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Warning,
                "compatibility",
                "standards pack should include compatibility metadata."));
            return;
        }

        if (string.IsNullOrWhiteSpace(compatibility.RevitCli))
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Warning,
                "compatibility.revitCli",
                "standards pack should declare compatible RevitCli versions, for example '>=0.1.0'."));
        }
        else
        {
            ValidateRevitCliCompatibility(compatibility.RevitCli, cliVersion, issues);
        }

        for (var i = 0; i < compatibility.RevitYears.Count; i++)
        {
            var year = compatibility.RevitYears[i];
            if (year < 2024 || year > 2100)
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"compatibility.revitYears[{i}]",
                    $"unsupported Revit year in standards compatibility metadata: {year}."));
            }
        }

        for (var i = 0; i < compatibility.Notes.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(compatibility.Notes[i]))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Warning,
                    $"compatibility.notes[{i}]",
                    "compatibility note is empty."));
            }
        }
    }

    private static void ValidateRevitCliCompatibility(
        string requirement,
        string cliVersion,
        List<StandardsValidationIssue> issues)
    {
        var trimmed = requirement.Trim();
        if (!trimmed.StartsWith(">=", StringComparison.Ordinal))
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "compatibility.revitCli",
                "compatibility.revitCli currently supports only minimum-version requirements like '>=0.1.0'."));
            return;
        }

        var minimumText = trimmed[2..].Trim();
        if (!ComponentVersion.TryParse(minimumText, out var minimum))
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "compatibility.revitCli",
                $"compatibility.revitCli minimum version is invalid: {minimumText}."));
            return;
        }

        if (!ComponentVersion.TryParse(cliVersion, out var current))
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Warning,
                "compatibility.revitCli",
                $"current RevitCli version could not be parsed for compatibility check: {cliVersion}."));
            return;
        }

        if (CompareVersion(current, minimum) < 0)
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "compatibility.revitCli",
                $"standards pack requires RevitCli {requirement}, current version is {cliVersion}."));
        }
    }

    private static int CompareVersion(ComponentVersion current, ComponentVersion minimum)
    {
        var major = current.Major.CompareTo(minimum.Major);
        if (major != 0)
            return major;

        var minor = current.Minor.CompareTo(minimum.Minor);
        if (minor != 0)
            return minor;

        return current.Patch.CompareTo(minimum.Patch);
    }

    private static void ValidateProfiles(
        string projectRoot,
        IReadOnlyList<string> profiles,
        List<StandardsValidationIssue> issues)
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            var path = profiles[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.profiles[{i}]",
                    "profile path is empty."));
                continue;
            }

            var fullPath = ResolveUnderProject(projectRoot, path);
            if (!File.Exists(fullPath))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.profiles[{i}]",
                    $"profile not found: {path}"));
                continue;
            }

            try
            {
                _ = ProfileLoader.Load(fullPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.profiles[{i}]",
                    $"profile failed validation: {ex.Message}"));
            }
        }
    }

    private static void ValidateWorkflows(
        string projectRoot,
        IReadOnlyList<string> workflows,
        List<StandardsValidationIssue> issues)
    {
        for (var i = 0; i < workflows.Count; i++)
        {
            var name = workflows[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.workflows[{i}]",
                    "workflow name is empty."));
                continue;
            }

            var path = ResolveWorkflowPath(projectRoot, name);
            if (path == null)
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.workflows[{i}]",
                    $"workflow not found: {name}"));
                continue;
            }

            try
            {
                var loaded = WorkflowLoader.Load(path);
                var workflowIssues = WorkflowValidator.Validate(loaded)
                    .Where(issue => issue.Severity == WorkflowValidationSeverity.Error)
                    .ToList();
                foreach (var issue in workflowIssues)
                {
                    issues.Add(new StandardsValidationIssue(
                        StandardsValidationSeverity.Error,
                        $"required.workflows[{i}].{issue.Path}",
                        issue.Message));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or YamlDotNet.Core.YamlException)
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.workflows[{i}]",
                    $"workflow failed validation: {ex.Message}"));
            }
        }
    }

    private static void ValidateOutputPaths(
        string projectRoot,
        IReadOnlyList<string> outputPaths,
        List<StandardsValidationIssue> issues)
    {
        for (var i = 0; i < outputPaths.Count; i++)
        {
            var path = outputPaths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.outputPaths[{i}]",
                    "output path is empty."));
                continue;
            }

            if (!Directory.Exists(ResolveUnderProject(projectRoot, path)))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.outputPaths[{i}]",
                    $"output path not found: {path}"));
            }
        }
    }

    private static void ValidateScheduleTemplates(
        ProjectProfile? profile,
        IReadOnlyList<string> templates,
        List<StandardsValidationIssue> issues)
    {
        if (templates.Count == 0)
        {
            return;
        }

        if (profile == null)
        {
            issues.Add(new StandardsValidationIssue(
                StandardsValidationSeverity.Error,
                "required.scheduleTemplates",
                $"schedule templates require {ProfileLoader.FileName}."));
            return;
        }

        for (var i = 0; i < templates.Count; i++)
        {
            var name = templates[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.scheduleTemplates[{i}]",
                    "schedule template name is empty."));
                continue;
            }

            if (!profile.Schedules.ContainsKey(name))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.scheduleTemplates[{i}]",
                    $"schedule template not found in {ProfileLoader.FileName}: {name}"));
            }
        }
    }

    private static void ValidateFamilyRules(
        IReadOnlyList<string> familyRules,
        List<StandardsValidationIssue> issues)
    {
        for (var i = 0; i < familyRules.Count; i++)
        {
            var rule = familyRules[i];
            if (string.IsNullOrWhiteSpace(rule))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.familyRules[{i}]",
                    "family rule id is empty."));
                continue;
            }

            if (!FamilyValidator.AllRuleIds.Contains(rule, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new StandardsValidationIssue(
                    StandardsValidationSeverity.Error,
                    $"required.familyRules[{i}]",
                    $"unknown built-in family rule: {rule}. Available: {string.Join(", ", FamilyValidator.AllRuleIds)}"));
            }
        }
    }

    private static string? ResolveWorkflowPath(string projectRoot, string name)
    {
        var candidates = new List<string>();
        if (Path.HasExtension(name))
        {
            candidates.Add(ResolveUnderProject(projectRoot, name));
            candidates.Add(ResolveUnderProject(projectRoot, Path.Combine(WorkflowLoader.DefaultDirectory, name)));
        }
        else
        {
            candidates.Add(ResolveUnderProject(projectRoot, Path.Combine(WorkflowLoader.DefaultDirectory, name + ".yml")));
            candidates.Add(ResolveUnderProject(projectRoot, Path.Combine(WorkflowLoader.DefaultDirectory, name + ".yaml")));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ResolveUnderProject(string projectRoot, string path) =>
        Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));
}
