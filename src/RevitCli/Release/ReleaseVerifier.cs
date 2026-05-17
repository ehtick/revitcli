using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RevitCli.Release;

internal static partial class ReleaseVerifier
{
    private const string SemVerPattern =
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$";

    public static ReleaseVerifyReport Verify(ReleaseVerifyOptions options)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.Root)
            ? Directory.GetCurrentDirectory()
            : options.Root);
        var report = new ReleaseVerifyReport
        {
            Root = root,
            Tag = NormalizeTag(options.Tag),
            Strict = options.Strict,
        };

        CheckRequiredFiles(root, report);
        CheckVersion(root, report);
        CheckChangelog(root, report);
        CheckReadme(root, report);
        CheckUbuntuCi(root, report);
        CheckReleaseWorkflow(root, report);
        CheckPublishWorkflow(root, report);
        CheckInstaller(root, report);
        CheckSmokeDocs(root, report);

        report.ErrorCount = report.Checks.Count(check => check.Status == ReleaseVerifyStatus.Error);
        report.WarningCount = report.Checks.Count(check => check.Status == ReleaseVerifyStatus.Warning);
        report.Success = report.ErrorCount == 0 && (!options.Strict || report.WarningCount == 0);
        return report;
    }

    private static void CheckRequiredFiles(string root, ReleaseVerifyReport report)
    {
        var required = new[]
        {
            "Directory.Build.props",
            "CHANGELOG.md",
            "README.md",
            "docs/release-checklist.md",
            "docs/architect-terminal-vision.md",
            "docs/revit2026-real-smoke.md",
            "docs/revit-version-compatibility.md",
            ".github/workflows/ci.yml",
            ".github/workflows/release.yml",
            ".github/workflows/publish.yml",
            "scripts/install.ps1",
            "scripts/smoke-revit.ps1",
        };

        foreach (var relative in required)
        {
            var exists = File.Exists(Path.Combine(root, ToNativePath(relative)));
            report.Add(
                $"file:{relative}",
                exists ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
                exists ? $"Found {relative}." : $"Missing required release file: {relative}.",
                relative);
        }
    }

    private static void CheckVersion(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, "Directory.Build.props");
        if (!File.Exists(path))
            return;

        string? version;
        try
        {
            version = XDocument.Load(path)
                .Descendants("RevitCliVersion")
                .FirstOrDefault()
                ?.Value
                .Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            report.Add("version:props-readable", ReleaseVerifyStatus.Error,
                $"Cannot read Directory.Build.props: {ex.Message}", "Directory.Build.props");
            return;
        }

        report.Version = version;
        if (string.IsNullOrWhiteSpace(version))
        {
            report.Add("version:present", ReleaseVerifyStatus.Error,
                "Directory.Build.props does not define RevitCliVersion.", "Directory.Build.props");
            return;
        }

        report.Add(
            "version:semver",
            SemVerRegex().IsMatch(version) ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            SemVerRegex().IsMatch(version)
                ? $"RevitCliVersion is {version}."
                : $"RevitCliVersion must be SemVer, got '{version}'.",
            "Directory.Build.props");

        if (!string.IsNullOrWhiteSpace(report.Tag))
        {
            var tagVersion = report.Tag.TrimStart('v', 'V');
            var tagMatches = string.Equals(version, tagVersion, StringComparison.Ordinal);
            report.Add(
                "version:tag-match",
                tagMatches ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
                tagMatches
                    ? $"Tag {report.Tag} matches RevitCliVersion {version}."
                    : $"Tag {report.Tag} does not match RevitCliVersion {version}.",
                "Directory.Build.props");
        }
    }

    private static void CheckChangelog(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, "CHANGELOG.md");
        if (!File.Exists(path))
            return;

        var text = ReadText(path, report, "changelog:readable");
        if (text is null)
            return;

        report.Add(
            "changelog:unreleased",
            text.Contains("## [Unreleased]", StringComparison.OrdinalIgnoreCase)
                ? ReleaseVerifyStatus.Ok
                : ReleaseVerifyStatus.Error,
            "CHANGELOG.md should keep a ## [Unreleased] section.",
            "CHANGELOG.md");

        report.Add(
            "changelog:release-notes",
            MentionsReleaseTrain(text, report.Version)
                ? ReleaseVerifyStatus.Ok
                : ReleaseVerifyStatus.Warning,
            "CHANGELOG.md should mention the current release train or version.",
            "CHANGELOG.md");

        if (!string.IsNullOrWhiteSpace(report.Tag))
        {
            var tagVersion = report.Tag.TrimStart('v', 'V');
            var hasTagSection =
                text.Contains($"## [{tagVersion}]", StringComparison.OrdinalIgnoreCase)
                || text.Contains($"## {tagVersion}", StringComparison.OrdinalIgnoreCase);
            report.Add(
                "changelog:tag-section",
                hasTagSection ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Warning,
                hasTagSection
                    ? $"CHANGELOG.md has a section for {tagVersion}."
                    : $"CHANGELOG.md has no released section for {tagVersion}; move Unreleased notes before tagging.",
                "CHANGELOG.md");
        }
    }

    private static void CheckReadme(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, "README.md");
        if (!File.Exists(path))
            return;

        var text = ReadText(path, report, "readme:readable");
        if (text is null)
            return;

        AddContains(report, "readme:release-checklist", text,
            "docs/release-checklist.md", "README links to the release checklist.", "README.md");
        AddContains(report, "readme:version-source", text,
            "RevitCliVersion", "README tells releasers to update RevitCliVersion.", "README.md");
        AddContains(report, "readme:real-smoke", text,
            "docs/revit2026-real-smoke.md", "README links to real Revit smoke evidence.", "README.md");

        report.Add(
            "readme:tag-placeholder",
            text.Contains("git tag v1.5.0", StringComparison.OrdinalIgnoreCase)
                ? ReleaseVerifyStatus.Warning
                : ReleaseVerifyStatus.Ok,
            "README should use a placeholder tag flow instead of an old concrete tag.",
            "README.md");
    }

    private static void CheckUbuntuCi(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, ToNativePath(".github/workflows/ci.yml"));
        if (!File.Exists(path))
            return;

        var text = ReadText(path, report, "ci:readable");
        if (text is null)
            return;

        AddContains(report, "ci:ubuntu", text, "ubuntu-latest",
            "Ubuntu CI runs on ubuntu-latest.", ".github/workflows/ci.yml");
        AddContains(report, "ci:portable-tests", text, "tests/RevitCli.Tests/RevitCli.Tests.csproj",
            "Ubuntu CI runs the portable CLI/Shared test project.", ".github/workflows/ci.yml");
        AddContains(report, "ci:release-verify", text, "release verify",
            "Ubuntu CI runs release verify guardrails.", ".github/workflows/ci.yml");
        AddNotContains(report, "ci:no-addin-build", text, "src/RevitCli.Addin",
            "Ubuntu CI does not build the Windows/Revit add-in.", ".github/workflows/ci.yml");
        AddNotContains(report, "ci:no-addin-tests", text, "tests/RevitCli.Addin.Tests",
            "Ubuntu CI does not run add-in tests.", ".github/workflows/ci.yml");
        AddNotContains(report, "ci:no-real-smoke", text, "smoke-revit",
            "Ubuntu CI does not run real Revit smoke.", ".github/workflows/ci.yml");
        AddNotContains(report, "ci:no-revit-api", text, "RevitAPI",
            "Ubuntu CI does not depend on local Revit API DLLs.", ".github/workflows/ci.yml");
    }

    private static void CheckReleaseWorkflow(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, ToNativePath(".github/workflows/release.yml"));
        if (!File.Exists(path))
            return;

        var text = ReadText(path, report, "release-workflow:readable");
        if (text is null)
            return;

        AddContains(report, "release-workflow:tag-trigger", text, "tags:",
            "Release workflow is tag-triggered.", ".github/workflows/release.yml");
        AddContains(report, "release-workflow:self-hosted-windows", text, "self-hosted",
            "Release workflow uses a self-hosted runner.", ".github/workflows/release.yml");
        AddContains(report, "release-workflow:windows", text, "windows",
            "Release workflow runs on Windows.", ".github/workflows/release.yml");
        foreach (var year in new[] { "2024", "2025", "2026" })
        {
            AddContains(report, $"release-workflow:addin-{year}", text, $"RevitYear={year}",
                $"Release workflow builds the Revit {year} add-in.", ".github/workflows/release.yml");
        }

        AddContains(report, "release-workflow:checksum", text, "SHA256SUMS.txt",
            "Release workflow writes SHA256SUMS.txt.", ".github/workflows/release.yml");
        AddContains(report, "release-workflow:github-release", text, "action-gh-release",
            "Release workflow uploads GitHub release assets.", ".github/workflows/release.yml");
    }

    private static void CheckPublishWorkflow(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, ToNativePath(".github/workflows/publish.yml"));
        if (!File.Exists(path))
            return;

        var text = ReadText(path, report, "publish-workflow:readable");
        if (text is null)
            return;

        AddContains(report, "publish-workflow:tag-trigger", text, "tags:",
            "NuGet publish workflow is tag-triggered.", ".github/workflows/publish.yml");
        AddContains(report, "publish-workflow:pack-cli", text, "dotnet pack src/RevitCli",
            "NuGet publish workflow packs the CLI project.", ".github/workflows/publish.yml");
        AddContains(report, "publish-workflow:nuget-secret", text, "NUGET_API_KEY",
            "NuGet publish workflow uses the NUGET_API_KEY secret.", ".github/workflows/publish.yml");
    }

    private static void CheckInstaller(string root, ReleaseVerifyReport report)
    {
        var path = Path.Combine(root, ToNativePath("scripts/install.ps1"));
        if (!File.Exists(path))
            return;

        var text = ReadText(path, report, "installer:readable");
        if (text is null)
            return;

        foreach (var year in new[] { "2024", "2025", "2026" })
        {
            AddContains(report, $"installer:override-{year}", text, $"Revit{year}InstallDir",
                $"Installer supports Revit {year} install-directory overrides.", "scripts/install.ps1");
        }

        AddContains(report, "installer:staged-addins", text, "staged",
            "Installer stages add-ins when Revit is running.", "scripts/install.ps1");
        AddContains(report, "installer:path-list-match", text, "Test-PathListContains",
            "Installer uses exact PATH list matching.", "scripts/install.ps1");
    }

    private static void CheckSmokeDocs(string root, ReleaseVerifyReport report)
    {
        var scriptPath = Path.Combine(root, ToNativePath("scripts/smoke-revit.ps1"));
        if (File.Exists(scriptPath))
        {
            var script = ReadText(scriptPath, report, "smoke-script:readable");
            if (script is not null)
            {
                foreach (var year in new[] { "2024", "2025", "2026" })
                {
                    AddContains(report, $"smoke-script:year-{year}", script, year,
                        $"Smoke script documents or supports Revit {year}.", "scripts/smoke-revit.ps1");
                }
            }
        }

        var compatibilityPath = Path.Combine(root, ToNativePath("docs/revit-version-compatibility.md"));
        if (File.Exists(compatibilityPath))
        {
            var compatibility = ReadText(compatibilityPath, report, "compatibility:readable");
            if (compatibility is not null)
            {
                foreach (var year in new[] { "2024", "2025", "2026" })
                {
                    AddContains(report, $"compatibility:year-{year}", compatibility, year,
                        $"Compatibility docs cover Revit {year}.", "docs/revit-version-compatibility.md");
                }
            }
        }

        var smokeDocPath = Path.Combine(root, ToNativePath("docs/revit2026-real-smoke.md"));
        if (File.Exists(smokeDocPath))
        {
            var smokeDoc = ReadText(smokeDocPath, report, "smoke-doc:readable");
            if (smokeDoc is not null)
            {
                AddContains(report, "smoke-doc:journal", smokeDoc, "journal",
                    "Real smoke doc includes journal evidence expectations.", "docs/revit2026-real-smoke.md");
            }
        }
    }

    private static void AddContains(
        ReleaseVerifyReport report,
        string id,
        string text,
        string needle,
        string okMessage,
        string path)
    {
        report.Add(
            id,
            text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                ? ReleaseVerifyStatus.Ok
                : ReleaseVerifyStatus.Error,
            text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                ? okMessage
                : $"Expected {path} to contain '{needle}'.",
            path);
    }

    private static void AddNotContains(
        ReleaseVerifyReport report,
        string id,
        string text,
        string needle,
        string okMessage,
        string path)
    {
        report.Add(
            id,
            text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                ? ReleaseVerifyStatus.Error
                : ReleaseVerifyStatus.Ok,
            text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                ? $"Expected {path} not to contain '{needle}'."
                : okMessage,
            path);
    }

    private static string? ReadText(string path, ReleaseVerifyReport report, string id)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Add(id, ReleaseVerifyStatus.Error,
                $"Cannot read {Path.GetFileName(path)}: {ex.Message}", path);
            return null;
        }
    }

    private static string ToNativePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    private static bool MentionsReleaseTrain(string text, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        if (text.Contains(version, StringComparison.OrdinalIgnoreCase))
            return true;

        var parts = version.Split('.');
        return parts.Length >= 2
            && text.Contains($"v{parts[0]}.{parts[1]}", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var trimmed = tag.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : "v" + trimmed;
    }

    [GeneratedRegex(SemVerPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SemVerRegex();
}

internal sealed class ReleaseVerifyOptions
{
    public string Root { get; init; } = "";
    public string? Tag { get; init; }
    public bool Strict { get; init; }
}

internal sealed class ReleaseVerifyReport
{
    public string SchemaVersion { get; init; } = "release-verify.v1";
    public string Root { get; init; } = "";
    public string? Version { get; set; }
    public string? Tag { get; init; }
    public bool Strict { get; init; }
    public bool Success { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<ReleaseVerifyCheck> Checks { get; } = new();

    public void Add(string id, ReleaseVerifyStatus status, string message, string? path)
    {
        Checks.Add(new ReleaseVerifyCheck
        {
            Id = id,
            Status = status,
            Message = message,
            Path = path,
        });
    }
}

internal sealed class ReleaseVerifyCheck
{
    public string Id { get; init; } = "";
    public ReleaseVerifyStatus Status { get; init; }
    public string Message { get; init; } = "";
    public string? Path { get; init; }
}

internal enum ReleaseVerifyStatus
{
    Ok,
    Warning,
    Error,
}
