using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RevitCli.Commands;
using RevitCli.Standards;
using RevitCli.Team;

namespace RevitCli.Release;

internal static partial class ReleaseVerifier
{
    private const string SemVerPattern =
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$";

    private static readonly string[] V60LocalWriteContradictions =
    {
        "may write",
        "can write",
        "will write",
        "also writes",
        "does write",
        "except it writes",
        "unless it writes",
        "write files when",
        "write model data when",
        "write registry cache",
    };

    private static readonly string[] V60RevitRuntimeContradictions =
    {
        "may start Revit",
        "can start Revit",
        "will start Revit",
        "also starts Revit",
        "does start Revit",
        "requires Revit to be running",
    };

    private static readonly string[] V60RevitApiContradictions =
    {
        "uses Revit API",
        "may call Revit API",
        "requires Revit API",
    };

    private static readonly string[] V60SaasContradictions =
    {
        "uses SaaS",
        "may use SaaS",
        "requires SaaS",
        "depends on SaaS",
        "calls SaaS",
    };

    private static readonly string[] V60McpContradictions =
    {
        "uses MCP",
        "may use MCP",
        "requires MCP",
        "depends on MCP",
    };

    private static readonly string[] V60LlmContradictions =
    {
        "uses built-in LLM",
        "may use built-in LLM",
        "requires built-in LLM",
        "built-in LLM parser is required",
    };

    private static readonly string[] V60DashboardCentralContradictions =
    {
        "uses dashboard-central",
        "may use dashboard-central",
        "requires dashboard-central",
    };

    private static readonly string[] V60DatabaseContradictions =
    {
        "uses database",
        "may use database",
        "requires database",
        "database-backed",
    };

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
        var workbenchVerify = new Lazy<WorkbenchVerifyRun>(() => RunWorkbenchVerify(root));

        CheckRequiredFiles(root, report);
        CheckVersion(root, report);
        CheckChangelog(root, report);
        CheckReadme(root, report);
        CheckUbuntuCi(root, report);
        CheckReleaseWorkflow(root, report);
        CheckPublishWorkflow(root, report);
        CheckInstaller(root, report);
        CheckSmokeDocs(root, report);
        CheckV5RcGate(root, report);
        CheckV54StandardsRuntimePack(root, report);
        CheckV55ViewCoordinationHygiene(root, report, workbenchVerify);
        CheckV56TeamPilotPack(root, report, workbenchVerify);
        CheckV60LocalBimOpsContract(root, report, workbenchVerify);

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
            "scripts/install-current-source-revit2026.ps1",
            "scripts/smoke-revit.ps1",
            "scripts/smoke-revit-wsl.sh",
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
        AddContains(report, "ci:workbench-verify", text, "workbench verify",
            "Ubuntu CI runs the v4 workbench contract verifier.", ".github/workflows/ci.yml");
        AddContains(report, "ci:workbench-v2-verify", text, "workbench-contract.v2",
            "Ubuntu CI runs the v5 workbench contract verifier.", ".github/workflows/ci.yml");
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
        AddContains(report, "release-workflow:addin-2026", text, "RevitYear=2026",
            "Release workflow builds the Revit 2026 add-in.", ".github/workflows/release.yml");
        AddNotContains(report, "release-workflow:no-addin-2024", text, "RevitYear=2024",
            "Release workflow does not package a Revit 2024 add-in for this release.", ".github/workflows/release.yml");
        AddNotContains(report, "release-workflow:no-addin-2025", text, "RevitYear=2025",
            "Release workflow does not package a Revit 2025 add-in for this release.", ".github/workflows/release.yml");
        AddContains(report, "release-workflow:revit2026-install-override", text, "REVITCLI_REVIT2026_INSTALL_DIR",
            "Release workflow supports a Revit 2026 install-directory override.", ".github/workflows/release.yml");

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

        AddContains(report, "publish-workflow:manual-trigger", text, "workflow_dispatch:",
            "NuGet publish workflow is manually triggered.", ".github/workflows/publish.yml");
        AddNotContains(report, "publish-workflow:no-tag-trigger", text, "tags:",
            "NuGet publish workflow does not run automatically on tag pushes.", ".github/workflows/publish.yml");
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

        var handoffPath = Path.Combine(root, ToNativePath("scripts/install-current-source-revit2026.ps1"));
        if (File.Exists(handoffPath))
        {
            var handoff = ReadText(handoffPath, report, "installer-current-source-handoff:readable");
            if (handoff is not null)
            {
                AddContains(report, "installer-current-source-handoff:install", handoff, "install.ps1",
                    "Current-source Revit 2026 handoff runs the source-tree installer.", "scripts/install-current-source-revit2026.ps1");
                AddContains(report, "installer-current-source-handoff:revit2026", handoff, "RevitYears = @(\"2026\")",
                    "Current-source Revit 2026 handoff targets the Revit 2026 add-in with named PowerShell splatting.", "scripts/install-current-source-revit2026.ps1");
                AddContains(report, "installer-current-source-handoff:force", handoff, "Force = $true",
                    "Current-source Revit 2026 handoff forces the source-tree install path with named PowerShell splatting.", "scripts/install-current-source-revit2026.ps1");
                AddContains(report, "installer-current-source-handoff:unc-snapshot", handoff, "current-source-snapshot",
                    "Current-source Revit 2026 handoff mirrors UNC source trees to a Windows-local snapshot before building.", "scripts/install-current-source-revit2026.ps1");
                AddContains(report, "installer-current-source-handoff:robocopy", handoff, "robocopy",
                    "Current-source Revit 2026 handoff uses robocopy for the Windows-local source snapshot.", "scripts/install-current-source-revit2026.ps1");
                report.Add(
                    "installer-current-source-handoff:named-splat",
                    handoff.Contains("\"-RevitYears\"", StringComparison.OrdinalIgnoreCase) ||
                    handoff.Contains("\"-Force\"", StringComparison.OrdinalIgnoreCase) ||
                    handoff.Contains("\"-AllowRunningRevit\"", StringComparison.OrdinalIgnoreCase)
                        ? ReleaseVerifyStatus.Error
                        : ReleaseVerifyStatus.Ok,
                    handoff.Contains("\"-RevitYears\"", StringComparison.OrdinalIgnoreCase) ||
                    handoff.Contains("\"-Force\"", StringComparison.OrdinalIgnoreCase) ||
                    handoff.Contains("\"-AllowRunningRevit\"", StringComparison.OrdinalIgnoreCase)
                        ? "Current-source Revit 2026 handoff must use named hashtable splatting, not quoted parameter-name strings."
                        : "Current-source Revit 2026 handoff avoids quoted parameter-name strings that PowerShell can pass as positional values.",
                    "scripts/install-current-source-revit2026.ps1");
                AddContains(report, "installer-current-source-handoff:verify", handoff, "scripts/smoke-revit-wsl.sh --require-current-source",
                    "Current-source Revit 2026 handoff prints the WSL live verification command.", "scripts/install-current-source-revit2026.ps1");
            }
        }
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

