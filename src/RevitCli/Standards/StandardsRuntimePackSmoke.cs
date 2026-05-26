using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace RevitCli.Standards;

public static class StandardsRuntimePackSmoke
{
    public static StandardsRuntimePackSmokeResult Run(string sourceRoot)
    {
        var issues = new List<string>();
        var source = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(source))
        {
            return new StandardsRuntimePackSmokeResult(
                false,
                $"standards pack directory not found: {source}",
                new[] { $"standards pack directory not found: {source}" });
        }

        var projectRoot = Path.Combine(Path.GetTempPath(), "revitcli-standards-runtime-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, ".revitcli", "existing"));
            File.WriteAllText(Path.Combine(projectRoot, "existing-project.txt"), "keep");
            File.WriteAllText(Path.Combine(projectRoot, ".revitcli", "existing", "note.txt"), "keep nested");
            var beforeDryRun = SnapshotFileTree(projectRoot);
            var dryRun = StandardsInstaller.InstallAsync(
                    source,
                    projectRoot,
                    refSpec: null,
                    subPath: null,
                    force: false,
                    dryRun: true)
                .GetAwaiter()
                .GetResult();

            Require(dryRun.DryRun, "dry-run install result must report dryRun=true", issues);
            Require(dryRun.Changes.Count > 0, "dry-run install must produce changes", issues);
            Require(beforeDryRun.SequenceEqual(SnapshotFileTree(projectRoot)),
                "dry-run install must leave the final file-tree snapshot unchanged", issues);
            Require(!File.Exists(Path.Combine(projectRoot, ".revitcli", "standards.yml")),
                "dry-run install must not write standards.yml", issues);
            RequireHasKind(dryRun, "manifest", issues);
            RequireHasKind(dryRun, "profile", issues);
            RequireHasKind(dryRun, "workflow", issues);
            RequireHasKind(dryRun, "sheet-map", issues);
            RequireHasKind(dryRun, "numbering-rule", issues);

            var applied = StandardsInstaller.InstallAsync(
                    source,
                    projectRoot,
                    refSpec: null,
                    subPath: null,
                    force: false,
                    dryRun: false)
                .GetAwaiter()
                .GetResult();

            Require(applied.Validation?.Valid == true, "approved install must validate the installed pack", issues);
            Require(File.Exists(Path.Combine(projectRoot, ".revitcli", "standards.yml")),
                "approved install must copy standards.yml", issues);
            Require(File.Exists(Path.Combine(projectRoot, ".revitcli.yml")),
                "approved install must copy .revitcli.yml", issues);
            Require(File.Exists(Path.Combine(projectRoot, ".revitcli", "workflows", "pre-issue.yml")),
                "approved install must copy pre-issue workflow", issues);
            Require(File.Exists(Path.Combine(projectRoot, ".revitcli", "sheets", "issue-meta.yml")),
                "approved install must copy sheet map", issues);
            Require(File.Exists(Path.Combine(projectRoot, ".revitcli", "numbering", "rooms.yml")),
                "approved install must copy room numbering rule", issues);
            Require(File.Exists(Path.Combine(projectRoot, ".revitcli", "numbering", "doors.yml")),
                "approved install must copy door numbering rule", issues);
            Require(Directory.Exists(Path.Combine(projectRoot, "deliverables")),
                "approved install must create deliverables output directory", issues);

            var valid = issues.Count == 0;
            var evidence = valid
                ? $"standards install dry-run produced {dryRun.Changes.Count} changes with populated-target final file-tree snapshot evidence unchanged, and approved install produced {applied.Changes.Count} changes with validation OK"
                : string.Join("; ", issues);
            return new StandardsRuntimePackSmokeResult(valid, evidence, issues.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or LibGit2Sharp.LibGit2SharpException
                                       or YamlDotNet.Core.YamlException)
        {
            return new StandardsRuntimePackSmokeResult(false, ex.Message, new[] { ex.Message });
        }
        finally
        {
            DeleteDirectoryQuietly(projectRoot);
        }
    }

    private static void RequireHasKind(StandardsInstallResult result, string kind, List<string> issues)
    {
        Require(
            result.Changes.Any(change => string.Equals(change.Kind, kind, StringComparison.OrdinalIgnoreCase)),
            $"install plan must include {kind} change",
            issues);
    }

    private static void Require(bool condition, string message, List<string> issues)
    {
        if (!condition)
            issues.Add(message);
    }

    private static string[] SnapshotFileTree(string root)
    {
        if (!Directory.Exists(root))
            return Array.Empty<string>();

        return Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
                return Directory.Exists(path)
                    ? $"dir:{relativePath}"
                    : $"file:{relativePath}:{new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture)}:{ComputeSha256(path)}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed record StandardsRuntimePackSmokeResult(
    bool Success,
    string Evidence,
    IReadOnlyList<string> Issues);
