using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Workflows;

public static class WorkflowLoader
{
    public static readonly string DefaultDirectory = Path.Combine(".revitcli", "workflows");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<string> Discover(string? fileOrDirectory, string? baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(fileOrDirectory))
        {
            var path = ResolvePath(fileOrDirectory, baseDirectory);
            if (File.Exists(path))
            {
                return new[] { path };
            }

            if (Directory.Exists(path))
            {
                return EnumerateWorkflowFiles(path);
            }

            throw new FileNotFoundException($"Workflow path not found: {path}");
        }

        var root = ResolvePath(DefaultDirectory, baseDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Workflow directory not found: {root}. Create .revitcli/workflows/*.yml or pass a workflow file.");
        }

        var files = EnumerateWorkflowFiles(root);
        if (files.Count == 0)
        {
            throw new FileNotFoundException($"No workflow YAML files found in {root}.");
        }

        return files;
    }

    public static LoadedWorkflow Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Workflow not found: {fullPath}");
        }

        var yaml = File.ReadAllText(fullPath);
        var workflow = Deserializer.Deserialize<WorkflowDefinition>(yaml)
            ?? throw new InvalidOperationException($"Failed to parse workflow: {fullPath}");

        workflow.Steps ??= new List<WorkflowStep>();
        foreach (var step in workflow.Steps)
        {
            if (step != null)
            {
                step.Name = string.IsNullOrWhiteSpace(step.Name) ? null : step.Name.Trim();
                step.Run = step.Run?.Trim() ?? "";
                step.Mode = step.Mode?.Trim() ?? "";
            }
        }

        return new LoadedWorkflow(fullPath, workflow);
    }

    private static string ResolvePath(string path, string? baseDirectory)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory ?? Directory.GetCurrentDirectory(), path));
    }

    private static List<string> EnumerateWorkflowFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.yml")
            .Concat(Directory.EnumerateFiles(directory, "*.yaml"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
