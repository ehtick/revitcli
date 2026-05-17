using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCli.Output;

public static class DeliveryBundlePlanner
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static DeliveryBundleReport Plan(string? projectDirectory, string? bundlePath)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var resolvedBundlePath = ResolveBundlePath(projectRoot, bundlePath);
        var receiptPath = resolvedBundlePath + ".receipt.json";
        var report = new DeliveryBundleReport
        {
            ProjectDirectory = projectRoot,
            BundlePath = resolvedBundlePath,
            ReceiptPath = receiptPath
        };

        var manifest = DeliveryManifestReader.Read(projectRoot);
        report.ManifestPath = manifest.ManifestPath;
        report.ManifestExists = manifest.Exists;
        report.EntryCount = manifest.EntryCount;
        report.ManifestValid = manifest.Valid;
        report.Issues.AddRange(manifest.Issues);

        if (!manifest.Exists)
        {
            report.Issues.Add(new DeliveryManifestIssue(
                null,
                "error",
                "manifest-missing",
                $"delivery manifest not found: {manifest.ManifestPath}"));
            return report;
        }

        AddFile(report, manifest.ManifestPath, "manifest", null);

        var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries.OrderBy(entry => entry.LineNumber))
        {
            if (entry.ReceiptReadable && !string.IsNullOrWhiteSpace(entry.ResolvedReceiptPath))
            {
                AddFile(report, entry.ResolvedReceiptPath, "receipt", entry.LineNumber);
                AddReceiptOutputs(report, entry, outputDirectories);
            }
        }

        return report;
    }

    public static void WriteBundle(DeliveryBundleReport report, bool force)
    {
        if (File.Exists(report.BundlePath) && !force)
        {
            report.Issues.Add(new DeliveryManifestIssue(
                null,
                "error",
                "bundle-exists",
                $"bundle already exists: {report.BundlePath}"));
            return;
        }

        try
        {
            var bundleDir = Path.GetDirectoryName(report.BundlePath);
            if (!string.IsNullOrWhiteSpace(bundleDir))
                Directory.CreateDirectory(bundleDir);

            if (File.Exists(report.BundlePath))
                File.Delete(report.BundlePath);

            using (var archive = ZipFile.Open(report.BundlePath, ZipArchiveMode.Create))
            {
                foreach (var file in report.Files)
                    archive.CreateEntryFromFile(file.SourcePath, file.ArchivePath, CompressionLevel.Optimal);
            }

            report.BundleWritten = true;
            report.WrittenAt = DateTime.UtcNow.ToString("o");

            var receipt = DeliveryBundleReceipt.From(report);
            var receiptDir = Path.GetDirectoryName(report.ReceiptPath);
            if (!string.IsNullOrWhiteSpace(receiptDir))
                Directory.CreateDirectory(receiptDir);
            File.WriteAllText(report.ReceiptPath, JsonSerializer.Serialize(receipt, JsonOpts));
            report.ReceiptWritten = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            report.Issues.Add(new DeliveryManifestIssue(
                null,
                "error",
                "bundle-write-failed",
                $"failed to write delivery bundle: {ex.Message}"));

            try
            {
                if (File.Exists(report.BundlePath))
                    File.Delete(report.BundlePath);
                report.BundleWritten = false;
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                report.Issues.Add(new DeliveryManifestIssue(
                    null,
                    "warning",
                    "bundle-cleanup-failed",
                    $"failed to remove partial bundle: {cleanupEx.Message}"));
            }
        }
    }

    private static void AddReceiptOutputs(
        DeliveryBundleReport report,
        DeliveryManifestEntry entry,
        ISet<string> outputDirectories)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(entry.ResolvedReceiptPath!));
            var root = document.RootElement;
            if (TryGetString(root, "outputDir", out var outputDir))
                AddOutputDirectory(report, outputDir, entry.LineNumber, outputDirectories);

            if (root.TryGetProperty("exports", out var exports) && exports.ValueKind == JsonValueKind.Array)
            {
                foreach (var export in exports.EnumerateArray())
                {
                    if (TryGetString(export, "outputDir", out var exportOutputDir))
                        AddOutputDirectory(report, exportOutputDir, entry.LineNumber, outputDirectories);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            report.Issues.Add(new DeliveryManifestIssue(
                entry.LineNumber,
                "error",
                "receipt-output-read-failed",
                $"failed to read receipt outputs: {ex.Message}"));
        }
    }

    private static void AddOutputDirectory(
        DeliveryBundleReport report,
        string outputDir,
        int lineNumber,
        ISet<string> outputDirectories)
    {
        var resolved = ResolvePath(report.ProjectDirectory, outputDir);
        if (!outputDirectories.Add(resolved))
            return;

        if (!Directory.Exists(resolved))
        {
            report.Issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "output-dir-missing",
                $"output directory not found: {resolved}"));
            return;
        }

        var files = Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories)
            .Where(path => !ContainsHiddenRevitCliDirectory(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            report.Issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "warning",
                "output-dir-empty",
                $"output directory has no deliverable files: {resolved}"));
            return;
        }

        foreach (var file in files)
            AddFile(report, file, "deliverable", lineNumber);
    }

    private static void AddFile(
        DeliveryBundleReport report,
        string path,
        string kind,
        int? lineNumber)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            report.Issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "bundle-file-missing",
                $"bundle file not found: {fullPath}"));
            return;
        }

        if (!report.AddedSourcePaths.Add(fullPath))
            return;

        var archivePath = CreateArchivePath(report, fullPath);
        var info = new FileInfo(fullPath);
        report.Files.Add(new DeliveryBundleFile
        {
            Kind = kind,
            SourcePath = fullPath,
            ArchivePath = archivePath,
            Bytes = info.Length,
            LineNumber = lineNumber
        });
    }

    private static string CreateArchivePath(DeliveryBundleReport report, string fullPath)
    {
        var relative = Path.GetRelativePath(report.ProjectDirectory, fullPath);
        string archivePath;
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            archivePath = NormalizeArchivePath(relative);
        }
        else
        {
            archivePath = NormalizeArchivePath(Path.Combine("external", HashPath(fullPath), Path.GetFileName(fullPath)));
        }

        if (report.AddedArchivePaths.Add(archivePath))
            return archivePath;

        var directory = Path.GetDirectoryName(archivePath)?.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(archivePath);
        var extension = Path.GetExtension(archivePath);
        for (var index = 2; ; index++)
        {
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? $"{name}-{index}{extension}"
                : $"{directory}/{name}-{index}{extension}";
            if (report.AddedArchivePaths.Add(candidate))
                return candidate;
        }
    }

    private static string ResolveBundlePath(string projectRoot, string? bundlePath)
    {
        if (!string.IsNullOrWhiteSpace(bundlePath))
        {
            return Path.IsPathRooted(bundlePath)
                ? Path.GetFullPath(bundlePath)
                : Path.GetFullPath(Path.Combine(projectRoot, bundlePath));
        }

        return Path.Combine(
            projectRoot,
            ".revitcli",
            "deliveries",
            "bundles",
            $"deliverables-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
    }

    private static string ResolvePath(string projectRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        value = "";
        return false;
    }

    private static bool ContainsHiddenRevitCliDirectory(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, ".revitcli", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string HashPath(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}

public sealed class DeliveryBundleReport
{
    [JsonIgnore]
    internal HashSet<string> AddedSourcePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    internal HashSet<string> AddedArchivePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "delivery-bundle.v1";

    [JsonPropertyName("success")]
    public bool Success => Valid && (!RequiresWrite || BundleWritten);

    [JsonPropertyName("valid")]
    public bool Valid => Issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("requiresWrite")]
    public bool RequiresWrite { get; set; }

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; set; } = "";

    [JsonPropertyName("manifestExists")]
    public bool ManifestExists { get; set; }

    [JsonPropertyName("manifestValid")]
    public bool ManifestValid { get; set; }

    [JsonPropertyName("bundlePath")]
    public string BundlePath { get; set; } = "";

    [JsonPropertyName("receiptPath")]
    public string ReceiptPath { get; set; } = "";

    [JsonPropertyName("bundleWritten")]
    public bool BundleWritten { get; set; }

    [JsonPropertyName("receiptWritten")]
    public bool ReceiptWritten { get; set; }

    [JsonPropertyName("writtenAt")]
    public string? WrittenAt { get; set; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount => Files.Count;

    [JsonPropertyName("receiptCount")]
    public int ReceiptCount => Files.Count(file => string.Equals(file.Kind, "receipt", StringComparison.OrdinalIgnoreCase));

    [JsonPropertyName("deliverableCount")]
    public int DeliverableCount => Files.Count(file => string.Equals(file.Kind, "deliverable", StringComparison.OrdinalIgnoreCase));

    [JsonPropertyName("totalBytes")]
    public long TotalBytes => Files.Sum(file => file.Bytes);

    [JsonPropertyName("files")]
    public List<DeliveryBundleFile> Files { get; } = new();

    [JsonPropertyName("issues")]
    public List<DeliveryManifestIssue> Issues { get; } = new();
}

public sealed class DeliveryBundleFile
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("archivePath")]
    public string ArchivePath { get; set; } = "";

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("lineNumber")]
    public int? LineNumber { get; set; }
}

