using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Profile;

namespace RevitCli.Standards;

public static class StandardsInstaller
{
    private static readonly string[] WorkflowExtensions = { "*.yml", "*.yaml" };

    public static async Task<StandardsInstallResult> InstallAsync(
        string source,
        string projectDirectory,
        string? refSpec,
        string? subPath,
        bool force,
        bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("source must be provided.", nameof(source));
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new ArgumentException("projectDirectory must be provided.", nameof(projectDirectory));

        var projectRoot = Path.GetFullPath(projectDirectory);
        var prepared = await PrepareSourceAsync(source, refSpec, subPath);
        try
        {
            var layout = DiscoverLayout(prepared.RootDirectory, prepared.ManifestPath);
            var result = BuildPlan(source, projectRoot, layout, dryRun);
            Preflight(result, force);

            if (!dryRun)
            {
                Apply(result, force);
                result.Validation = StandardsValidator.Validate(
                    Path.Combine(projectRoot, StandardsValidator.DefaultManifestPath),
                    projectRoot);
            }

            return result;
        }
        finally
        {
            prepared.Dispose();
        }
    }

    private static async Task<PreparedSource> PrepareSourceAsync(
        string source,
        string? refSpec,
        string? subPath)
    {
        if (TryResolveExistingLocalSource(source, out var localSource))
        {
            var resolved = ApplyLocalSubPath(localSource, subPath);
            if (File.Exists(resolved))
            {
                return new PreparedSource(
                    Path.GetDirectoryName(resolved)!,
                    resolved,
                    cleanup: null);
            }

            return new PreparedSource(resolved, ManifestPath: null, cleanup: null);
        }

        var scratch = Path.Combine(Path.GetTempPath(), "revitcli-standards-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ProfileInstaller.InstallAsync(
                source,
                refSpec,
                subPath,
                scratch,
                force: true);
            return new PreparedSource(scratch, ManifestPath: null, cleanup: scratch);
        }
        catch
        {
            if (Directory.Exists(scratch))
            {
                try { DeleteDirectoryRobust(scratch); }
                catch { }
            }

            throw;
        }
    }

    private static bool TryResolveExistingLocalSource(string source, out string resolved)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            resolved = Path.GetFullPath(uri.LocalPath);
            return File.Exists(resolved) || Directory.Exists(resolved);
        }

        if (!source.Contains("://", StringComparison.Ordinal) &&
            !source.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            resolved = Path.GetFullPath(source);
            return File.Exists(resolved) || Directory.Exists(resolved);
        }

        resolved = "";
        return false;
    }

    private static string ApplyLocalSubPath(string localSource, string? subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath))
            return localSource;
        if (File.Exists(localSource))
            throw new InvalidOperationException("--subpath cannot be used when the source is a single file.");

        var root = Path.GetFullPath(localSource);
        var candidate = Path.GetFullPath(Path.Combine(root, NormalizeSubPath(subPath!)));
        var rootWithSep = WithTrailingSeparator(root);
        if (!candidate.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(candidate, root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"--subpath '{subPath}' resolves outside the standards source (refused for safety).");
        }

        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            throw new InvalidOperationException($"--subpath '{subPath}' does not exist inside standards source.");

        return candidate;
    }

    private static StandardsPackLayout DiscoverLayout(string rootDirectory, string? explicitManifestPath)
    {
        var manifest = explicitManifestPath;
        if (manifest == null)
        {
            manifest = FirstExisting(
                Path.Combine(rootDirectory, StandardsValidator.DefaultManifestPath),
                Path.Combine(rootDirectory, "standards.yml"),
                Path.Combine(rootDirectory, "standards.yaml"));
        }

        if (manifest == null)
        {
            throw new InvalidOperationException(
                "Standards pack must contain .revitcli/standards.yml, standards.yml, or standards.yaml.");
        }

        var profile = FirstExisting(
            Path.Combine(rootDirectory, ".revitcli.yml"),
            Path.Combine(rootDirectory, ".revitcli", ".revitcli.yml"));
        var workflowRoot = FirstExistingDirectory(
            Path.Combine(rootDirectory, ".revitcli", "workflows"),
            Path.Combine(rootDirectory, "workflows"));
        var workflows = workflowRoot == null
            ? Array.Empty<string>()
            : WorkflowExtensions
                .SelectMany(pattern => Directory.GetFiles(workflowRoot, pattern, SearchOption.TopDirectoryOnly))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();

        return new StandardsPackLayout(manifest, profile, workflows);
    }

    private static StandardsInstallResult BuildPlan(
        string source,
        string projectRoot,
        StandardsPackLayout layout,
        bool dryRun)
    {
        var result = new StandardsInstallResult
        {
            Source = source,
            ProjectDirectory = projectRoot,
            DryRun = dryRun,
        };

        AddFile(result, "manifest", layout.ManifestPath, Path.Combine(projectRoot, StandardsValidator.DefaultManifestPath));
        if (layout.ProfilePath != null)
        {
            AddFile(result, "profile", layout.ProfilePath, Path.Combine(projectRoot, ProfileLoader.FileName));
        }

        foreach (var workflow in layout.WorkflowPaths)
        {
            AddFile(
                result,
                "workflow",
                workflow,
                Path.Combine(projectRoot, ".revitcli", "workflows", Path.GetFileName(workflow)));
        }

        var manifest = StandardsValidator.LoadManifest(layout.ManifestPath);
        foreach (var outputPath in manifest.Required.OutputPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var target = ResolveUnderProject(projectRoot, outputPath);
            result.Changes.Add(new StandardsInstallChange(
                "directory",
                outputPath,
                target,
                Directory.Exists(target) ? "exists" : "create"));
        }

        return result;
    }

    private static void AddFile(
        StandardsInstallResult result,
        string kind,
        string sourcePath,
        string targetPath)
    {
        result.Changes.Add(new StandardsInstallChange(
            kind,
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(targetPath),
            File.Exists(targetPath) ? "overwrite" : "create"));
    }

    private static void Preflight(StandardsInstallResult result, bool force)
    {
        foreach (var change in result.Changes.Where(change => change.Kind != "directory"))
        {
            if (File.Exists(change.Target) && !force)
            {
                throw new InvalidOperationException(
                    $"Target file already exists: {change.Target}. Pass --force to overwrite.");
            }

            if (Directory.Exists(change.Target))
            {
                throw new InvalidOperationException(
                    $"Target path is a directory, expected a file: {change.Target}");
            }
        }
    }

    private static void Apply(StandardsInstallResult result, bool force)
    {
        foreach (var change in result.Changes)
        {
            if (change.Kind == "directory")
            {
                Directory.CreateDirectory(change.Target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(change.Target)!);
            File.Copy(change.Source, change.Target, overwrite: force);
        }
    }

    private static string ResolveUnderProject(string projectRoot, string path)
    {
        if (Path.IsPathFullyQualified(path))
            throw new InvalidOperationException($"Output path must be relative to the project: {path}");

        var candidate = Path.GetFullPath(Path.Combine(projectRoot, path));
        var rootWithSep = WithTrailingSeparator(projectRoot);
        if (!candidate.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(candidate, projectRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Output path escapes the project directory: {path}");
        }

        return candidate;
    }

    private static string? FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(File.Exists);

    private static string? FirstExistingDirectory(params string[] paths) =>
        paths.FirstOrDefault(Directory.Exists);

    private static string NormalizeSubPath(string subPath) =>
        subPath.Replace('/', Path.DirectorySeparatorChar)
               .Replace('\\', Path.DirectorySeparatorChar)
               .TrimStart(Path.DirectorySeparatorChar);

    private static string WithTrailingSeparator(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? full
            : full + Path.DirectorySeparatorChar;
    }

    private static void DeleteDirectoryRobust(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            catch (UnauthorizedAccessException) { }
            catch (FileNotFoundException) { }
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed record PreparedSource(
        string RootDirectory,
        string? ManifestPath,
        string? cleanup) : IDisposable
    {
        public void Dispose()
        {
            if (!string.IsNullOrWhiteSpace(cleanup) && Directory.Exists(cleanup))
            {
                DeleteDirectoryRobust(cleanup);
            }
        }
    }

    private sealed record StandardsPackLayout(
        string ManifestPath,
        string? ProfilePath,
        IReadOnlyList<string> WorkflowPaths);
}

public sealed class StandardsInstallResult
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("changes")]
    public List<StandardsInstallChange> Changes { get; set; } = new();

    [JsonPropertyName("validation")]
    public StandardsValidationReport? Validation { get; set; }
}

public sealed record StandardsInstallChange(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("action")] string Action);