                AddContains(report, "smoke-script:v4-workbench", script, "V4Workbench",
                    "Real smoke script can run the v4 workbench and live discovery gate.", "scripts/smoke-revit.ps1");
                AddContains(report, "smoke-script:v4-workbench-verify", script, "workbench\", \"verify",
                    "Real smoke script runs workbench verify during v4 smoke.", "scripts/smoke-revit.ps1");
                AddContains(report, "smoke-script:v4-schedule-export", script, "schedule\", \"export",
                    "Real smoke script exercises live schedule export during v4 smoke.", "scripts/smoke-revit.ps1");
            }
        }

        var wslScriptPath = Path.Combine(root, ToNativePath("scripts/smoke-revit-wsl.sh"));
        if (File.Exists(wslScriptPath))
        {
            var wslScript = ReadText(wslScriptPath, report, "smoke-script-wsl:readable");
            if (wslScript is not null)
            {
                AddContains(report, "smoke-script-wsl:require-current-source", wslScript, "--require-current-source",
                    "WSL live smoke can require current-source installation.", "scripts/smoke-revit-wsl.sh");
                AddContains(report, "smoke-script-wsl:cli-commit", wslScript, "cliCommit",
                    "WSL live smoke records the installed Windows CLI commit.", "scripts/smoke-revit-wsl.sh");
                AddContains(report, "smoke-script-wsl:installed-addin-commit", wslScript, "installedAddinCommit",
                    "WSL live smoke records the installed add-in commit.", "scripts/smoke-revit-wsl.sh");
                AddContains(report, "smoke-script-wsl:live-addin-commit", wslScript, "liveAddinCommit",
                    "WSL live smoke records the live Revit add-in commit.", "scripts/smoke-revit-wsl.sh");
                AddContains(report, "smoke-script-wsl:status-addin-commit", wslScript, "statusAddinCommit",
                    "WSL live smoke records the status endpoint add-in commit.", "scripts/smoke-revit-wsl.sh");
                AddContains(report, "smoke-script-wsl:current-source-boundary", wslScript, "current_source_installed=false",
                    "WSL live smoke defaults current-source proof to false until every commit surface matches source HEAD.", "scripts/smoke-revit-wsl.sh");
                AddContains(report, "smoke-script-wsl:repair-handoff", wslScript, @".\scripts\install-current-source-revit2026.ps1",
                    "WSL live smoke repair handoff uses the current-source installer path.", "scripts/smoke-revit-wsl.sh");
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
                AddContains(report, "smoke-doc:v4-workbench", smokeDoc, "V4Workbench",
                    "Real smoke doc includes the v4 workbench smoke gate.", "docs/revit2026-real-smoke.md");
            }
        }
    }

    private static void CheckV5RcGate(string root, ReleaseVerifyReport report)
    {
        var readinessPath = Path.Combine(root, ToNativePath("docs/v5-rc-readiness.md"));
        if (!File.Exists(readinessPath))
        {
            report.Add("v5-rc:readiness-doc", ReleaseVerifyStatus.Error,
                "Missing docs/v5-rc-readiness.md; v5.0 RC boundaries are not disclosed.",
                "docs/v5-rc-readiness.md");
        }
        else
        {
            var readiness = ReadText(readinessPath, report, "v5-rc:readiness-readable");
            if (readiness is not null)
            {
                AddContains(report, "v5-rc:stable-p0-scope", readiness, "Stable P0 Commands",
                    "v5.0 RC stable P0 command scope is documented.", "docs/v5-rc-readiness.md");
                AddContains(report, "v5-rc:experimental-boundary", readiness, "Experimental / Deferred Commands",
                    "v5.0 RC experimental/deferred command boundary is documented.", "docs/v5-rc-readiness.md");
                AddContains(report, "v5-rc:workbench-check-ids", readiness, "v5RealSmokeDisclosure",
                    "v5.0 RC readiness references the v5 workbench evidence checks.", "docs/v5-rc-readiness.md");
                AddContains(report, "v5-rc:traceability-check-id", readiness, "issuePackageTraceability",
                    "v5.0 RC readiness references issue package traceability.", "docs/v5-rc-readiness.md");
                AddContains(report, "v5-rc:fault-check-id", readiness, "v5FaultInjectionCoverage",
                    "v5.0 RC readiness references fault-injection coverage.", "docs/v5-rc-readiness.md");
                AddContains(report, "v5-rc:strict-command", readiness, "release verify --strict",
                    "v5.0 RC readiness requires strict release verification.", "docs/v5-rc-readiness.md");

                var status = ParseV5RcStatus(readiness);
                report.Add(
                    "v5-rc:status",
                    status switch
                    {
                        "GO" => ReleaseVerifyStatus.Ok,
                        "NO-GO" => ReleaseVerifyStatus.Warning,
                        _ => ReleaseVerifyStatus.Error,
                    },
                    status switch
                    {
                        "GO" => "v5.0 RC readiness document declares GO.",
                        "NO-GO" => "v5.0 RC readiness document declares NO-GO; use --strict to block an RC handoff.",
                        _ => "docs/v5-rc-readiness.md must declare GO or NO-GO status.",
                    },
                    "docs/v5-rc-readiness.md");
            }
        }

        CheckV5SmokeDisclosure(root, report);

        var checklistPath = Path.Combine(root, ToNativePath("docs/release-checklist.md"));
        if (File.Exists(checklistPath))
        {
            var checklist = ReadText(checklistPath, report, "v5-rc:checklist-readable");
            if (checklist is not null)
            {
                AddContains(report, "v5-rc:fault-injection-checklist", checklist, "v5.0 Fault Injection Evidence",
                    "Release checklist includes v5.0 fault-injection evidence requirements.", "docs/release-checklist.md");
                AddContains(report, "v5-rc:strict-checklist", checklist, "release verify --strict",
                    "Release checklist documents strict RC verification.", "docs/release-checklist.md");
            }
        }
    }

    private static void CheckV5SmokeDisclosure(string root, ReleaseVerifyReport report)
    {
        var smokeRoot = Path.Combine(root, ToNativePath("docs/smoke/v5.0"));
        if (!Directory.Exists(smokeRoot))
        {
            report.Add("v5-rc:smoke-disclosure", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v5.0; v5.0 live-smoke evidence or gaps are not disclosed.",
                "docs/smoke/v5.0");
            return;
        }

        var knownYears = new[] { "2024", "2025", "2026" };
        var readinessPath = Path.Combine(root, ToNativePath("docs/v5-rc-readiness.md"));
        var readinessText = File.Exists(readinessPath) ? File.ReadAllText(readinessPath) : "";
        var claimedYears = ParseV5ClaimedYears(readinessText, knownYears);
        var liveYears = knownYears
            .Where(year => File.Exists(Path.Combine(smokeRoot, $"revit-{year}-issue-closure.md")))
            .ToArray();
        var missingClaimedYears = claimedYears
            .Where(year => !liveYears.Contains(year, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var unverifiedYears = knownYears
            .Where(year => !liveYears.Contains(year, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var gapReportPath = Path.Combine(smokeRoot, "gap-report.md");
        var gapReport = File.Exists(gapReportPath)
            ? ReadText(gapReportPath, report, "v5-rc:gap-report-readable") ?? ""
            : "";
        var undisclosedYears = unverifiedYears
            .Where(year => !GapReportDisclosesMissingSmoke(gapReport, year))
            .ToArray();

        if (undisclosedYears.Length > 0)
        {
            report.Add(
                "v5-rc:smoke-disclosure",
                ReleaseVerifyStatus.Error,
                $"v5.0 issue-closure smoke evidence is missing and not disclosed for Revit {string.Join(", Revit ", undisclosedYears)}.",
                "docs/smoke/v5.0/gap-report.md");
            return;
        }

        report.Add(
            "v5-rc:smoke-disclosure",
            ReleaseVerifyStatus.Ok,
            liveYears.Length == 0
                ? "v5.0 issue-closure live-smoke gaps are explicitly disclosed for all known years."
                : $"v5.0 issue-closure live-smoke evidence exists for Revit {string.Join(", Revit ", liveYears)}; claimed target years are Revit {string.Join(", Revit ", claimedYears)} and all other known-year gaps are disclosed.",
            "docs/smoke/v5.0");

        if (missingClaimedYears.Length > 0)
        {
            report.Add(
                "v5-rc:smoke-no-go",
                ReleaseVerifyStatus.Warning,
                $"v5.0 RC remains NO-GO for claimed live-support years until Revit {string.Join(", Revit ", missingClaimedYears)} issue-closure smoke evidence exists.",
                "docs/smoke/v5.0/gap-report.md");
        }
        else
        {
            report.Add(
                "v5-rc:smoke-no-go",
                ReleaseVerifyStatus.Ok,
                $"v5.0 issue-closure live-smoke evidence exists for every claimed target year: Revit {string.Join(", Revit ", claimedYears)}.",
                "docs/smoke/v5.0");
        }
    }

    private static string[] ParseV5ClaimedYears(string text, string[] knownYears)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!line.Contains("Claimed live Revit years", StringComparison.OrdinalIgnoreCase))
                continue;

            var years = Regex.Matches(line, @"20\d{2}")
                .Select(match => match.Value)
                .Where(year => knownYears.Contains(year, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (years.Length > 0)
                return years;
        }

        return knownYears;
    }

    private static string ParseV5RcStatus(string text)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!line.Contains("status", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.Contains("NO-GO", StringComparison.OrdinalIgnoreCase))
                return "NO-GO";
            if (line.Contains("GO", StringComparison.OrdinalIgnoreCase))
                return "GO";
        }

        return "";
    }

    private static bool GapReportDisclosesMissingSmoke(string gapReportText, string year) =>
        gapReportText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line =>
                line.Contains($"Revit {year}", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("not live verified", StringComparison.OrdinalIgnoreCase));

    private static void CheckV54StandardsRuntimePack(string root, ReleaseVerifyReport report)
    {
        var gapPath = Path.Combine(root, ToNativePath("docs/smoke/v5.4/gap-report.md"));
        if (!File.Exists(gapPath))
        {
            report.Add("v5.4:standards-runtime-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v5.4/gap-report.md; v5.4 standards runtime pack gates are not disclosed.",
                "docs/smoke/v5.4/gap-report.md");
            return;
        }

        var gap = ReadText(gapPath, report, "v5.4:standards-runtime-doc-readable");
        if (gap is not null)
        {
            AddContains(report, "v5.4:standards-runtime-doc", gap, "v5.4 Standards Runtime Pack",
                "v5.4 standards runtime pack gap report is documented.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:office-standard-doc", gap, "profiles/office-standard",
                "v5.4 docs identify profiles/office-standard as the canonical pack.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:install-dry-run-doc", gap, "standards install",
                "v5.4 docs include standards install dry-run/apply commands.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:validate-doc", gap, "standards validate",
                "v5.4 docs include standards validate evidence.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:sheet-map-doc", gap, "sheet map",
                "v5.4 docs disclose sheet map runtime files.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:numbering-rules-doc", gap, "numbering rules",
                "v5.4 docs disclose numbering rule runtime files.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:benchmark-gap-doc", gap, "not benchmarked",
                "v5.4 docs keep bootstrap timing unclaimed until benchmarked.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:pilot-gap-doc", gap, "not live verified",
                "v5.4 docs keep BIM manager pilot evidence unclaimed.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:no-saas-doc", gap, "SaaS",
                "v5.4 docs keep SaaS out of the runtime-pack gate.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:no-mcp-doc", gap, "MCP",
                "v5.4 docs keep MCP out of the runtime-pack gate.", "docs/smoke/v5.4/gap-report.md");
            AddContains(report, "v5.4:no-llm-doc", gap, "LLM",
                "v5.4 docs keep built-in LLM behavior out of the runtime-pack gate.", "docs/smoke/v5.4/gap-report.md");
        }

        var packRoot = Path.Combine(root, ToNativePath("profiles/office-standard"));
        var manifestPath = Path.Combine(packRoot, ToNativePath(".revitcli/standards.yml"));
        if (!File.Exists(manifestPath))
        {
            report.Add("v5.4:office-standard-pack", ReleaseVerifyStatus.Error,
                "Missing profiles/office-standard/.revitcli/standards.yml.",
                "profiles/office-standard/.revitcli/standards.yml");
            return;
        }

        var manifestText = ReadText(manifestPath, report, "v5.4:office-standard-readable");
        if (manifestText is not null)
        {
            AddContains(report, "v5.4:sheet-map-manifest", manifestText, "sheetMaps",
                "office-standard manifest declares required sheet map files.", "profiles/office-standard/.revitcli/standards.yml");
            AddContains(report, "v5.4:numbering-rules-manifest", manifestText, "numberingRules",
                "office-standard manifest declares required numbering rule files.", "profiles/office-standard/.revitcli/standards.yml");
        }

        var validation = StandardsValidator.Validate(manifestPath, packRoot);
        if (validation.Valid)
        {
            report.Add("v5.4:office-standard-validate", ReleaseVerifyStatus.Ok,
                "profiles/office-standard validates offline with profile, workflow, output path, schedule template, sheet map, numbering rule, and family-rule requirements.",
                "profiles/office-standard/.revitcli/standards.yml");
        }
        else
        {
            var preview = string.Join("; ", validation.Issues
                .Where(issue => issue.Severity == StandardsValidationSeverity.Error)
                .Take(3)
                .Select(issue => $"{issue.Path}: {issue.Message}"));
            report.Add("v5.4:office-standard-validate", ReleaseVerifyStatus.Error,
                $"profiles/office-standard failed offline standards validation: {preview}",
                "profiles/office-standard/.revitcli/standards.yml");
        }

        var installSmoke = StandardsRuntimePackSmoke.Run(packRoot);
        report.Add(
            "v5.4:office-standard-install",
            installSmoke.Success ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            installSmoke.Success
                ? $"profiles/office-standard install smoke passed: {installSmoke.Evidence}."
                : $"profiles/office-standard install smoke failed: {installSmoke.Evidence}",
            "profiles/office-standard");

        var readmePath = Path.Combine(root, ToNativePath("README.md"));
        if (File.Exists(readmePath))
        {
            var readme = ReadText(readmePath, report, "v5.4:readme-readable");
            if (readme is not null)
            {
                AddContains(report, "v5.4:readme-office-standard", readme, "profiles/office-standard",
                    "README points users to the canonical office-standard pack.", "README.md");
            }
        }

        var recipePath = Path.Combine(root, ToNativePath("docs/templates/codex-recipes/standards-bootstrap.md"));
        if (File.Exists(recipePath))
        {
            var recipe = ReadText(recipePath, report, "v5.4:recipe-readable");
            if (recipe is not null)
            {
                AddContains(report, "v5.4:recipe-office-standard", recipe, "profiles/office-standard",
                    "Standards bootstrap recipe uses the canonical office-standard pack.", "docs/templates/codex-recipes/standards-bootstrap.md");
            }
        }
    }

    private static void CheckV55ViewCoordinationHygiene(
        string root,
        ReleaseVerifyReport report,
        Lazy<WorkbenchVerifyRun> workbenchVerify)
    {
        var gapPath = Path.Combine(root, ToNativePath("docs/smoke/v5.5/gap-report.md"));
        if (!File.Exists(gapPath))
        {
            report.Add("v5.5:view-coordination-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v5.5/gap-report.md; v5.5 view/coordination hygiene gates are not disclosed.",
                "docs/smoke/v5.5/gap-report.md");
            return;
        }

        var gap = ReadText(gapPath, report, "v5.5:view-coordination-doc-readable");
        if (gap is null)
            return;

        AddContains(report, "v5.5:view-coordination-doc", gap, "v5.5 View and Coordination Hygiene",
            "v5.5 view/coordination hygiene gap report is documented.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:audit-first-doc", gap, "audit-first",
            "v5.5 docs keep the lane audit-first.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:views-doc", gap, "views template-apply",
            "v5.5 docs cover view template dry-run planning.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:placed-view-doc", gap, "placed-view rollback guard",
            "v5.5 docs require placed-view rollback guards.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:links-doc", gap, "links repair",
            "v5.5 docs cover link repair planning.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:no-coordinate-doc", gap, "no coordinate moves",
            "v5.5 docs keep coordinate repair out of the production claim.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:model-map-doc", gap, "model map-fix",
            "v5.5 docs cover model map dry-run planning.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:worksharing-gap-doc", gap, "worksharing locks",
            "v5.5 docs keep worksharing lock behavior unclaimed until live verification.", "docs/smoke/v5.5/gap-report.md");
        AddContains(report, "v5.5:journal-gap-doc", gap, "journal verify",
            "v5.5 docs require journal verification for approved coordination apply smoke.", "docs/smoke/v5.5/gap-report.md");
        AddGuardedContains(report, "v5.5:no-saas-doc", gap, "SaaS",
            "v5.5 docs keep SaaS out of the hygiene gate.", "docs/smoke/v5.5/gap-report.md", V60SaasContradictions);
        AddGuardedContains(report, "v5.5:no-mcp-doc", gap, "MCP",
            "v5.5 docs keep MCP out of the hygiene gate.", "docs/smoke/v5.5/gap-report.md", V60McpContradictions);
        AddGuardedContains(report, "v5.5:no-llm-doc", gap, "built-in LLM",
            "v5.5 docs keep built-in LLM behavior out of the hygiene gate.", "docs/smoke/v5.5/gap-report.md", V60LlmContradictions);
        AddGuardedContains(report, "v5.5:no-dashboard-central-doc", gap, "dashboard-central",
            "v5.5 docs keep dashboard-central out of the hygiene gate.", "docs/smoke/v5.5/gap-report.md", V60DashboardCentralContradictions);

        var linkJsonEvidence = LinksCommand.VerifyLinkRepairPlanJsonIsPathLoadOnly();
        report.Add(
            "v5.5:link-repair-plan-json",
            linkJsonEvidence.Success ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            linkJsonEvidence.Success
                ? $"links repair emits a path/load-only plan JSON shape: {linkJsonEvidence.Evidence}"
                : $"links repair no-coordinate plan JSON evidence failed: {linkJsonEvidence.Evidence}",
            "src/RevitCli/Commands/LinksCommand.cs");

        AddV55WorkbenchGate(root, report, workbenchVerify);
    }

    private static void AddV55WorkbenchGate(
        string root,
        ReleaseVerifyReport report,
        Lazy<WorkbenchVerifyRun> workbenchVerify)
    {
        var run = workbenchVerify.Value;
        if (run.Error is not null)
        {
            report.Add(
                "v5.5:workbench-gate",
                ReleaseVerifyStatus.Error,
                $"Could not evaluate workbench v2 v5.5 gate for this release root: {run.Error.Message}",
                run.Source);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(run.Output);
            string? status = null;
            string? evidence = null;
            foreach (var check in document.RootElement.GetProperty("checks").EnumerateArray())
            {
                if (!string.Equals(check.GetProperty("id").GetString(), "v55ViewCoordinationHygieneGate", StringComparison.Ordinal))
                    continue;

                status = check.GetProperty("status").GetString();
                evidence = check.GetProperty("evidence").GetString();
                break;
            }

            var success = string.Equals(status, "pass", StringComparison.OrdinalIgnoreCase);
            report.Add(
                "v5.5:workbench-gate",
                success ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
                success
                    ? $"scoped workbench v2 v5.5 gate passes for release root {root}: {evidence} (overall workbench exit {run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} ignored by design; scoped v5.5 gate status={status})."
                    : $"scoped workbench v2 v5.5 gate did not pass for release root {root}: status={status ?? "missing"} evidence={evidence ?? "n/a"} exit={run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}.",
                run.Source);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            report.Add(
                "v5.5:workbench-gate",
                ReleaseVerifyStatus.Error,
                $"Could not evaluate workbench v2 v5.5 gate for this release root: {ex.Message}",
                run.Source);
        }
    }

    private static void CheckV56TeamPilotPack(
        string root,
        ReleaseVerifyReport report,
        Lazy<WorkbenchVerifyRun> workbenchVerify)
    {
        var gapPath = Path.Combine(root, ToNativePath("docs/smoke/v5.6/gap-report.md"));
        if (!File.Exists(gapPath))
        {
            report.Add("v5.6:team-pilot-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v5.6/gap-report.md; v5.6 team pilot pack gates are not disclosed.",
                "docs/smoke/v5.6/gap-report.md");
            return;
        }

        var gap = ReadText(gapPath, report, "v5.6:team-pilot-doc-readable");
        if (gap is null)
            return;

        AddContains(report, "v5.6:team-pilot-doc", gap, "v5.6 Team Pilot Pack",
            "v5.6 team pilot pack gap report is documented.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:installer-doc", gap, "installer",
            "v5.6 docs cover installer and bootstrap checks.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:doctor-doc", gap, "doctor",
            "v5.6 docs require doctor evidence before pilots.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:policy-doc", gap, "policy files",
            "v5.6 docs cover local policy files.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:receipt-retention-doc", gap, "receipt retention",
            "v5.6 docs cover receipt retention.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:training-doc", gap, "training",
            "v5.6 docs cover training handoff.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:support-doc", gap, "supportable error reports",
            "v5.6 docs cover supportable error reports.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:pilot-gap-doc", gap, "office pilots",
            "v5.6 docs keep office pilot evidence explicit.", "docs/smoke/v5.6/gap-report.md");
        AddContains(report, "v5.6:not-live-doc", gap, "not live verified",
            "v5.6 docs do not overclaim live team pilot evidence.", "docs/smoke/v5.6/gap-report.md");
        AddGuardedContains(report, "v5.6:no-saas-doc", gap, "SaaS",
            "v5.6 docs keep SaaS out of the team pilot pack.", "docs/smoke/v5.6/gap-report.md", V60SaasContradictions);
        AddGuardedContains(report, "v5.6:no-mcp-doc", gap, "MCP",
            "v5.6 docs keep MCP out of the team pilot pack.", "docs/smoke/v5.6/gap-report.md", V60McpContradictions);
        AddGuardedContains(report, "v5.6:no-llm-doc", gap, "built-in LLM",
            "v5.6 docs keep built-in LLM behavior out of the team pilot pack.", "docs/smoke/v5.6/gap-report.md", V60LlmContradictions);
        AddGuardedContains(report, "v5.6:no-dashboard-central-doc", gap, "dashboard-central",
            "v5.6 docs keep dashboard-central out of the team pilot pack.", "docs/smoke/v5.6/gap-report.md", V60DashboardCentralContradictions);

        var policyPath = Path.Combine(root, ToNativePath("profiles/team-pilot/.revitcli/team-policy.yml"));
        var policy = TeamPolicyValidator.Validate(policyPath, root);
        if (policy.Valid)
        {
            report.Add("v5.6:team-policy", ReleaseVerifyStatus.Ok,
                "profiles/team-pilot/.revitcli/team-policy.yml validates local-first boundaries, install years, receipt retention, required commands, and support evidence paths.",
                "profiles/team-pilot/.revitcli/team-policy.yml");
        }
        else
        {
            var preview = string.Join("; ", policy.Issues.Take(3).Select(issue => $"{issue.Code}: {issue.Message}"));
            report.Add("v5.6:team-policy", ReleaseVerifyStatus.Error,
                $"profiles/team-pilot/.revitcli/team-policy.yml failed validation: {preview}",
                "profiles/team-pilot/.revitcli/team-policy.yml");
        }

        AddV56WorkbenchGate(root, report, workbenchVerify);
    }

    private static void AddV56WorkbenchGate(
        string root,
        ReleaseVerifyReport report,
        Lazy<WorkbenchVerifyRun> workbenchVerify)
    {
        var run = workbenchVerify.Value;
        if (run.Error is not null)
        {
            report.Add(
                "v5.6:workbench-gate",
                ReleaseVerifyStatus.Error,
                $"Could not evaluate workbench v2 v5.6 gate for this release root: {run.Error.Message}",
                run.Source);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(run.Output);
            string? status = null;
            string? evidence = null;
            foreach (var check in document.RootElement.GetProperty("checks").EnumerateArray())
            {
                if (!string.Equals(check.GetProperty("id").GetString(), "v56TeamPilotPackGate", StringComparison.Ordinal))
                    continue;

                status = check.GetProperty("status").GetString();
                evidence = check.GetProperty("evidence").GetString();
                break;
            }

            var success = string.Equals(status, "pass", StringComparison.OrdinalIgnoreCase);
            report.Add(
                "v5.6:workbench-gate",
                success ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
                success
                    ? $"scoped workbench v2 v5.6 gate passes for release root {root}: {evidence} (overall workbench exit {run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} ignored by design; scoped v5.6 gate status={status})."
                    : $"scoped workbench v2 v5.6 gate did not pass for release root {root}: status={status ?? "missing"} evidence={evidence ?? "n/a"} exit={run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}.",
                run.Source);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            report.Add(
                "v5.6:workbench-gate",
                ReleaseVerifyStatus.Error,
                $"Could not evaluate workbench v2 v5.6 gate for this release root: {ex.Message}",
                run.Source);
        }
    }

    private static void CheckV60LocalBimOpsContract(
        string root,
        ReleaseVerifyReport report,
        Lazy<WorkbenchVerifyRun> workbenchVerify)
    {
        var contractPath = Path.Combine(root, ToNativePath("docs/v6-local-bimops-contract.md"));
        if (!File.Exists(contractPath))
        {
            report.Add("v6.0:contract-doc", ReleaseVerifyStatus.Error,
                "Missing docs/v6-local-bimops-contract.md; v6.0 Local BIMOps contract gates are not disclosed.",
                "docs/v6-local-bimops-contract.md");
            return;
        }

        var contract = ReadText(contractPath, report, "v6.0:contract-doc-readable");
        if (contract is null)
            return;

        AddContains(report, "v6.0:contract-doc", contract, "v6.0 Local BIMOps Workbench Contract",
            "v6.0 Local BIMOps contract is documented.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-doc", contract, "Revit Model Operations Ledger",
            "v6.0 contract documents the local operations ledger kernel.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:dry-run-doc", contract, "dry-run first",
            "v6.0 contract keeps dry-run first behavior.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:deterministic-receipt-doc", contract, "deterministic receipt",
            "v6.0 contract documents deterministic receipt rules.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:receipt-hash-doc", contract, "receiptHash",
            "v6.0 contract documents receipt hash fields.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:journal-path-doc", contract, "journalPath",
            "v6.0 contract documents journal path fields.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:rollback-pointer-doc", contract, "rollbackPointer",
            "v6.0 contract documents rollback pointer fields.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:checks-doc", contract, "checks",
            "v6.0 contract documents checks fields.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:artifacts-doc", contract, "artifacts",
            "v6.0 contract documents artifacts fields.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:rollback-doc", contract, "rollback preconditions",
            "v6.0 contract documents rollback preconditions.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:audit-doc", contract, "audit trail",
            "v6.0 contract documents local audit trail invariants.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:project-memory-doc", contract, "project memory",
            "v6.0 contract bounds project memory to local history.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:workflow-registry-doc", contract, "workflow registry",
            "v6.0 contract documents governed local workflow registry requirements.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:workflow-registry-command-doc", contract, "workflow registry --output json",
            "v6.0 contract exposes the read-only workflow registry command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:workflow-registry-schema-doc", contract, "workflow-registry.v1",
            "v6.0 contract documents the workflow registry output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-append-doc", contract, "ledger append",
            "v6.0 contract exposes the append-only ledger runtime command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-append-schema-doc", contract, "ledger-append.v1",
            "v6.0 contract documents the ledger append output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-query-doc", contract, "ledger query",
            "v6.0 contract exposes the read-only ledger query command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-query-schema-doc", contract, "ledger-query.v1",
            "v6.0 contract documents the ledger query output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-replay-doc", contract, "ledger replay",
            "v6.0 contract exposes the preview-only ledger replay command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-replay-schema-doc", contract, "ledger-replay.v1",
            "v6.0 contract documents the ledger replay output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-validate-doc", contract, "ledger validate",
            "v6.0 contract exposes the read-only ledger validation command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-validate-schema-doc", contract, "ledger-validate.v1",
            "v6.0 contract documents the ledger validation output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-stats-doc", contract, "ledger stats",
            "v6.0 contract exposes the read-only ledger stats command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-stats-schema-doc", contract, "ledger-stats.v1",
            "v6.0 contract documents the ledger stats output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-timeline-doc", contract, "ledger timeline",
            "v6.0 contract exposes the read-only ledger timeline command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-timeline-schema-doc", contract, "ledger-timeline.v1",
            "v6.0 contract documents the ledger timeline output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-analytics-doc", contract, "ledger analytics",
            "v6.0 contract exposes the local ledger analytics bundle command.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:ledger-analytics-schema-doc", contract, "ledger-analytics-bundle.v1",
            "v6.0 contract documents the ledger analytics bundle output schema.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:release-pilot-validate-doc", contract, "release pilot validate",
            "v6.0 contract exposes local office pilot evidence packet validation.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:release-pilot-register-doc", contract, "release pilot register",
            "v6.0 contract exposes local office pilot rollout status registration.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:release-pilot-status-doc", contract, "release pilot status",
            "v6.0 contract exposes local office pilot rollout status reporting.", "docs/v6-local-bimops-contract.md");
        AddContains(report, "v6.0:release-pilot-claim-doc", contract, "release pilot claim",
            "v6.0 contract exposes an explicit local office rollout completion claim command.", "docs/v6-local-bimops-contract.md");
        AddGuardedContains(report, "v6.0:no-saas-doc", contract, "SaaS",
            "v6.0 contract keeps SaaS out of the baseline.", "docs/v6-local-bimops-contract.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:no-mcp-doc", contract, "MCP",
            "v6.0 contract keeps MCP out of the baseline.", "docs/v6-local-bimops-contract.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:no-llm-doc", contract, "built-in LLM",
            "v6.0 contract keeps built-in LLM behavior out of the baseline.", "docs/v6-local-bimops-contract.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:no-dashboard-central-doc", contract, "dashboard-central",
            "v6.0 contract keeps dashboard-central workflow state out of the baseline.", "docs/v6-local-bimops-contract.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:no-database-doc", contract, "database",
            "v6.0 contract keeps database-backed ledger runtime out of the baseline.", "docs/v6-local-bimops-contract.md", V60DatabaseContradictions);

        var gapPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/gap-report.md"));
        if (!File.Exists(gapPath))
        {
            report.Add("v6.0:gap-report", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/gap-report.md; v6.0 Local BIMOps gaps are not disclosed.",
                "docs/smoke/v6.0/gap-report.md");
            return;
        }

        var gap = ReadText(gapPath, report, "v6.0:gap-report-readable");
        if (gap is null)
            return;

        AddContains(report, "v6.0:gap-report", gap, "contract baseline",
            "v6.0 gap report documents the contract baseline.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-gap-doc", gap, "live ledger apply",
            "v6.0 gap report keeps live ledger apply out of the staged runtime slice.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:pilot-gap-doc", gap, "real Revit pilots",
            "v6.0 gap report keeps real Revit pilots explicit.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:office-pilot-gap-doc", gap, "office rollout pilots",
            "v6.0 gap report keeps office rollout pilots explicit.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:local-controlled-pilot-gap-doc", gap, "local controlled pilot packet",
            "v6.0 gap report records the local controlled pilot packet without claiming rollout completion.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:not-live-doc", gap, "not live verified",
            "v6.0 gap report does not overclaim live evidence.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-query-gap-doc", gap, "read-only ledger query",
            "v6.0 gap report scopes ledger query as read-only local artifact aggregation.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-append-gap-doc", gap, "append-only ledger runtime",
            "v6.0 gap report scopes ledger append as a staged local runtime.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-replay-gap-doc", gap, "ledger replay preview",
            "v6.0 gap report scopes ledger replay as preview-only.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-validate-gap-doc", gap, "read-only ledger validate",
            "v6.0 gap report scopes ledger validation as read-only local artifact checking.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-stats-gap-doc", gap, "read-only ledger stats",
            "v6.0 gap report scopes ledger stats as read-only local artifact summarization.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:ledger-timeline-gap-doc", gap, "read-only ledger timeline",
            "v6.0 gap report scopes ledger timeline as read-only project-memory bucketing.", "docs/smoke/v6.0/gap-report.md");
        AddContains(report, "v6.0:workflow-registry-gap-doc", gap, "read-only workflow registry",
            "v6.0 gap report scopes workflow registry as read-only local workflow indexing.", "docs/smoke/v6.0/gap-report.md");
        AddSemanticContains(report, "v6.0:command-spine-output-parity-gap-doc", gap,
            new[] { "table", "summary", "Markdown", "detail", "parity" },
            "v6.0 gap report documents command-spine non-JSON output parity.", "docs/smoke/v6.0/gap-report.md");
        AddSemanticContains(report, "v6.0:audit-spine-journal-parity-gap-doc", gap,
            new[] { "journal verify", "JSON/table", "validity", "root-hash", "parity" },
            "v6.0 gap report documents journal verify JSON/table validity/root-hash parity.", "docs/smoke/v6.0/gap-report.md");
        AddSemanticContains(report, "v6.0:audit-spine-history-row-order-gap-doc", gap,
            new[] { "history-list.v1", "JSON count consistency", "table", "row-order parity" },
            "v6.0 gap report documents history-list.v1 JSON count consistency and table row-order parity.", "docs/smoke/v6.0/gap-report.md");
        AddGuardedContains(report, "v6.0:no-dashboard-central-gap-doc", gap, "dashboard-central",
            "v6.0 gap report keeps dashboard-central out of the baseline.", "docs/smoke/v6.0/gap-report.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:no-saas-gap-doc", gap, "SaaS",
            "v6.0 gap report keeps SaaS out of the baseline.", "docs/smoke/v6.0/gap-report.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:no-mcp-gap-doc", gap, "MCP",
            "v6.0 gap report keeps MCP out of the baseline.", "docs/smoke/v6.0/gap-report.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:no-llm-gap-doc", gap, "built-in LLM",
            "v6.0 gap report keeps built-in LLM behavior out of the baseline.", "docs/smoke/v6.0/gap-report.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:no-database-gap-doc", gap, "database",
            "v6.0 gap report avoids a centralized ledger database claim.", "docs/smoke/v6.0/gap-report.md", V60DatabaseContradictions);

        var pilotEvidencePath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/pilot-evidence-template.md"));
        if (!File.Exists(pilotEvidencePath))
        {
            report.Add("v6.0:pilot-evidence-template", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/pilot-evidence-template.md; v6.0 office rollout pilot evidence intake is not documented.",
                "docs/smoke/v6.0/pilot-evidence-template.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var pilotEvidence = ReadText(pilotEvidencePath, report, "v6.0:pilot-evidence-template-readable");
        if (pilotEvidence is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:pilot-evidence-template", pilotEvidence, "v6.0 Office Rollout Pilot Evidence Packet",
            "v6.0 office rollout pilot evidence packet is documented.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-project-copy", pilotEvidence, "controlled project-copy pilots",
            "v6.0 pilot evidence is scoped to controlled project copies.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-pilot-identifier", pilotEvidence, "Pilot identifier",
            "v6.0 pilot evidence binds each packet to a registered pilot id.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-scaffold-command", pilotEvidence, "release pilot scaffold",
            "v6.0 pilot evidence intake exposes the local scaffold command.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-validate-command", pilotEvidence, "release pilot validate",
            "v6.0 pilot evidence intake exposes the local packet validation command.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-register-command", pilotEvidence, "release pilot register",
            "v6.0 pilot evidence intake exposes the local status registration command.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-register-next-actions", pilotEvidence, "register nextActions",
            "v6.0 pilot register reports machine-readable next actions after validation or writes.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-rollout-status-command", pilotEvidence, "release pilot status",
            "v6.0 pilot evidence intake exposes the local rollout status reporting command.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-rollout-status-missing-evidence", pilotEvidence, "missingEvidence",
            "v6.0 pilot status reports per-pilot missing evidence flags.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-rollout-status-missing-evidence-summary", pilotEvidence, "missingEvidenceSummary",
            "v6.0 pilot status reports aggregate missing evidence summary by evidence field.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-rollout-status-evidence-complete-count", pilotEvidence, "evidenceCompleteOfficePilotCount",
            "v6.0 pilot status distinguishes registered pilot count from evidence-complete pilot count.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-rollout-status-evidence-complete-remaining", pilotEvidence, "remainingEvidenceCompleteOfficePilotCount",
            "v6.0 pilot status reports remaining evidence-complete pilots needed before rollout completion.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-claim-command", pilotEvidence, "release pilot claim",
            "v6.0 pilot evidence intake exposes an explicit completion claim command.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-claim-blockers", pilotEvidence, "claimBlockers",
            "v6.0 pilot claim dry-run reports machine-readable rollout claim blockers.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-next-actions", pilotEvidence, "nextActions",
            "v6.0 pilot status and claim dry-runs report machine-readable next actions.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-doctor", pilotEvidence, "doctor --check-version 2026 --output json",
            "v6.0 pilot evidence requires doctor version proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-status", pilotEvidence, "`status --output json`",
            "v6.0 pilot evidence requires live status proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-workbench", pilotEvidence, "workbench verify --contract workbench-contract.v2",
            "v6.0 pilot evidence requires workbench gate proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-release", pilotEvidence, "release verify --strict --output json",
            "v6.0 pilot evidence requires release gate proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-ledger-query", pilotEvidence, "ledger query --source ledger --output json",
            "v6.0 pilot evidence requires ledger query proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-ledger", pilotEvidence, "ledger validate --source ledger --output json",
            "v6.0 pilot evidence requires ledger validation proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-ledger-stats", pilotEvidence, "ledger stats --source ledger --analytics-snapshot",
            "v6.0 pilot evidence requires ledger stats analytics snapshot proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-ledger-timeline", pilotEvidence, "ledger timeline --source ledger --analytics-snapshot",
            "v6.0 pilot evidence requires ledger timeline analytics snapshot proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-journal", pilotEvidence, "journal verify --output json",
            "v6.0 pilot evidence requires journal verification proof.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-rollback", pilotEvidence, "Rollback result",
            "v6.0 pilot evidence records rollback results.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-support-claim", pilotEvidence, "no production support claim",
            "v6.0 pilot evidence template avoids production support overclaiming.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-minimum-count", pilotEvidence, "Minimum office pilots: 2-3 completed office pilots",
            "v6.0 pilot evidence template keeps the office rollout pilot count explicit.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-bim-manager-signoff", pilotEvidence, "BIM manager signoff",
            "v6.0 pilot evidence template requires BIM manager signoff.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-owner-signoff", pilotEvidence, "Project-copy owner signoff",
            "v6.0 pilot evidence template requires project-copy owner signoff.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-support-review", pilotEvidence, "Support ticket review",
            "v6.0 pilot evidence template requires support ticket review.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddContains(report, "v6.0:pilot-evidence-postmortem", pilotEvidence, "Multi-user rollout postmortem",
            "v6.0 pilot evidence template requires multi-user rollout postmortem.", "docs/smoke/v6.0/pilot-evidence-template.md");
        AddGuardedContains(report, "v6.0:pilot-evidence-no-saas", pilotEvidence, "SaaS",
            "v6.0 pilot evidence template excludes SaaS.", "docs/smoke/v6.0/pilot-evidence-template.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:pilot-evidence-no-mcp", pilotEvidence, "MCP",
            "v6.0 pilot evidence template excludes MCP.", "docs/smoke/v6.0/pilot-evidence-template.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:pilot-evidence-no-llm", pilotEvidence, "built-in LLM",
            "v6.0 pilot evidence template excludes built-in LLM behavior.", "docs/smoke/v6.0/pilot-evidence-template.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:pilot-evidence-no-dashboard-central", pilotEvidence, "dashboard-central",
            "v6.0 pilot evidence template excludes dashboard-central.", "docs/smoke/v6.0/pilot-evidence-template.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:pilot-evidence-no-database", pilotEvidence, "database",
            "v6.0 pilot evidence template excludes database runtime.", "docs/smoke/v6.0/pilot-evidence-template.md", V60DatabaseContradictions);
        AddV60OfficeRolloutStatus(root, report);

        var localPilotPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/local-controlled-pilot-20260525.md"));
        if (!File.Exists(localPilotPath))
        {
            report.Add("v6.0:local-controlled-pilot-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/local-controlled-pilot-20260525.md; v6.0 local controlled pilot evidence is not recorded.",
                "docs/smoke/v6.0/local-controlled-pilot-20260525.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var localPilot = ReadText(localPilotPath, report, "v6.0:local-controlled-pilot-doc-readable");
        if (localPilot is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:local-controlled-pilot-doc", localPilot, "Local Controlled Pilot Evidence",
            "v6.0 local controlled pilot evidence packet is documented.", "docs/smoke/v6.0/local-controlled-pilot-20260525.md");
        AddContains(report, "v6.0:local-controlled-pilot-bundle-doc", localPilot, "revit2026-v6-local-controlled-pilot-20260525",
            "v6.0 local controlled pilot doc names its evidence bundle.", "docs/smoke/v6.0/local-controlled-pilot-20260525.md");
        AddContains(report, "v6.0:local-controlled-pilot-ledger-doc", localPilot, "ledger-validate.v1",
            "v6.0 local controlled pilot doc records ledger validation evidence.", "docs/smoke/v6.0/local-controlled-pilot-20260525.md");
        AddContains(report, "v6.0:local-controlled-pilot-journal-doc", localPilot, "isValid=true",
            "v6.0 local controlled pilot doc records journal verification evidence.", "docs/smoke/v6.0/local-controlled-pilot-20260525.md");
        AddContains(report, "v6.0:local-controlled-pilot-boundary-doc", localPilot, "not office rollout completion",
            "v6.0 local controlled pilot doc avoids office rollout overclaiming.", "docs/smoke/v6.0/local-controlled-pilot-20260525.md");
        AddV60LocalControlledPilotEvidenceSummary(root, report);

        var standardsRuntimePath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/standards-runtime.md"));
        if (!File.Exists(standardsRuntimePath))
        {
            report.Add("v6.0:standards-spine-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/standards-runtime.md; v6.0 standards runtime behavior is not documented.",
                "docs/smoke/v6.0/standards-runtime.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var standardsRuntime = ReadText(standardsRuntimePath, report, "v6.0:standards-spine-smoke-doc-readable");
        if (standardsRuntime is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:standards-spine-smoke-doc", standardsRuntime, "standards validate --output json",
            "v6.0 standards runtime smoke doc exposes the standards validate command.", "docs/smoke/v6.0/standards-runtime.md");
        AddContains(report, "v6.0:standards-runtime-smoke-doc", standardsRuntime, "standards runtime",
            "v6.0 standards runtime smoke doc describes standards runtime checks.", "docs/smoke/v6.0/standards-runtime.md");
        AddSemanticContains(report, "v6.0:standards-output-parity-smoke-doc", standardsRuntime,
            new[] { "table", "summary", "Markdown", "detail", "parity" },
            "v6.0 standards runtime smoke doc documents non-JSON output parity.", "docs/smoke/v6.0/standards-runtime.md");
        AddContains(report, "v6.0:standards-final-snapshot-smoke-doc", standardsRuntime, "final file-tree snapshot evidence",
            "v6.0 standards runtime smoke doc documents dry-run final snapshot evidence.", "docs/smoke/v6.0/standards-runtime.md");
        AddGuardedContains(report, "v6.0:standards-no-write-smoke-doc", standardsRuntime, "read-only",
            "v6.0 standards runtime smoke doc scopes standards validate as read-only.", "docs/smoke/v6.0/standards-runtime.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:standards-no-revit-smoke-doc", standardsRuntime, "does not start Revit",
            "v6.0 standards runtime smoke doc avoids live Revit claims.", "docs/smoke/v6.0/standards-runtime.md", V60RevitRuntimeContradictions);
        AddGuardedContains(report, "v6.0:standards-no-model-write-smoke-doc", standardsRuntime, "does not write model data",
            "v6.0 standards runtime smoke doc avoids Revit model writes.", "docs/smoke/v6.0/standards-runtime.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:standards-no-saas-smoke-doc", standardsRuntime, "SaaS",
            "v6.0 standards runtime smoke doc keeps SaaS out.", "docs/smoke/v6.0/standards-runtime.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:standards-no-mcp-smoke-doc", standardsRuntime, "MCP",
            "v6.0 standards runtime smoke doc keeps MCP out.", "docs/smoke/v6.0/standards-runtime.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:standards-no-llm-smoke-doc", standardsRuntime, "built-in LLM",
            "v6.0 standards runtime smoke doc keeps built-in LLM behavior out.", "docs/smoke/v6.0/standards-runtime.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:standards-no-dashboard-central-smoke-doc", standardsRuntime, "dashboard-central",
            "v6.0 standards runtime smoke doc keeps dashboard-central state out.", "docs/smoke/v6.0/standards-runtime.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:standards-no-db-smoke-doc", standardsRuntime, "database",
            "v6.0 standards runtime smoke doc avoids a centralized standards database claim.", "docs/smoke/v6.0/standards-runtime.md", V60DatabaseContradictions);

        var issueSpinePath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/issue-spine.md"));
        if (!File.Exists(issueSpinePath))
        {
            report.Add("v6.0:issue-spine-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/issue-spine.md; v6.0 issue command spine behavior is not documented.",
                "docs/smoke/v6.0/issue-spine.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var issueSpine = ReadText(issueSpinePath, report, "v6.0:issue-spine-smoke-doc-readable");
        if (issueSpine is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:issue-spine-smoke-doc", issueSpine, "issue preflight --profile",
            "v6.0 issue spine smoke doc exposes issue preflight.", "docs/smoke/v6.0/issue-spine.md");
        AddContains(report, "v6.0:issue-package-dry-run-smoke-doc", issueSpine, "issue package --profile",
            "v6.0 issue spine smoke doc exposes issue package.", "docs/smoke/v6.0/issue-spine.md");
        AddContains(report, "v6.0:issue-dry-run-first-smoke-doc", issueSpine, "dry-run first",
            "v6.0 issue spine smoke doc keeps issue packaging dry-run first.", "docs/smoke/v6.0/issue-spine.md");
        AddContains(report, "v6.0:issue-hidden-mutation-smoke-doc", issueSpine, "hidden mutation guards",
            "v6.0 issue spine smoke doc documents hidden mutation guards.", "docs/smoke/v6.0/issue-spine.md");
        AddContains(report, "v6.0:issue-package-receipt-smoke-doc", issueSpine, "issue-package-receipt.v1",
            "v6.0 issue spine smoke doc documents issue package receipt schema.", "docs/smoke/v6.0/issue-spine.md");
        AddSemanticContains(report, "v6.0:issue-output-parity-smoke-doc", issueSpine,
            new[] { "table", "summary", "Markdown", "detail", "parity" },
            "v6.0 issue spine smoke doc documents non-JSON output parity.", "docs/smoke/v6.0/issue-spine.md");
        AddGuardedContains(report, "v6.0:issue-no-write-smoke-doc", issueSpine, "dry-run no-write evidence",
            "v6.0 issue spine smoke doc documents dry-run no-write evidence.", "docs/smoke/v6.0/issue-spine.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:issue-no-revit-smoke-doc", issueSpine, "does not start Revit",
            "v6.0 issue spine smoke doc avoids live Revit claims.", "docs/smoke/v6.0/issue-spine.md", V60RevitRuntimeContradictions);
        AddGuardedContains(report, "v6.0:issue-no-saas-smoke-doc", issueSpine, "SaaS",
            "v6.0 issue spine smoke doc keeps SaaS out.", "docs/smoke/v6.0/issue-spine.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:issue-no-mcp-smoke-doc", issueSpine, "MCP",
            "v6.0 issue spine smoke doc keeps MCP out.", "docs/smoke/v6.0/issue-spine.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:issue-no-llm-smoke-doc", issueSpine, "built-in LLM",
            "v6.0 issue spine smoke doc keeps built-in LLM behavior out.", "docs/smoke/v6.0/issue-spine.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:issue-no-dashboard-central-smoke-doc", issueSpine, "dashboard-central",
            "v6.0 issue spine smoke doc keeps dashboard-central state out.", "docs/smoke/v6.0/issue-spine.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:issue-no-db-smoke-doc", issueSpine, "database",
            "v6.0 issue spine smoke doc avoids a centralized issue database claim.", "docs/smoke/v6.0/issue-spine.md", V60DatabaseContradictions);

        var deliverablesVerifyPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/deliverables-verify.md"));
        if (!File.Exists(deliverablesVerifyPath))
        {
            report.Add("v6.0:deliverables-spine-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/deliverables-verify.md; v6.0 deliverables verification behavior is not documented.",
                "docs/smoke/v6.0/deliverables-verify.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var deliverablesVerify = ReadText(deliverablesVerifyPath, report, "v6.0:deliverables-spine-smoke-doc-readable");
        if (deliverablesVerify is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:deliverables-spine-smoke-doc", deliverablesVerify, "deliverables verify --output json",
            "v6.0 deliverables verification smoke doc exposes deliverables verify.", "docs/smoke/v6.0/deliverables-verify.md");
        AddContains(report, "v6.0:deliverables-manifest-smoke-doc", deliverablesVerify, "local manifest-read",
            "v6.0 deliverables verification smoke doc documents local manifest reads.", "docs/smoke/v6.0/deliverables-verify.md");
        AddContains(report, "v6.0:deliverables-receipt-smoke-doc", deliverablesVerify, "readable-receipt evidence",
            "v6.0 deliverables verification smoke doc documents readable receipt evidence.", "docs/smoke/v6.0/deliverables-verify.md");
        AddSemanticContains(report, "v6.0:deliverables-output-parity-smoke-doc", deliverablesVerify,
            new[] { "Kinds", "Outcomes", "counts", "table", "Markdown" },
            "v6.0 deliverables verification smoke doc documents table/Markdown stats parity.", "docs/smoke/v6.0/deliverables-verify.md");
        AddGuardedContains(report, "v6.0:deliverables-no-package-write-smoke-doc", deliverablesVerify, "without package writes",
            "v6.0 deliverables verification smoke doc avoids package write claims.", "docs/smoke/v6.0/deliverables-verify.md", V60LocalWriteContradictions);
        AddContains(report, "v6.0:deliverables-missing-receipts-smoke-doc", deliverablesVerify, "missing receipts",
            "v6.0 deliverables verification smoke doc covers missing receipt evidence.", "docs/smoke/v6.0/deliverables-verify.md");
        AddGuardedContains(report, "v6.0:deliverables-no-revit-smoke-doc", deliverablesVerify, "no Revit API",
            "v6.0 deliverables verification smoke doc avoids live Revit claims.", "docs/smoke/v6.0/deliverables-verify.md", V60RevitApiContradictions);
        AddGuardedContains(report, "v6.0:deliverables-no-revit-runtime-smoke-doc", deliverablesVerify, "starting Revit",
            "v6.0 deliverables verification smoke doc avoids starting Revit.", "docs/smoke/v6.0/deliverables-verify.md", V60RevitRuntimeContradictions);
        AddGuardedContains(report, "v6.0:deliverables-no-saas-smoke-doc", deliverablesVerify, "SaaS",
            "v6.0 deliverables verification smoke doc keeps SaaS out.", "docs/smoke/v6.0/deliverables-verify.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:deliverables-no-mcp-smoke-doc", deliverablesVerify, "MCP",
            "v6.0 deliverables verification smoke doc keeps MCP out.", "docs/smoke/v6.0/deliverables-verify.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:deliverables-no-llm-smoke-doc", deliverablesVerify, "built-in LLM",
            "v6.0 deliverables verification smoke doc keeps built-in LLM behavior out.", "docs/smoke/v6.0/deliverables-verify.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:deliverables-no-dashboard-central-smoke-doc", deliverablesVerify, "dashboard-central",
            "v6.0 deliverables verification smoke doc keeps dashboard-central state out.", "docs/smoke/v6.0/deliverables-verify.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:deliverables-no-db-smoke-doc", deliverablesVerify, "database",
            "v6.0 deliverables verification smoke doc avoids a centralized deliverables database claim.", "docs/smoke/v6.0/deliverables-verify.md", V60DatabaseContradictions);

        var ledgerQueryPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-query.md"));
        if (!File.Exists(ledgerQueryPath))
        {
            report.Add("v6.0:ledger-query-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-query.md; v6.0 ledger query behavior is not documented.",
                "docs/smoke/v6.0/ledger-query.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerQuery = ReadText(ledgerQueryPath, report, "v6.0:ledger-query-smoke-doc-readable");
        if (ledgerQuery is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-query-smoke-doc", ledgerQuery, "read-only ledger query",
            "v6.0 ledger query smoke doc describes the read-only scope.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-schema-smoke-doc", ledgerQuery, "ledger-query.v1",
            "v6.0 ledger query smoke doc documents the output schema.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-sources-smoke-doc", ledgerQuery, "journal",
            "v6.0 ledger query smoke doc includes journal source evidence.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-history-smoke-doc", ledgerQuery, "history",
            "v6.0 ledger query smoke doc includes history source evidence.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-delivery-smoke-doc", ledgerQuery, "delivery",
            "v6.0 ledger query smoke doc includes delivery source evidence.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-workflow-smoke-doc", ledgerQuery, "workflow receipt",
            "v6.0 ledger query smoke doc includes workflow receipt evidence.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-ordering-smoke-doc", ledgerQuery, "timestamp/source/path/line ordering",
            "v6.0 ledger query smoke doc documents deterministic ordering keys.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-output-parity-smoke-doc", ledgerQuery, "JSON/table/Markdown output semantic parity",
            "v6.0 ledger query smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-query.md");
        AddContains(report, "v6.0:ledger-query-malformed-smoke-doc", ledgerQuery, "malformed",
            "v6.0 ledger query smoke doc covers malformed artifacts as issues.", "docs/smoke/v6.0/ledger-query.md");
        AddGuardedContains(report, "v6.0:ledger-query-no-write-smoke-doc", ledgerQuery, "event-level no-write evidence",
            "v6.0 ledger query smoke doc documents no-write evidence.", "docs/smoke/v6.0/ledger-query.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-query-no-revit-smoke-doc", ledgerQuery, "start Revit",
            "v6.0 ledger query smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-query.md", V60RevitRuntimeContradictions);
        AddContains(report, "v6.0:ledger-query-final-snapshot-smoke-doc", ledgerQuery, "final file-tree snapshot evidence",
            "v6.0 ledger query smoke doc documents final file-tree snapshot evidence.", "docs/smoke/v6.0/ledger-query.md");
        AddGuardedContains(report, "v6.0:ledger-query-no-db-smoke-doc", ledgerQuery, "database",
            "v6.0 ledger query smoke doc avoids a centralized ledger database claim.", "docs/smoke/v6.0/ledger-query.md", V60DatabaseContradictions);

        var ledgerAppendPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-append.md"));
        if (!File.Exists(ledgerAppendPath))
        {
            report.Add("v6.0:ledger-append-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-append.md; v6.0 ledger append behavior is not documented.",
                "docs/smoke/v6.0/ledger-append.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerAppend = ReadText(ledgerAppendPath, report, "v6.0:ledger-append-smoke-doc-readable");
        if (ledgerAppend is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-append-smoke-doc", ledgerAppend, "append-only ledger runtime",
            "v6.0 ledger append smoke doc describes the staged runtime scope.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-command-smoke-doc", ledgerAppend, "ledger append",
            "v6.0 ledger append smoke doc exposes the command.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-schema-smoke-doc", ledgerAppend, "ledger-append.v1",
            "v6.0 ledger append smoke doc documents the output schema.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-record-schema-smoke-doc", ledgerAppend, "ledger-operation.v1",
            "v6.0 ledger append smoke doc documents the appended record schema.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-path-smoke-doc", ledgerAppend, ".revitcli/ledger/operations.jsonl",
            "v6.0 ledger append smoke doc documents the local JSONL path.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-dry-run-smoke-doc", ledgerAppend, "dry-run default",
            "v6.0 ledger append smoke doc documents dry-run default behavior.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-approval-smoke-doc", ledgerAppend, "--yes",
            "v6.0 ledger append smoke doc documents explicit approval.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-source-smoke-doc", ledgerAppend, "source ledger",
            "v6.0 ledger append smoke doc documents ledger-source readback.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-evidence-smoke-doc", ledgerAppend, "deterministic evidence links",
            "v6.0 ledger append smoke doc documents deterministic evidence links.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-output-parity-smoke-doc", ledgerAppend, "JSON/table/Markdown output semantic parity",
            "v6.0 ledger append smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-append.md");
        AddContains(report, "v6.0:ledger-append-bounded-write-smoke-doc", ledgerAppend, "bounded local write evidence",
            "v6.0 ledger append smoke doc documents bounded local write evidence.", "docs/smoke/v6.0/ledger-append.md");
        AddGuardedContains(report, "v6.0:ledger-append-no-revit-smoke-doc", ledgerAppend, "start Revit",
            "v6.0 ledger append smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-append.md", V60RevitRuntimeContradictions);
        AddGuardedContains(report, "v6.0:ledger-append-no-db-smoke-doc", ledgerAppend, "database",
            "v6.0 ledger append smoke doc avoids a centralized ledger database claim.", "docs/smoke/v6.0/ledger-append.md", V60DatabaseContradictions);

        var ledgerReplayPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-replay.md"));
        if (!File.Exists(ledgerReplayPath))
        {
            report.Add("v6.0:ledger-replay-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-replay.md; v6.0 ledger replay preview behavior is not documented.",
                "docs/smoke/v6.0/ledger-replay.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerReplay = ReadText(ledgerReplayPath, report, "v6.0:ledger-replay-smoke-doc-readable");
        if (ledgerReplay is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-replay-smoke-doc", ledgerReplay, "ledger replay",
            "v6.0 ledger replay smoke doc exposes the command.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-schema-smoke-doc", ledgerReplay, "ledger-replay.v1",
            "v6.0 ledger replay smoke doc documents the output schema.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-preview-smoke-doc", ledgerReplay, "preview-only",
            "v6.0 ledger replay smoke doc documents preview-only scope.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-dry-run-smoke-doc", ledgerReplay, "dryRun",
            "v6.0 ledger replay smoke doc documents dryRun=true.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-apply-supported-smoke-doc", ledgerReplay, "applySupported",
            "v6.0 ledger replay smoke doc documents applySupported=false.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-can-apply-smoke-doc", ledgerReplay, "canApply",
            "v6.0 ledger replay smoke doc documents per-step canApply=false.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-source-smoke-doc", ledgerReplay, "source ledger",
            "v6.0 ledger replay smoke doc documents source-ledger preview.", "docs/smoke/v6.0/ledger-replay.md");
        AddContains(report, "v6.0:ledger-replay-output-parity-smoke-doc", ledgerReplay, "JSON/table/Markdown output semantic parity",
            "v6.0 ledger replay smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-replay.md");
        AddGuardedContains(report, "v6.0:ledger-replay-no-write-smoke-doc", ledgerReplay, "does not write files",
            "v6.0 ledger replay smoke doc avoids local write claims.", "docs/smoke/v6.0/ledger-replay.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-replay-no-revit-smoke-doc", ledgerReplay, "does not start Revit",
            "v6.0 ledger replay smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-replay.md", V60RevitRuntimeContradictions);
        AddGuardedContains(report, "v6.0:ledger-replay-no-db-smoke-doc", ledgerReplay, "no database",
            "v6.0 ledger replay smoke doc avoids a centralized ledger database claim.", "docs/smoke/v6.0/ledger-replay.md", V60DatabaseContradictions);

        var ledgerValidatePath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-validate.md"));
        if (!File.Exists(ledgerValidatePath))
        {
            report.Add("v6.0:ledger-validate-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-validate.md; v6.0 ledger validation behavior is not documented.",
                "docs/smoke/v6.0/ledger-validate.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerValidate = ReadText(ledgerValidatePath, report, "v6.0:ledger-validate-smoke-doc-readable");
        if (ledgerValidate is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-validate-smoke-doc", ledgerValidate, "read-only ledger validate",
            "v6.0 ledger validation smoke doc describes the read-only scope.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-schema-smoke-doc", ledgerValidate, "ledger-validate.v1",
            "v6.0 ledger validation smoke doc documents the output schema.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-sources-smoke-doc", ledgerValidate, "source readability",
            "v6.0 ledger validation smoke doc documents source-readability checks.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-checks-smoke-doc", ledgerValidate, "artifact links",
            "v6.0 ledger validation smoke doc documents artifact-link checks.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-receipts-smoke-doc", ledgerValidate, "receipt status",
            "v6.0 ledger validation smoke doc documents receipt-status checks.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-receipt-hash-smoke-doc", ledgerValidate, "receipt hash",
            "v6.0 ledger validation smoke doc documents declared receipt-hash checks.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-timestamp-smoke-doc", ledgerValidate, "timestamp format",
            "v6.0 ledger validation smoke doc documents timestamp-format checks.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-timestamp-offset-smoke-doc", ledgerValidate, "explicit UTC offset",
            "v6.0 ledger validation smoke doc documents explicit timestamp offset checks.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-time-filter-timestamp-smoke-doc", ledgerValidate, "time filters preserve invalid timestamp warnings",
            "v6.0 ledger validation smoke doc documents invalid timestamp warning preservation under time filters.", "docs/smoke/v6.0/ledger-validate.md");
        AddContains(report, "v6.0:ledger-validate-output-parity-smoke-doc", ledgerValidate, "validation JSON/table/Markdown semantic parity",
            "v6.0 ledger validation smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-validate.md");
        AddGuardedContains(report, "v6.0:ledger-validate-no-write-smoke-doc", ledgerValidate, "does not write files",
            "v6.0 ledger validation smoke doc avoids local write claims.", "docs/smoke/v6.0/ledger-validate.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-validate-no-write-evidence-smoke-doc", ledgerValidate, "event-level no-write evidence",
            "v6.0 ledger validation smoke doc documents no-write evidence.", "docs/smoke/v6.0/ledger-validate.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-validate-no-revit-smoke-doc", ledgerValidate, "start Revit",
            "v6.0 ledger validation smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-validate.md", V60RevitRuntimeContradictions);
        AddContains(report, "v6.0:ledger-validate-final-snapshot-smoke-doc", ledgerValidate, "final file-tree snapshot evidence",
            "v6.0 ledger validation smoke doc documents final file-tree snapshot evidence.", "docs/smoke/v6.0/ledger-validate.md");
        AddGuardedContains(report, "v6.0:ledger-validate-no-db-smoke-doc", ledgerValidate, "database",
            "v6.0 ledger validation smoke doc avoids a centralized ledger database claim.", "docs/smoke/v6.0/ledger-validate.md", V60DatabaseContradictions);

        var ledgerStatsPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-stats.md"));
        if (!File.Exists(ledgerStatsPath))
        {
            report.Add("v6.0:ledger-stats-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-stats.md; v6.0 ledger stats behavior is not documented.",
                "docs/smoke/v6.0/ledger-stats.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerStats = ReadText(ledgerStatsPath, report, "v6.0:ledger-stats-smoke-doc-readable");
        if (ledgerStats is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-stats-smoke-doc", ledgerStats, "read-only ledger stats",
            "v6.0 ledger stats smoke doc describes the read-only scope.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-schema-smoke-doc", ledgerStats, "ledger-stats.v1",
            "v6.0 ledger stats smoke doc documents the output schema.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-counts-smoke-doc", ledgerStats, "operation counts",
            "v6.0 ledger stats smoke doc documents operation-count summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-source-smoke-doc", ledgerStats, "source counts",
            "v6.0 ledger stats smoke doc documents source summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-action-smoke-doc", ledgerStats, "action counts",
            "v6.0 ledger stats smoke doc documents action summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-category-operator-smoke-doc", ledgerStats, "category and operator counts",
            "v6.0 ledger stats smoke doc documents category and operator summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-receipts-smoke-doc", ledgerStats, "receipt status counts",
            "v6.0 ledger stats smoke doc documents receipt-status summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-issue-source-smoke-doc", ledgerStats, "issue source counts",
            "v6.0 ledger stats smoke doc documents issue-source summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-issues-smoke-doc", ledgerStats, "issue severity counts",
            "v6.0 ledger stats smoke doc documents issue-severity summaries.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-malformed-smoke-doc", ledgerStats, "malformed journal, delivery manifest, and workflow receipt artifacts",
            "v6.0 ledger stats smoke doc documents malformed artifact issue handling.", "docs/smoke/v6.0/ledger-stats.md");
        AddContains(report, "v6.0:ledger-stats-output-parity-smoke-doc", ledgerStats, "JSON/table/Markdown stats semantic parity",
            "v6.0 ledger stats smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-stats.md");
        AddGuardedContains(report, "v6.0:ledger-stats-no-write-smoke-doc", ledgerStats, "event-level no-write evidence",
            "v6.0 ledger stats smoke doc documents no-write event evidence.", "docs/smoke/v6.0/ledger-stats.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-stats-no-revit-smoke-doc", ledgerStats, "start Revit",
            "v6.0 ledger stats smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-stats.md", V60RevitRuntimeContradictions);
        AddContains(report, "v6.0:ledger-stats-final-snapshot-smoke-doc", ledgerStats, "final file-tree snapshot evidence",
            "v6.0 ledger stats smoke doc documents final file-tree snapshot evidence.", "docs/smoke/v6.0/ledger-stats.md");
        AddGuardedContains(report, "v6.0:ledger-stats-no-db-smoke-doc", ledgerStats, "database",
            "v6.0 ledger stats smoke doc avoids a centralized ledger database claim.", "docs/smoke/v6.0/ledger-stats.md", V60DatabaseContradictions);

        var ledgerTimelinePath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-timeline.md"));
        if (!File.Exists(ledgerTimelinePath))
        {
            report.Add("v6.0:ledger-timeline-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-timeline.md; v6.0 ledger timeline behavior is not documented.",
                "docs/smoke/v6.0/ledger-timeline.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerTimeline = ReadText(ledgerTimelinePath, report, "v6.0:ledger-timeline-smoke-doc-readable");
        if (ledgerTimeline is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-timeline-smoke-doc", ledgerTimeline, "read-only ledger timeline",
            "v6.0 ledger timeline smoke doc describes the read-only scope.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-schema-smoke-doc", ledgerTimeline, "ledger-timeline.v1",
            "v6.0 ledger timeline smoke doc documents the output schema.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-bucket-smoke-doc", ledgerTimeline, "bucket",
            "v6.0 ledger timeline smoke doc documents bucketed timeline output.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-source-smoke-doc", ledgerTimeline, "source",
            "v6.0 ledger timeline smoke doc documents source summaries.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-action-smoke-doc", ledgerTimeline, "action",
            "v6.0 ledger timeline smoke doc documents action summaries.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-category-smoke-doc", ledgerTimeline, "category counts per bucket",
            "v6.0 ledger timeline smoke doc documents category summaries per bucket.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-operator-smoke-doc", ledgerTimeline, "operator counts per bucket",
            "v6.0 ledger timeline smoke doc documents operator summaries per bucket.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-receipts-smoke-doc", ledgerTimeline, "receipt status",
            "v6.0 ledger timeline smoke doc documents receipt-status summaries.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-issues-smoke-doc", ledgerTimeline, "issue severity",
            "v6.0 ledger timeline smoke doc documents issue-severity summaries.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-output-parity-smoke-doc", ledgerTimeline, "JSON/table/Markdown timeline semantic parity",
            "v6.0 ledger timeline smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-unbucketed-smoke-doc", ledgerTimeline, "unbucketed timestamp",
            "v6.0 ledger timeline smoke doc documents unbucketed timestamp handling.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-timestamp-offset-smoke-doc", ledgerTimeline, "explicit UTC offset",
            "v6.0 ledger timeline smoke doc documents explicit timestamp offset handling.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-time-filter-timestamp-smoke-doc", ledgerTimeline, "time filters preserve unbucketed timestamp warnings",
            "v6.0 ledger timeline smoke doc documents unbucketed timestamp warning preservation under time filters.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-cross-project-smoke-doc", ledgerTimeline, "projectDirectories",
            "v6.0 ledger timeline smoke doc documents explicit cross-project project directory evidence.", "docs/smoke/v6.0/ledger-timeline.md");
        AddContains(report, "v6.0:ledger-timeline-by-project-smoke-doc", ledgerTimeline, "byProject",
            "v6.0 ledger timeline smoke doc documents per-project timeline counts.", "docs/smoke/v6.0/ledger-timeline.md");
        AddGuardedContains(report, "v6.0:ledger-timeline-no-write-smoke-doc", ledgerTimeline, "does not write files",
            "v6.0 ledger timeline smoke doc avoids local write claims.", "docs/smoke/v6.0/ledger-timeline.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-timeline-no-write-evidence-smoke-doc", ledgerTimeline, "event-level no-write evidence",
            "v6.0 ledger timeline smoke doc documents no-write event evidence.", "docs/smoke/v6.0/ledger-timeline.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:ledger-timeline-no-revit-smoke-doc", ledgerTimeline, "start Revit",
            "v6.0 ledger timeline smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-timeline.md", V60RevitRuntimeContradictions);
        AddContains(report, "v6.0:ledger-timeline-final-snapshot-smoke-doc", ledgerTimeline, "final file-tree snapshot evidence",
            "v6.0 ledger timeline smoke doc documents final file-tree snapshot evidence.", "docs/smoke/v6.0/ledger-timeline.md");
        AddGuardedContains(report, "v6.0:ledger-timeline-no-db-smoke-doc", ledgerTimeline, "database",
            "v6.0 ledger timeline smoke doc avoids a centralized ledger database claim.", "docs/smoke/v6.0/ledger-timeline.md", V60DatabaseContradictions);

        var ledgerAnalyticsPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/ledger-analytics.md"));
        if (!File.Exists(ledgerAnalyticsPath))
        {
            report.Add("v6.0:ledger-analytics-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/ledger-analytics.md; v6.0 ledger analytics bundle behavior is not documented.",
                "docs/smoke/v6.0/ledger-analytics.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var ledgerAnalytics = ReadText(ledgerAnalyticsPath, report, "v6.0:ledger-analytics-smoke-doc-readable");
        if (ledgerAnalytics is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:ledger-analytics-smoke-doc", ledgerAnalytics, "ledger analytics",
            "v6.0 ledger analytics smoke doc exposes the bundle command.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-schema-smoke-doc", ledgerAnalytics, "ledger-analytics-bundle.v1",
            "v6.0 ledger analytics smoke doc documents the bundle output schema.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-stats-schema-smoke-doc", ledgerAnalytics, "ledger-stats.v1",
            "v6.0 ledger analytics smoke doc documents the stats snapshot schema.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-timeline-schema-smoke-doc", ledgerAnalytics, "ledger-timeline.v1",
            "v6.0 ledger analytics smoke doc documents the timeline snapshot schema.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-output-parity-smoke-doc", ledgerAnalytics, "JSON/table/Markdown output formats",
            "v6.0 ledger analytics smoke doc documents output-format semantic parity.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-local-only-smoke-doc", ledgerAnalytics, "localOnly=true",
            "v6.0 ledger analytics smoke doc documents the local-only boundary flag.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-no-db-flag-smoke-doc", ledgerAnalytics, "databaseRuntime=false",
            "v6.0 ledger analytics smoke doc documents the no-database boundary flag.", "docs/smoke/v6.0/ledger-analytics.md");
        AddContains(report, "v6.0:ledger-analytics-no-network-flag-smoke-doc", ledgerAnalytics, "networkService=false",
            "v6.0 ledger analytics smoke doc documents the no-network-service boundary flag.", "docs/smoke/v6.0/ledger-analytics.md");
        AddGuardedContains(report, "v6.0:ledger-analytics-no-revit-smoke-doc", ledgerAnalytics, "start Revit",
            "v6.0 ledger analytics smoke doc avoids starting Revit.", "docs/smoke/v6.0/ledger-analytics.md", V60RevitRuntimeContradictions);
        AddContains(report, "v6.0:ledger-analytics-no-network-smoke-doc", ledgerAnalytics, "does not call a network service",
            "v6.0 ledger analytics smoke doc avoids a network service claim.", "docs/smoke/v6.0/ledger-analytics.md");
        AddGuardedContains(report, "v6.0:ledger-analytics-no-db-smoke-doc", ledgerAnalytics, "database",
            "v6.0 ledger analytics smoke doc avoids a database runtime claim.", "docs/smoke/v6.0/ledger-analytics.md", V60DatabaseContradictions);

        var workflowRegistryPath = Path.Combine(root, ToNativePath("docs/smoke/v6.0/workflow-registry.md"));
        if (!File.Exists(workflowRegistryPath))
        {
            report.Add("v6.0:workflow-registry-smoke-doc", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/workflow-registry.md; v6.0 workflow registry behavior is not documented.",
                "docs/smoke/v6.0/workflow-registry.md");
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        var workflowRegistry = ReadText(workflowRegistryPath, report, "v6.0:workflow-registry-smoke-doc-readable");
        if (workflowRegistry is null)
        {
            AddV60WorkbenchGate(root, report, workbenchVerify);
            return;
        }

        AddContains(report, "v6.0:workflow-registry-smoke-doc", workflowRegistry, "read-only workflow registry",
            "v6.0 workflow registry smoke doc describes the read-only scope.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-schema-smoke-doc", workflowRegistry, "workflow-registry.v1",
            "v6.0 workflow registry smoke doc documents the output schema.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-inputs-smoke-doc", workflowRegistry, "inputs",
            "v6.0 workflow registry smoke doc documents inputs.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-outputs-smoke-doc", workflowRegistry, "outputs",
            "v6.0 workflow registry smoke doc documents outputs.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-scope-smoke-doc", workflowRegistry, "read/write scope",
            "v6.0 workflow registry smoke doc documents read/write scope.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-risk-smoke-doc", workflowRegistry, "risk level",
            "v6.0 workflow registry smoke doc documents risk level.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-dry-run-smoke-doc", workflowRegistry, "dry-run command",
            "v6.0 workflow registry smoke doc documents dry-run commands.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-approval-smoke-doc", workflowRegistry, "approval command",
            "v6.0 workflow registry smoke doc documents approval commands.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-rollback-smoke-doc", workflowRegistry, "rollback support",
            "v6.0 workflow registry smoke doc documents rollback support.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-receipt-smoke-doc", workflowRegistry, "receipt schema",
            "v6.0 workflow registry smoke doc documents receipt schemas.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-evidence-smoke-doc", workflowRegistry, "acceptance evidence",
            "v6.0 workflow registry smoke doc documents acceptance evidence.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-schedule-manifest-smoke-doc", workflowRegistry, "schedule-export-manifest.v1",
            "v6.0 workflow registry smoke doc documents schedule export manifest inference.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-publish-receipt-smoke-doc", workflowRegistry, "publish-receipt.v1",
            "v6.0 workflow registry smoke doc documents publish receipt inference.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-journal-verify-smoke-doc", workflowRegistry, "journal verify",
            "v6.0 workflow registry smoke doc documents journal verification evidence inference.", "docs/smoke/v6.0/workflow-registry.md");
        AddContains(report, "v6.0:workflow-registry-output-parity-smoke-doc", workflowRegistry, "JSON/table/Markdown output semantic parity",
            "v6.0 workflow registry smoke doc documents output semantic parity.", "docs/smoke/v6.0/workflow-registry.md");
        AddGuardedContains(report, "v6.0:workflow-registry-no-write-smoke-doc", workflowRegistry, "does not write files",
            "v6.0 workflow registry smoke doc avoids local write claims.", "docs/smoke/v6.0/workflow-registry.md", V60LocalWriteContradictions);
        AddContains(report, "v6.0:workflow-registry-final-snapshot-smoke-doc", workflowRegistry, "final file-tree snapshot evidence",
            "v6.0 workflow registry smoke doc documents final file-tree snapshot evidence.", "docs/smoke/v6.0/workflow-registry.md");
        AddGuardedContains(report, "v6.0:workflow-registry-event-no-write-smoke-doc", workflowRegistry, "event-level no-write evidence",
            "v6.0 workflow registry smoke doc documents event-level no-write evidence.", "docs/smoke/v6.0/workflow-registry.md", V60LocalWriteContradictions);
        AddGuardedContains(report, "v6.0:workflow-registry-no-revit-smoke-doc", workflowRegistry, "start Revit",
            "v6.0 workflow registry smoke doc avoids starting Revit.", "docs/smoke/v6.0/workflow-registry.md", V60RevitRuntimeContradictions);
        AddGuardedContains(report, "v6.0:workflow-registry-no-saas-smoke-doc", workflowRegistry, "no SaaS",
            "v6.0 workflow registry smoke doc avoids SaaS claims.", "docs/smoke/v6.0/workflow-registry.md", V60SaasContradictions);
        AddGuardedContains(report, "v6.0:workflow-registry-no-mcp-smoke-doc", workflowRegistry, "MCP",
            "v6.0 workflow registry smoke doc keeps MCP out of the baseline.", "docs/smoke/v6.0/workflow-registry.md", V60McpContradictions);
        AddGuardedContains(report, "v6.0:workflow-registry-no-llm-smoke-doc", workflowRegistry, "built-in LLM",
            "v6.0 workflow registry smoke doc keeps built-in LLM behavior out of the baseline.", "docs/smoke/v6.0/workflow-registry.md", V60LlmContradictions);
        AddGuardedContains(report, "v6.0:workflow-registry-no-dashboard-central-smoke-doc", workflowRegistry, "dashboard-central",
            "v6.0 workflow registry smoke doc keeps dashboard-central state out of the baseline.", "docs/smoke/v6.0/workflow-registry.md", V60DashboardCentralContradictions);
        AddGuardedContains(report, "v6.0:workflow-registry-no-db-smoke-doc", workflowRegistry, "database",
            "v6.0 workflow registry smoke doc avoids a centralized workflow database claim.", "docs/smoke/v6.0/workflow-registry.md", V60DatabaseContradictions);

        AddV60WorkbenchGate(root, report, workbenchVerify);
    }

    private static void AddV60OfficeRolloutStatus(string root, ReleaseVerifyReport report)
    {
        const string path = "docs/smoke/v6.0/office-rollout-status.json";
        var fullPath = Path.Combine(root, ToNativePath(path));
        if (!File.Exists(fullPath))
        {
            report.Add("v6.0:office-rollout-status-json", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/office-rollout-status.json; release gate cannot distinguish documented pilot intake from completed office rollout evidence.",
                path);
            return;
        }

        if (!TryReadJsonRoot(fullPath, report, "v6.0:office-rollout-status-json-readable", out var status))
            return;

        var minimumCount = JsonInt(status, "minimumOfficePilotCount");
        var completedCount = JsonInt(status, "completedOfficePilotCount");
        var completionClaim = JsonBool(status, "officeRolloutCompletion");
        var supportClaim = JsonBool(status, "productionSupportClaim");
        var hasPilotCounts = minimumCount.HasValue && completedCount.HasValue;
        var completedPilotsComplete = completedCount.HasValue &&
            CompletedOfficePilotEvidenceComplete(root, status, completedCount.Value);
        var requiredEvidenceComplete =
            JsonBoolEquals(status, "requiredEvidence.doctor", true) &&
            JsonBoolEquals(status, "requiredEvidence.status", true) &&
            JsonBoolEquals(status, "requiredEvidence.workbench", true) &&
            JsonBoolEquals(status, "requiredEvidence.release", true) &&
            JsonBoolEquals(status, "requiredEvidence.ledgerQuery", true) &&
            JsonBoolEquals(status, "requiredEvidence.ledgerValidate", true) &&
            JsonBoolEquals(status, "requiredEvidence.ledgerStatsAnalyticsSnapshot", true) &&
            JsonBoolEquals(status, "requiredEvidence.ledgerTimelineAnalyticsSnapshot", true) &&
            JsonBoolEquals(status, "requiredEvidence.journalVerify", true) &&
            JsonBoolEquals(status, "requiredEvidence.rollbackResult", true) &&
            JsonBoolEquals(status, "requiredEvidence.userReview", true) &&
            JsonBoolEquals(status, "requiredEvidence.bimManagerSignoff", true) &&
            JsonBoolEquals(status, "requiredEvidence.projectCopyOwnerSignoff", true) &&
            JsonBoolEquals(status, "requiredEvidence.supportTicketReview", true) &&
            JsonBoolEquals(status, "requiredEvidence.multiUserRolloutPostmortem", true);
        var belowMinimum = hasPilotCounts && completedCount.GetValueOrDefault() < minimumCount.GetValueOrDefault();
        var reachedMinimum = hasPilotCounts && completedCount.GetValueOrDefault() >= minimumCount.GetValueOrDefault();

        AddJsonCheck(report, "v6.0:office-rollout-status-json",
            JsonStringEquals(status, "schemaVersion", "v6-office-rollout-status.v1") &&
            hasPilotCounts &&
            minimumCount.GetValueOrDefault() >= 2 &&
            completedCount.GetValueOrDefault() >= 0 &&
            JsonArrayLengthEquals(status, "completedPilotIds", completedCount.GetValueOrDefault()) &&
            JsonArrayLengthEquals(status, "completedPilots", completedCount.GetValueOrDefault()),
            "v6.0 office rollout status JSON records the pilot-count threshold, current completed pilot count, and per-pilot evidence list.",
            $"Expected {path} to use schema v6-office-rollout-status.v1, minimumOfficePilotCount>=2, completedOfficePilotCount>=0, and matching completedPilotIds/completedPilots counts.",
            path);
        AddJsonCheck(report, "v6.0:office-rollout-status-no-overclaim-json",
            (belowMinimum && completionClaim is false && supportClaim is false) ||
            (reachedMinimum && completionClaim.HasValue && supportClaim.HasValue && requiredEvidenceComplete && completedPilotsComplete),
            "v6.0 office rollout status JSON is consistent with the pilot threshold and does not overclaim production support.",
            $"Expected {path} to keep completion/support false below the pilot threshold, or require completed pilots with full per-pilot evidence when the threshold is reached.",
            path);
        AddJsonCheck(report, "v6.0:office-rollout-status-required-evidence-json",
            requiredEvidenceComplete,
            "v6.0 office rollout status JSON requires command evidence, review, signoff, support review, and postmortem fields.",
            $"Expected {path} requiredEvidence to require all office rollout command/review/signoff/postmortem fields.",
            path);
        AddJsonCheck(report, "v6.0:office-rollout-status-completed-pilots-json",
            completedPilotsComplete,
            "v6.0 office rollout status JSON has a completedPilots entry with complete evidence flags and a matching evidence packet Pilot identifier for each completed pilot.",
            $"Expected {path} completedPilots to contain one complete per-pilot evidence object and a matching packet Pilot identifier for each completed pilot.",
            path);
    }

    private static bool CompletedOfficePilotEvidenceComplete(string root, JsonElement status, int expectedCount)
    {
        if (!TryReadUniqueStringArray(status, "completedPilotIds", expectedCount, out var completedPilotIds))
            return false;

        if (!TryGetJsonProperty(status, "completedPilots", out var completedPilots) ||
            completedPilots.ValueKind != JsonValueKind.Array ||
            completedPilots.GetArrayLength() != expectedCount)
        {
            return false;
        }

        var evidencePilotIds = new HashSet<string>(StringComparer.Ordinal);
        return completedPilots.EnumerateArray().All(pilot =>
            TryReadNonEmptyJsonString(pilot, "pilotId", out var pilotId) &&
            completedPilotIds.Contains(pilotId) &&
            evidencePilotIds.Add(pilotId) &&
            JsonPublicOfficePilotEvidencePacketPath(pilot, "evidencePacketPath") &&
            CompletedOfficePilotEvidencePacketComplete(root, JsonString(pilot, "evidencePacketPath"), pilotId) &&
            JsonBoolEquals(pilot, "doctor", true) &&
            JsonBoolEquals(pilot, "status", true) &&
            JsonBoolEquals(pilot, "workbench", true) &&
            JsonBoolEquals(pilot, "release", true) &&
            JsonBoolEquals(pilot, "ledgerQuery", true) &&
            JsonBoolEquals(pilot, "ledgerValidate", true) &&
            JsonBoolEquals(pilot, "ledgerStatsAnalyticsSnapshot", true) &&
            JsonBoolEquals(pilot, "ledgerTimelineAnalyticsSnapshot", true) &&
            JsonBoolEquals(pilot, "journalVerify", true) &&
            JsonBoolEquals(pilot, "rollbackResult", true) &&
            JsonBoolEquals(pilot, "userReview", true) &&
            JsonBoolEquals(pilot, "bimManagerSignoff", true) &&
            JsonBoolEquals(pilot, "projectCopyOwnerSignoff", true) &&
            JsonBoolEquals(pilot, "supportTicketReview", true) &&
            JsonBoolEquals(pilot, "multiUserRolloutPostmortem", true));
    }

    private static bool JsonPublicOfficePilotEvidencePacketPath(JsonElement element, string path)
    {
        if (!TryReadNonEmptyJsonString(element, path, out var value))
            return false;

        var trimmed = value.Trim();
        return !trimmed.Contains('\\', StringComparison.Ordinal) &&
            !trimmed.Contains(':', StringComparison.Ordinal) &&
            !trimmed.StartsWith("/", StringComparison.Ordinal) &&
            !trimmed.Contains("../", StringComparison.Ordinal) &&
            !trimmed.Contains("/..", StringComparison.Ordinal) &&
            trimmed.StartsWith("docs/smoke/v6.0/", StringComparison.Ordinal) &&
            trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompletedOfficePilotEvidencePacketComplete(string root, string? relativePath, string expectedPilotId)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var fullPath = Path.Combine(root, ToNativePath(relativePath.Trim()));
        if (!File.Exists(fullPath))
            return false;

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return ContainsPilotIdentifier(text, expectedPilotId) &&
            ContainsAll(text,
            "Pilot identifier",
            "Required Commands",
            "doctor --check-version 2026 --output json",
            "status --output json",
            "workbench verify --contract workbench-contract.v2",
            "release verify --strict --output json",
            "ledger query --source ledger --output json",
            "ledger validate --source ledger --output json",
            "ledger stats --source ledger --analytics-snapshot",
            "ledger timeline --source ledger --analytics-snapshot",
            "journal verify --output json",
            "Live Operation Evidence",
            "Rollback result",
            "User Review",
            "BIM manager signoff",
            "Project-copy owner signoff",
            "Support ticket review",
            "Multi-user rollout postmortem",
            "Boundary summary");
    }

    private static bool ContainsPilotIdentifier(string text, string expectedPilotId)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
                continue;

            var label = trimmed[2..separator].Trim();
            if (!string.Equals(label, "Pilot identifier", StringComparison.OrdinalIgnoreCase))
                continue;

            return string.Equals(
                trimmed[(separator + 1)..].Trim(),
                expectedPilotId,
                StringComparison.Ordinal);
        }

        return false;
    }

    private static bool ContainsAll(string text, params string[] values) =>
        values.All(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool TryReadUniqueStringArray(
        JsonElement element,
        string path,
        int expectedCount,
        out HashSet<string> values)
    {
        values = new HashSet<string>(StringComparer.Ordinal);
        if (!TryGetJsonProperty(element, path, out var property) ||
            property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() != expectedCount)
        {
            return false;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()) ||
                !values.Add(item.GetString()!))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddV60LocalControlledPilotEvidenceSummary(string root, ReleaseVerifyReport report)
    {
        const string path = "docs/smoke/v6.0/local-controlled-pilot-20260525.evidence.json";
        const string sourceBundle = "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525";
        var fullPath = Path.Combine(root, ToNativePath(path));
        if (!File.Exists(fullPath))
        {
            report.Add("v6.0:local-controlled-pilot-evidence-json", ReleaseVerifyStatus.Error,
                "Missing docs/smoke/v6.0/local-controlled-pilot-20260525.evidence.json; release gate cannot validate the local controlled pilot JSON evidence summary.",
                path);
            return;
        }

        if (!TryReadJsonRoot(fullPath, report, "v6.0:local-controlled-pilot-evidence-json-readable", out var evidence))
            return;

        AddJsonCheck(report, "v6.0:local-controlled-pilot-evidence-json",
            JsonStringEquals(evidence, "schemaVersion", "v6-local-controlled-pilot-evidence.v1") &&
            JsonStringEquals(evidence, "pilotId", "v6-local-controlled-pilot-20260525") &&
            JsonStringEquals(evidence, "sourceBundle", sourceBundle),
            "v6.0 local controlled pilot evidence JSON summary identifies the stable schema, pilot id, and source bundle.",
            $"Expected {path} to identify schemaVersion=v6-local-controlled-pilot-evidence.v1, pilotId=v6-local-controlled-pilot-20260525, and sourceBundle={sourceBundle}.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-evidence-boundary-json",
            JsonBoolEquals(evidence, "scope.localControlledPilot", true) &&
            JsonBoolEquals(evidence, "scope.officeRolloutCompletion", false) &&
            JsonBoolEquals(evidence, "scope.productionSupportClaim", false),
            "v6.0 local controlled pilot evidence JSON preserves local-only and no-production-claim boundaries.",
            $"Expected {path} scope to mark localControlledPilot=true, officeRolloutCompletion=false, and productionSupportClaim=false.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-required-files-json",
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/doctor.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/status.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/workbench.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/release.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/ledger-query.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/ledger-validate.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/ledger-stats.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/ledger-timeline.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/journal-sign.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/outputs/journal-verify.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/project/.revitcli/ledger/operations.jsonl") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/project/.revitcli/analytics/ledger-stats.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/project/.revitcli/analytics/ledger-timeline.json") &&
            JsonStringArrayContains(evidence, "requiredFiles", $"{sourceBundle}/project/.revitcli/journal.jsonl.sig"),
            "v6.0 local controlled pilot evidence JSON lists the required doctor/status/gate/ledger/analytics/journal source files.",
            $"Expected {path} requiredFiles to list the local controlled pilot source outputs and project evidence files.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-doctor-json",
            JsonBoolEquals(evidence, "doctor.success", true) &&
            JsonIntEquals(evidence, "doctor.targetRevitYear", 2026) &&
            JsonBoolEquals(evidence, "doctor.versionsMatch", true),
            "v6.0 local controlled pilot doctor JSON evidence passed for Revit 2026 with matching CLI/add-in versions.",
            $"Expected {path} doctor evidence to pass for Revit 2026 with matching versions.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-status-json",
            JsonIntEquals(evidence, "status.revitYear", 2026) &&
            JsonStringEquals(evidence, "status.documentName", "revit_cli"),
            "v6.0 local controlled pilot status JSON evidence records the Revit 2026 smoke document.",
            $"Expected {path} status evidence to record Revit 2026 documentName=revit_cli.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-workbench-json",
            JsonBoolEquals(evidence, "workbench.success", true) &&
            JsonIntEquals(evidence, "workbench.issueCount", 0),
            "v6.0 local controlled pilot workbench JSON evidence passed with zero issues.",
            $"Expected {path} workbench evidence to have success=true and issueCount=0.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-release-json",
            JsonBoolEquals(evidence, "release.success", true) &&
            JsonIntEquals(evidence, "release.errorCount", 0) &&
            JsonIntEquals(evidence, "release.warningCount", 0),
            "v6.0 local controlled pilot release JSON evidence passed with zero errors and warnings.",
            $"Expected {path} release evidence to have success=true, errorCount=0, and warningCount=0.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-ledger-query-json",
            JsonStringEquals(evidence, "ledgerQuery.schemaVersion", "ledger-query.v1") &&
            JsonIntAtLeast(evidence, "ledgerQuery.totalOperations", 1) &&
            JsonIntEquals(evidence, "ledgerQuery.issueCount", 0),
            "v6.0 local controlled pilot ledger query JSON evidence records source-ledger operations with zero issues.",
            $"Expected {path} ledgerQuery evidence to use ledger-query.v1 with totalOperations>=1 and issueCount=0.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-ledger-validate-json",
            JsonStringEquals(evidence, "ledgerValidate.schemaVersion", "ledger-validate.v1") &&
            JsonBoolEquals(evidence, "ledgerValidate.valid", true) &&
            JsonIntAtLeast(evidence, "ledgerValidate.operationCount", 1) &&
            JsonIntEquals(evidence, "ledgerValidate.issueCount", 0) &&
            JsonIntEquals(evidence, "ledgerValidate.errorCount", 0) &&
            JsonIntEquals(evidence, "ledgerValidate.warningCount", 0),
            "v6.0 local controlled pilot ledger validation JSON evidence passed with zero issues.",
            $"Expected {path} ledgerValidate evidence to pass ledger-validate.v1 with operationCount>=1 and zero issues/errors/warnings.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-ledger-analytics-json",
            JsonStringEquals(evidence, "ledgerStats.schemaVersion", "ledger-stats.v1") &&
            JsonIntAtLeast(evidence, "ledgerStats.operationCount", 1) &&
            JsonIntEquals(evidence, "ledgerStats.issueCount", 0) &&
            JsonStringEquals(evidence, "ledgerTimeline.schemaVersion", "ledger-timeline.v1") &&
            JsonIntAtLeast(evidence, "ledgerTimeline.operationCount", 1) &&
            JsonIntAtLeast(evidence, "ledgerTimeline.bucketCount", 1) &&
            JsonIntEquals(evidence, "ledgerTimeline.issueCount", 0),
            "v6.0 local controlled pilot ledger stats/timeline JSON evidence passed with operations and zero issues.",
            $"Expected {path} ledgerStats/ledgerTimeline evidence to pass v1 schemas with operationCount>=1 and zero issues.",
            path);
        AddJsonCheck(report, "v6.0:local-controlled-pilot-journal-json",
            JsonBoolEquals(evidence, "journal.isValid", true) &&
            JsonIntAtLeast(evidence, "journal.signEntryCount", 1) &&
            JsonIntAtLeast(evidence, "journal.verifyEntryCount", 1) &&
            JsonStringEquals(evidence, "journal.signRootHash", JsonString(evidence, "journal.rootHash")) &&
            JsonArrayLengthEquals(evidence, "journal.errors", 0),
            "v6.0 local controlled pilot journal JSON evidence verifies signed entries with matching root hash and no errors.",
            $"Expected {path} journal evidence to verify signed entries with matching root hashes and no errors.",
            path);
        AddV60LocalControlledPilotSourceBundle(root, report, sourceBundle);
    }

    private static void AddV60LocalControlledPilotSourceBundle(string root, ReleaseVerifyReport report, string sourceBundle)
    {
        var bundlePath = Path.Combine(root, ToNativePath(sourceBundle));
        if (!Directory.Exists(bundlePath))
        {
            report.Add("v6.0:local-controlled-pilot-source-bundle", ReleaseVerifyStatus.Error,
                $"Local controlled pilot source bundle is not present at {sourceBundle}; strict release verification requires the checked-in public-safe evidence source files.",
                sourceBundle);
            return;
        }

        var required = new[]
        {
            "outputs/doctor.json",
            "outputs/status.json",
            "outputs/workbench.json",
            "outputs/release.json",
            "outputs/ledger-query.json",
            "outputs/ledger-validate.json",
            "outputs/ledger-stats.json",
            "outputs/ledger-timeline.json",
            "outputs/journal-sign.json",
            "outputs/journal-verify.json",
            "project/.revitcli/ledger/operations.jsonl",
            "project/.revitcli/analytics/ledger-stats.json",
            "project/.revitcli/analytics/ledger-timeline.json",
            "project/.revitcli/journal.jsonl.sig",
        };
        var missing = required
            .Where(relative => !File.Exists(Path.Combine(bundlePath, ToNativePath(relative))))
            .ToArray();
        report.Add("v6.0:local-controlled-pilot-source-bundle",
            missing.Length == 0 ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            missing.Length == 0
                ? "v6.0 local controlled pilot source bundle contains the required doctor/status/gate/ledger/analytics/journal files."
                : $"Expected {sourceBundle} to contain required files: {string.Join(", ", missing)}.",
            sourceBundle);

        AddBundleJsonCheck(root, report, sourceBundle, "outputs/doctor.json", "v6.0:local-controlled-pilot-source-doctor-json",
            json => JsonBoolEquals(json, "success", true) && JsonIntEquals(json, "targetRevitYear", 2026),
            "v6.0 local controlled pilot source doctor JSON passed for Revit 2026.",
            "Expected source doctor.json to have success=true and targetRevitYear=2026.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/status.json", "v6.0:local-controlled-pilot-source-status-json",
            json => JsonIntEquals(json, "revitYear", 2026) && JsonStringNonEmpty(json, "documentName"),
            "v6.0 local controlled pilot source status JSON records a Revit 2026 document.",
            "Expected source status.json to have revitYear=2026 and a documentName.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/workbench.json", "v6.0:local-controlled-pilot-source-workbench-json",
            json => JsonBoolEquals(json, "success", true) && JsonIntEquals(json, "issueCount", 0),
            "v6.0 local controlled pilot source workbench JSON passed with zero issues.",
            "Expected source workbench.json to have success=true and issueCount=0.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/release.json", "v6.0:local-controlled-pilot-source-release-json",
            json => JsonBoolEquals(json, "success", true) && JsonIntEquals(json, "errorCount", 0) && JsonIntEquals(json, "warningCount", 0),
            "v6.0 local controlled pilot source release JSON passed with zero errors and warnings.",
            "Expected source release.json to have success=true, errorCount=0, and warningCount=0.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/ledger-query.json", "v6.0:local-controlled-pilot-source-ledger-query-json",
            json => JsonStringEquals(json, "schemaVersion", "ledger-query.v1") && JsonIntAtLeast(json, "summary.totalOperations", 1) && JsonIntEquals(json, "summary.issueCount", 0),
            "v6.0 local controlled pilot source ledger query JSON has operations and zero issues.",
            "Expected source ledger-query.json to use ledger-query.v1 with totalOperations>=1 and issueCount=0.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/ledger-validate.json", "v6.0:local-controlled-pilot-source-ledger-validate-json",
            json => JsonStringEquals(json, "schemaVersion", "ledger-validate.v1") && JsonBoolEquals(json, "valid", true) && JsonIntAtLeast(json, "summary.operationCount", 1) && JsonIntEquals(json, "summary.issueCount", 0) && JsonIntEquals(json, "summary.errorCount", 0),
            "v6.0 local controlled pilot source ledger validation JSON passed with zero issues.",
            "Expected source ledger-validate.json to be valid ledger-validate.v1 with operationCount>=1 and zero issues/errors.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/ledger-stats.json", "v6.0:local-controlled-pilot-source-ledger-stats-json",
            json => JsonStringEquals(json, "schemaVersion", "ledger-stats.v1") && JsonIntAtLeast(json, "summary.operationCount", 1) && JsonIntEquals(json, "summary.issueCount", 0),
            "v6.0 local controlled pilot source ledger stats JSON has operations and zero issues.",
            "Expected source ledger-stats.json to use ledger-stats.v1 with operationCount>=1 and issueCount=0.");
        AddBundleJsonCheck(root, report, sourceBundle, "outputs/ledger-timeline.json", "v6.0:local-controlled-pilot-source-ledger-timeline-json",
            json => JsonStringEquals(json, "schemaVersion", "ledger-timeline.v1") && JsonIntAtLeast(json, "summary.operationCount", 1) && JsonIntAtLeast(json, "summary.bucketCount", 1) && JsonIntEquals(json, "summary.issueCount", 0),
            "v6.0 local controlled pilot source ledger timeline JSON has operations, buckets, and zero issues.",
            "Expected source ledger-timeline.json to use ledger-timeline.v1 with operationCount>=1, bucketCount>=1, and issueCount=0.");
        AddV60LocalControlledPilotSourceJournal(root, report, sourceBundle);
    }

    private static void AddBundleJsonCheck(
        string root,
        ReleaseVerifyReport report,
        string sourceBundle,
        string relativePath,
        string id,
        Func<JsonElement, bool> predicate,
        string okMessage,
        string errorMessage)
    {
        var path = $"{sourceBundle}/{relativePath}";
        var fullPath = Path.Combine(root, ToNativePath(path));
        if (!TryReadJsonRoot(fullPath, report, $"{id}:readable", out var json))
            return;

        AddJsonCheck(report, id, predicate(json), okMessage, errorMessage, path);
    }

    private static void AddV60LocalControlledPilotSourceJournal(string root, ReleaseVerifyReport report, string sourceBundle)
    {
        var signPath = $"{sourceBundle}/outputs/journal-sign.json";
        var verifyPath = $"{sourceBundle}/outputs/journal-verify.json";
        var signFullPath = Path.Combine(root, ToNativePath(signPath));
        var verifyFullPath = Path.Combine(root, ToNativePath(verifyPath));
        if (!TryReadJsonRoot(signFullPath, report, "v6.0:local-controlled-pilot-source-journal-json:sign-readable", out var sign) ||
            !TryReadJsonRoot(verifyFullPath, report, "v6.0:local-controlled-pilot-source-journal-json:verify-readable", out var verify))
        {
            return;
        }

        AddJsonCheck(report, "v6.0:local-controlled-pilot-source-journal-json",
            JsonIntAtLeast(sign, "entryCount", 1) &&
            JsonBoolEquals(verify, "isValid", true) &&
            JsonIntAtLeast(verify, "entryCount", 1) &&
            JsonStringEquals(sign, "rootHash", JsonString(verify, "rootHash")) &&
            JsonArrayLengthEquals(verify, "errors", 0),
            "v6.0 local controlled pilot source journal JSON verifies signed entries with matching root hash and no errors.",
            "Expected source journal-sign.json and journal-verify.json to have signed entries, isValid=true, matching rootHash values, and zero errors.",
            verifyPath);
    }

    private static void AddV60WorkbenchGate(
        string root,
        ReleaseVerifyReport report,
        Lazy<WorkbenchVerifyRun> workbenchVerify)
    {
        var run = workbenchVerify.Value;
        if (run.Error is not null)
        {
            report.Add(
                "v6.0:workbench-gate",
                ReleaseVerifyStatus.Error,
                $"Could not evaluate workbench v2 v6.0 gate for this release root: {run.Error.Message}",
                run.Source);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(run.Output);
            string? status = null;
            string? evidence = null;
            var runtimeEvidenceStatus = "missing";
            IReadOnlyDictionary<string, object?>? runtimeEvidence = null;
            var success = false;
            foreach (var check in document.RootElement.GetProperty("checks").EnumerateArray())
            {
                if (!string.Equals(check.GetProperty("id").GetString(), "v60LocalBimOpsContractGate", StringComparison.Ordinal))
                    continue;

                success = EvaluateV60WorkbenchGateCheck(check, out status, out evidence, out runtimeEvidenceStatus, out runtimeEvidence);
                break;
            }

            report.Add(
                "v6.0:workbench-gate",
                success ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
                success
                    ? $"scoped workbench v2 v6.0 gate passes for release root {root}: {evidence} (runtimeEvidence=pass; {runtimeEvidenceStatus}; overall workbench exit {run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} ignored by design; scoped v6 gate status={status})."
                    : $"scoped workbench v2 v6.0 gate did not pass for release root {root}: status={status ?? "missing"} runtimeEvidence={runtimeEvidenceStatus} evidence={evidence ?? "n/a"} exit={run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}.",
                run.Source,
                runtimeEvidence);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            report.Add(
                "v6.0:workbench-gate",
                ReleaseVerifyStatus.Error,
                $"Could not evaluate workbench v2 v6.0 gate for this release root: {ex.Message}",
                run.Source);
        }
    }

    private static WorkbenchVerifyRun RunWorkbenchVerify(string root)
    {
        var source = WorkbenchVerifySource(root);
        try
        {
            var output = new StringWriter();
            var exitCode = WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                root,
                "workbench-contract.v2").GetAwaiter().GetResult();
            return new WorkbenchVerifyRun(source, exitCode, output.ToString(), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            return new WorkbenchVerifyRun(source, -1, "", ex);
        }
    }

    private sealed record WorkbenchVerifyRun(string Source, int ExitCode, string Output, Exception? Error);

    internal static string WorkbenchVerifySource(string root) =>
        $"workbench verify --contract workbench-contract.v2 --dir {QuoteDisplayArgument(root)} --output json";

    private static string QuoteDisplayArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    internal static bool EvaluateV60WorkbenchGateCheck(
        JsonElement check,
        out string? checkStatus,
        out string? evidence,
        out string runtimeEvidenceStatus)
    {
        return EvaluateV60WorkbenchGateCheck(
            check,
            out checkStatus,
            out evidence,
            out runtimeEvidenceStatus,
            out _);
    }

    internal static bool EvaluateV60WorkbenchGateCheck(
        JsonElement check,
        out string? checkStatus,
        out string? evidence,
        out string runtimeEvidenceStatus,
        out IReadOnlyDictionary<string, object?>? runtimeEvidence)
    {
        checkStatus = check.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        evidence = check.TryGetProperty("evidence", out var evidenceElement)
            ? evidenceElement.GetString()
            : null;
        var runtimeEvidenceOk = TryReadV60RuntimeEvidence(check, out runtimeEvidenceStatus, out runtimeEvidence);
        return string.Equals(checkStatus, "pass", StringComparison.OrdinalIgnoreCase) && runtimeEvidenceOk;
    }

    private static bool TryReadV60RuntimeEvidence(
        JsonElement check,
        out string status,
        out IReadOnlyDictionary<string, object?>? runtimeEvidence)
    {
        runtimeEvidence = null;
        if (!check.TryGetProperty("runtimeEvidence", out var evidence) ||
            evidence.ValueKind != JsonValueKind.Object)
        {
            status = "missing";
            return false;
        }

        var commandSpine = ReadBoolean(evidence, "commandSpine");
        var commandSpineOutputParity = ReadBoolean(evidence, "commandSpineOutputParity");
        var commandSpineNoWrites = ReadBoolean(evidence, "commandSpineNoWrites");
        var workflowRegistry = ReadBoolean(evidence, "workflowRegistry");
        var ledgerAppend = ReadBoolean(evidence, "ledgerAppend");
        var ledgerQueryValidate = ReadBoolean(evidence, "ledgerQueryValidate");
        var ledgerStats = ReadBoolean(evidence, "ledgerStats");
        var ledgerTimeline = ReadBoolean(evidence, "ledgerTimeline");
        var ledgerAnalytics = ReadBoolean(evidence, "ledgerAnalytics");
        var ledgerReplay = ReadBoolean(evidence, "ledgerReplay");
        var standardsValidate = ReadBoolean(evidence, "standardsValidate");
        var issuePreflight = ReadBoolean(evidence, "issuePreflight");
        var issuePackageDryRun = ReadBoolean(evidence, "issuePackageDryRun");
        var deliverablesVerify = ReadBoolean(evidence, "deliverablesVerify");
        var journalVerify = ReadBoolean(evidence, "journalVerify");
        var historyList = ReadBoolean(evidence, "historyList");
        var historyListCountConsistency = ReadBoolean(evidence, "historyListCountConsistency");
        var historyListRowOrder = ReadBoolean(evidence, "historyListRowOrder");
        var historyListEvidence = ReadHistoryListEvidence(evidence, out var historyListEvidencePayload);
        var rollbackDryRun = ReadBoolean(evidence, "rollbackDryRun");
        var rollbackDryRunPreview = ReadBoolean(evidence, "rollbackDryRunPreview");
        var rollbackNoMutatingSetRequest = ReadBoolean(evidence, "rollbackNoMutatingSetRequest");
        var rollbackDryRunEvidence = ReadRollbackDryRunEvidence(evidence, out var rollbackDryRunEvidencePayload);
        var allRuntimeChecksPass = ReadBoolean(evidence, "allRuntimeChecksPass");
        var presentRuntimeEvidence = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "allRuntimeChecksPass", allRuntimeChecksPass);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "commandSpine", commandSpine);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "commandSpineNoWrites", commandSpineNoWrites);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "commandSpineOutputParity", commandSpineOutputParity);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "deliverablesVerify", deliverablesVerify);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "historyList", historyList);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "historyListCountConsistency", historyListCountConsistency);
        AddRuntimeEvidenceObject(presentRuntimeEvidence, "historyListEvidence", historyListEvidencePayload);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "historyListRowOrder", historyListRowOrder);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "issuePackageDryRun", issuePackageDryRun);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "issuePreflight", issuePreflight);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "journalVerify", journalVerify);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "ledgerAppend", ledgerAppend);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "ledgerAnalytics", ledgerAnalytics);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "ledgerQueryValidate", ledgerQueryValidate);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "ledgerReplay", ledgerReplay);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "ledgerStats", ledgerStats);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "ledgerTimeline", ledgerTimeline);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "rollbackDryRun", rollbackDryRun);
        AddRuntimeEvidenceObject(presentRuntimeEvidence, "rollbackDryRunEvidence", rollbackDryRunEvidencePayload);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "rollbackDryRunPreview", rollbackDryRunPreview);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "rollbackNoMutatingSetRequest", rollbackNoMutatingSetRequest);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "standardsValidate", standardsValidate);
        AddRuntimeEvidenceFlag(presentRuntimeEvidence, "workflowRegistry", workflowRegistry);
        runtimeEvidence = presentRuntimeEvidence;
        var ok =
            commandSpine == true &&
            commandSpineOutputParity == true &&
            commandSpineNoWrites == true &&
            standardsValidate == true &&
            issuePreflight == true &&
            issuePackageDryRun == true &&
            deliverablesVerify == true &&
            journalVerify == true &&
            historyList == true &&
            historyListCountConsistency == true &&
            historyListRowOrder == true &&
            historyListEvidence == true &&
            rollbackDryRun == true &&
            rollbackDryRunPreview == true &&
            rollbackNoMutatingSetRequest == true &&
            rollbackDryRunEvidence == true &&
            workflowRegistry == true &&
            ledgerAppend == true &&
            ledgerQueryValidate == true &&
            ledgerReplay == true &&
            ledgerStats == true &&
            ledgerTimeline == true &&
            ledgerAnalytics == true &&
            allRuntimeChecksPass == true;
        status =
            $"commandSpine={FormatRuntimeFlag(commandSpine)}, commandSpineOutputParity={FormatRuntimeFlag(commandSpineOutputParity)}, commandSpineNoWrites={FormatRuntimeFlag(commandSpineNoWrites)}, standardsValidate={FormatRuntimeFlag(standardsValidate)}, issuePreflight={FormatRuntimeFlag(issuePreflight)}, issuePackageDryRun={FormatRuntimeFlag(issuePackageDryRun)}, deliverablesVerify={FormatRuntimeFlag(deliverablesVerify)}, journalVerify={FormatRuntimeFlag(journalVerify)}, historyList={FormatRuntimeFlag(historyList)}, historyListCountConsistency={FormatRuntimeFlag(historyListCountConsistency)}, historyListRowOrder={FormatRuntimeFlag(historyListRowOrder)}, historyListEvidence={FormatRuntimeFlag(historyListEvidence)}, rollbackDryRun={FormatRuntimeFlag(rollbackDryRun)}, rollbackDryRunPreview={FormatRuntimeFlag(rollbackDryRunPreview)}, rollbackNoMutatingSetRequest={FormatRuntimeFlag(rollbackNoMutatingSetRequest)}, rollbackDryRunEvidence={FormatRuntimeFlag(rollbackDryRunEvidence)}, workflowRegistry={FormatRuntimeFlag(workflowRegistry)}, ledgerAppend={FormatRuntimeFlag(ledgerAppend)}, ledgerAnalytics={FormatRuntimeFlag(ledgerAnalytics)}, ledgerQueryValidate={FormatRuntimeFlag(ledgerQueryValidate)}, ledgerReplay={FormatRuntimeFlag(ledgerReplay)}, ledgerStats={FormatRuntimeFlag(ledgerStats)}, ledgerTimeline={FormatRuntimeFlag(ledgerTimeline)}, allRuntimeChecksPass={FormatRuntimeFlag(allRuntimeChecksPass)}";
        return ok;

        static void AddRuntimeEvidenceFlag(IDictionary<string, object?> values, string propertyName, bool? value)
        {
            if (value.HasValue)
                values[propertyName] = value.Value;
        }

        static void AddRuntimeEvidenceObject(IDictionary<string, object?> values, string propertyName, IReadOnlyDictionary<string, object?>? value)
        {
            if (value is not null)
                values[propertyName] = value;
        }

        static bool? ReadBoolean(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True
                ? true
                : element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.False
                    ? false
                    : null;
        }

        static bool? ReadHistoryListEvidence(
            JsonElement element,
            out IReadOnlyDictionary<string, object?>? payload)
        {
            payload = null;
            if (!element.TryGetProperty("historyListEvidence", out var property) ||
                property.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var jsonEntryCount = ReadInt(property, "jsonEntryCount");
            var jsonHiddenCount = ReadInt(property, "jsonHiddenCount");
            var jsonReturnedCount = ReadInt(property, "jsonReturnedCount");
            var tableRowCount = ReadInt(property, "tableRowCount");
            var countConsistency = ReadBoolean(property, "countConsistency");
            var idOrderMatch = ReadBoolean(property, "idOrderMatch");
            var headerMatched = ReadBoolean(property, "headerMatched");
            payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["countConsistency"] = countConsistency,
                ["headerMatched"] = headerMatched,
                ["idOrderMatch"] = idOrderMatch,
                ["jsonEntryCount"] = jsonEntryCount,
                ["jsonHiddenCount"] = jsonHiddenCount,
                ["jsonReturnedCount"] = jsonReturnedCount,
                ["tableRowCount"] = tableRowCount,
            };
            if (!jsonEntryCount.HasValue ||
                !jsonHiddenCount.HasValue ||
                !jsonReturnedCount.HasValue ||
                !tableRowCount.HasValue ||
                !countConsistency.HasValue ||
                !idOrderMatch.HasValue ||
                !headerMatched.HasValue)
            {
                return null;
            }

            return jsonEntryCount.Value >= 0 &&
                   jsonHiddenCount.Value >= 0 &&
                   jsonReturnedCount.Value >= 0 &&
                   tableRowCount.Value >= 0 &&
                   jsonEntryCount.Value == jsonHiddenCount.Value + jsonReturnedCount.Value &&
                   tableRowCount.Value == jsonReturnedCount.Value &&
                   countConsistency == true &&
                   idOrderMatch == true &&
                   headerMatched == true;
        }

        static bool? ReadRollbackDryRunEvidence(
            JsonElement element,
            out IReadOnlyDictionary<string, object?>? payload)
        {
            payload = null;
            if (!element.TryGetProperty("rollbackDryRunEvidence", out var property) ||
                property.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var actionCount = ReadInt(property, "actionCount");
            var conflictCount = ReadInt(property, "conflictCount");
            var errorCount = ReadInt(property, "errorCount");
            var safeApplyCommand = ReadString(property, "safeApplyCommand");
            var safeApplyEmitted = ReadBoolean(property, "safeApplyEmitted");
            var dryRunPreviewOnly = ReadBoolean(property, "dryRunPreviewOnly");
            var sawDryRunSetPreview = ReadBoolean(property, "sawDryRunSetPreview");
            var sawMutatingSetRequest = ReadBoolean(property, "sawMutatingSetRequest");
            payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["actionCount"] = actionCount,
                ["conflictCount"] = conflictCount,
                ["dryRunPreviewOnly"] = dryRunPreviewOnly,
                ["errorCount"] = errorCount,
                ["safeApplyCommand"] = safeApplyCommand,
                ["safeApplyEmitted"] = safeApplyEmitted,
                ["sawDryRunSetPreview"] = sawDryRunSetPreview,
                ["sawMutatingSetRequest"] = sawMutatingSetRequest,
            };
            if (!actionCount.HasValue ||
                !conflictCount.HasValue ||
                !errorCount.HasValue ||
                !safeApplyEmitted.HasValue ||
                !dryRunPreviewOnly.HasValue ||
                !sawDryRunSetPreview.HasValue ||
                !sawMutatingSetRequest.HasValue)
            {
                return null;
            }

            return actionCount.Value >= 1 &&
                   conflictCount.Value == 0 &&
                   errorCount.Value == 0 &&
                   IsSafeRollbackApplyCommand(safeApplyCommand) &&
                   safeApplyEmitted == true &&
                   dryRunPreviewOnly == true &&
                   sawDryRunSetPreview == true &&
                   sawMutatingSetRequest == false;
        }

        static bool IsSafeRollbackApplyCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var normalized = command.Trim();
            return normalized.StartsWith("revitcli rollback ", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                   (normalized.Contains(" --approve", StringComparison.OrdinalIgnoreCase) ||
                    normalized.EndsWith(" --approve", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(" --yes", StringComparison.OrdinalIgnoreCase) ||
                    normalized.EndsWith(" --yes", StringComparison.OrdinalIgnoreCase));
        }

        static int? ReadInt(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.Number &&
                   property.TryGetInt32(out var value)
                ? value
                : null;
        }

        static string? ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        static string FormatRuntimeFlag(bool? value) =>
            value.HasValue ? value.Value.ToString().ToLowerInvariant() : "missing";
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

    private static void AddSemanticContains(
        ReleaseVerifyReport report,
        string id,
        string text,
        IReadOnlyList<string> terms,
        string okMessage,
        string path)
    {
        var missing = terms
            .Where(term => !text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        report.Add(
            id,
            missing.Length == 0 ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            missing.Length == 0
                ? okMessage
                : $"Expected {path} to contain semantic evidence terms: {string.Join(", ", missing)}.",
            path);
    }

    private static void AddGuardedContains(
        ReleaseVerifyReport report,
        string id,
        string text,
        string needle,
        string okMessage,
        string path,
        params string[][] contradictionGroups)
    {
        var contains = text.Contains(needle, StringComparison.OrdinalIgnoreCase);
        var evidence = contains ? FindBoundaryEvidenceLine(text, needle) : null;
        var contradiction = evidence is not null
            ? contradictionGroups
                .SelectMany(group => group)
                .FirstOrDefault(phrase => evidence.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            : null;

        report.Add(
            id,
            contains && evidence is not null && contradiction is null ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            contains
                ? evidence is null
                    ? $"Expected {path} to contain boundary wording for '{needle}', not only a bare mention."
                    : contradiction is null
                        ? okMessage
                        : $"Expected {path} to keep '{needle}' uncontradicted, but found contradictory wording '{contradiction}'."
                : $"Expected {path} to contain '{needle}'.",
            path);
    }

    private static string? FindBoundaryEvidenceLine(string text, string needle)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                !IsIgnoredBoundaryExampleLine(line) &&
                IsBoundaryEvidenceLine(line, needle))
            {
                return line;
            }
        }

        return null;
    }

    private static bool IsBoundaryEvidenceLine(string line, string needle)
    {
        var normalizedLine = line.Trim().ToLowerInvariant();
        var normalizedNeedle = needle.Trim().ToLowerInvariant();
        return normalizedNeedle.Contains("read-only", StringComparison.Ordinal) ||
               normalizedNeedle.Contains("no-write", StringComparison.Ordinal) ||
               normalizedNeedle.Contains("does not", StringComparison.Ordinal) ||
               normalizedNeedle.Contains("without", StringComparison.Ordinal) ||
               normalizedNeedle.StartsWith("no ", StringComparison.Ordinal) ||
               normalizedLine.Contains("no " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("not " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("without " + normalizedNeedle, StringComparison.Ordinal) ||
               IsDoesNotActionBoundary(normalizedLine, normalizedNeedle) ||
               normalizedLine.Contains("avoids " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("excludes " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("does not introduce " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("do not introduce " + normalizedNeedle, StringComparison.Ordinal) ||
               BoundaryPhraseAppearsBeforeNeedle(normalizedLine, normalizedNeedle, "does not introduce") ||
               BoundaryPhraseAppearsBeforeNeedle(normalizedLine, normalizedNeedle, "do not introduce") ||
               KeepsNeedleOut(normalizedLine, normalizedNeedle);
    }

    private static bool IsIgnoredBoundaryExampleLine(string line)
    {
        var normalizedLine = line.Trim().ToLowerInvariant();
        return normalizedLine.StartsWith("rejected example", StringComparison.Ordinal) ||
               normalizedLine.StartsWith("reviewer note", StringComparison.Ordinal) ||
               normalizedLine.Contains("rejected example wording", StringComparison.Ordinal);
    }

    private static bool IsDoesNotActionBoundary(string normalizedLine, string normalizedNeedle)
    {
        if (!normalizedNeedle.Contains("revit", StringComparison.Ordinal) &&
            !normalizedNeedle.Contains("write", StringComparison.Ordinal))
        {
            return false;
        }

        var doesNotIndex = normalizedLine.IndexOf("does not", StringComparison.Ordinal);
        var needleIndex = normalizedLine.IndexOf(normalizedNeedle, StringComparison.Ordinal);
        return doesNotIndex >= 0 &&
               needleIndex > doesNotIndex;
    }

    private static bool KeepsNeedleOut(string normalizedLine, string normalizedNeedle)
    {
        var keepsIndex = normalizedLine.IndexOf("keeps", StringComparison.Ordinal);
        var needleIndex = normalizedLine.IndexOf(normalizedNeedle, StringComparison.Ordinal);
        var outIndex = normalizedLine.IndexOf("out", StringComparison.Ordinal);
        return keepsIndex >= 0 &&
               needleIndex > keepsIndex &&
               outIndex > needleIndex;
    }

    private static bool BoundaryPhraseAppearsBeforeNeedle(string normalizedLine, string normalizedNeedle, string boundaryPhrase)
    {
        var boundaryIndex = normalizedLine.IndexOf(boundaryPhrase, StringComparison.Ordinal);
        var needleIndex = normalizedLine.IndexOf(normalizedNeedle, StringComparison.Ordinal);
        return boundaryIndex >= 0 &&
               needleIndex > boundaryIndex;
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

    private static bool TryReadJsonRoot(string path, ReleaseVerifyReport report, string id, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            root = document.RootElement.Clone();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            root = default;
            report.Add(id, ReleaseVerifyStatus.Error,
                $"Cannot read valid JSON from {Path.GetFileName(path)}: {ex.Message}", path);
            return false;
        }
    }

    private static void AddJsonCheck(
        ReleaseVerifyReport report,
        string id,
        bool success,
        string okMessage,
        string errorMessage,
        string path)
    {
        report.Add(id, success ? ReleaseVerifyStatus.Ok : ReleaseVerifyStatus.Error,
            success ? okMessage : errorMessage, path);
    }

    private static bool TryGetJsonProperty(JsonElement element, string path, out JsonElement property)
    {
        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                property = default;
                return false;
            }
        }

        property = current;
        return true;
    }

    private static string? JsonString(JsonElement element, string path) =>
        TryGetJsonProperty(element, path, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryReadNonEmptyJsonString(JsonElement element, string path, out string value)
    {
        value = JsonString(element, path) ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool JsonStringEquals(JsonElement element, string path, string? expected) =>
        expected is not null &&
        string.Equals(JsonString(element, path), expected, StringComparison.Ordinal);

    private static bool JsonStringNonEmpty(JsonElement element, string path) =>
        !string.IsNullOrWhiteSpace(JsonString(element, path));

    private static bool JsonBoolEquals(JsonElement element, string path, bool expected) =>
        TryGetJsonProperty(element, path, out var property) &&
        (expected ? property.ValueKind == JsonValueKind.True : property.ValueKind == JsonValueKind.False);

    private static bool? JsonBool(JsonElement element, string path) =>
        TryGetJsonProperty(element, path, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static int? JsonInt(JsonElement element, string path) =>
        TryGetJsonProperty(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var actual)
            ? actual
            : null;

    private static bool JsonIntEquals(JsonElement element, string path, int expected) =>
        TryGetJsonProperty(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var actual) &&
        actual == expected;

    private static bool JsonIntAtLeast(JsonElement element, string path, int minimum) =>
        TryGetJsonProperty(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var actual) &&
        actual >= minimum;

    private static bool JsonArrayLengthEquals(JsonElement element, string path, int expected) =>
        TryGetJsonProperty(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Array &&
        property.GetArrayLength() == expected;

    private static bool JsonStringArrayContains(JsonElement element, string path, string expected) =>
        TryGetJsonProperty(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Array &&
        property.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            string.Equals(item.GetString(), expected, StringComparison.Ordinal));

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

    public void Add(
        string id,
        ReleaseVerifyStatus status,
        string message,
        string? path,
        IReadOnlyDictionary<string, object?>? runtimeEvidence = null)
    {
        Checks.Add(new ReleaseVerifyCheck
        {
            Id = id,
            Status = status,
            Message = message,
            Path = path,
            RuntimeEvidence = runtimeEvidence,
        });
    }
}

internal sealed class ReleaseVerifyCheck
{
    public string Id { get; init; } = "";
    public ReleaseVerifyStatus Status { get; init; }
    public string Message { get; init; } = "";
    public string? Path { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? RuntimeEvidence { get; init; }
}

internal enum ReleaseVerifyStatus
{
    Ok,
    Warning,
    Error,
}