public sealed record DeliveryBundleReceipt(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("projectDirectory")] string ProjectDirectory,
    [property: JsonPropertyName("manifestPath")] string ManifestPath,
    [property: JsonPropertyName("bundlePath")] string BundlePath,
    [property: JsonPropertyName("receiptPath")] string ReceiptPath,
    [property: JsonPropertyName("entryCount")] int EntryCount,
    [property: JsonPropertyName("fileCount")] int FileCount,
    [property: JsonPropertyName("receiptCount")] int ReceiptCount,
    [property: JsonPropertyName("deliverableCount")] int DeliverableCount,
    [property: JsonPropertyName("totalBytes")] long TotalBytes,
    [property: JsonPropertyName("files")] IReadOnlyList<DeliveryBundleFile> Files,
    [property: JsonPropertyName("issues")] IReadOnlyList<DeliveryManifestIssue> Issues,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("machine")] string Machine)
{
    public static DeliveryBundleReceipt From(DeliveryBundleReport report) =>
        new(
            "delivery-bundle-receipt.v1",
            "deliverables.bundle",
            report.Valid,
            false,
            report.ProjectDirectory,
            report.ManifestPath,
            report.BundlePath,
            report.ReceiptPath,
            report.EntryCount,
            report.FileCount,
            report.ReceiptCount,
            report.DeliverableCount,
            report.TotalBytes,
            report.Files,
            report.Issues,
            report.WrittenAt ?? DateTime.UtcNow.ToString("o"),
            Environment.UserName,
            Environment.MachineName);
}
