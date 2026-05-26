using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Output;
using RevitCli.Release;

namespace RevitCli.Commands;

public static class ReleaseCommand
{
    private const string PilotScaffoldSchemaVersion = "release-pilot-scaffold.v1";
    private const string PilotValidateSchemaVersion = "release-pilot-validate.v1";
    private const string PilotRegisterSchemaVersion = "release-pilot-register.v1";
    private const string OfficeRolloutStatusSchemaVersion = "v6-office-rollout-status.v1";
    private const string OfficeRolloutStatusPath = "docs/smoke/v6.0/office-rollout-status.json";
    private const string PilotScaffoldRolloutStatusHint =
        "Do not add this pilot to office-rollout-status.json until every required command, live-operation, review, signoff, support-review, and postmortem item is complete.";
    private static readonly string[] RequiredPilotEvidencePacketPhrases =
    {
        "## Required Commands",
        "doctor --check-version 2026 --output json",
        "status --output json",
        "workbench verify --contract workbench-contract.v2",
        "release verify --strict --output json",
        "ledger query --source ledger --output json",
        "ledger validate --source ledger --output json",
        "ledger stats --source ledger --analytics-snapshot",
        "ledger timeline --source ledger --analytics-snapshot",
        "journal verify --output json",
        "## Live Operation Evidence",
        "Rollback result",
        "## User Review",
        "BIM manager signoff",
        "Project-copy owner signoff",
        "Support ticket review",
        "Multi-user rollout postmortem",
        "Boundary summary",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Command Create()
    {
        var command = new Command("release", "Release preparation helpers");
        command.AddCommand(CreateVerifyCommand());
        command.AddCommand(CreatePilotCommand());
        return command;
    }

    private static Command CreatePilotCommand()
    {
        var command = new Command("pilot", "Office rollout pilot evidence helpers");
        command.AddCommand(CreatePilotScaffoldCommand());
        command.AddCommand(CreatePilotValidateCommand());
        command.AddCommand(CreatePilotRegisterCommand());
        return command;
    }

    private static Command CreatePilotScaffoldCommand()
    {
        var rootOpt = new Option<string>(
            "--root",
            () => Directory.GetCurrentDirectory(),
            "Repository root");
        var pilotIdOpt = new Option<string>(
            "--pilot-id",
            "Public-safe pilot identifier, for example v6-pilot-2026-office-copy-01")
        {
            IsRequired = true,
        };
        var pathOpt = new Option<string?>(
            "--path",
            "Repo-relative evidence packet path under docs/smoke/v6.0/");
        var forceOpt = new Option<bool>(
            "--force",
            "Overwrite an existing evidence packet");
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, markdown");

        var command = new Command("scaffold", "Create a public-safe v6.0 office pilot evidence packet")
        {
            rootOpt, pilotIdOpt, pathOpt, forceOpt, outputOpt
        };
        command.SetHandler(async (root, pilotId, path, force, output) =>
        {
            Environment.ExitCode = await ExecutePilotScaffoldAsync(root, pilotId, path, force, output, Console.Out);
        }, rootOpt, pilotIdOpt, pathOpt, forceOpt, outputOpt);

        return command;
    }

    private static Command CreatePilotValidateCommand()
    {
        var rootOpt = new Option<string>(
            "--root",
            () => Directory.GetCurrentDirectory(),
            "Repository root");
        var pathOpt = new Option<string>(
            "--path",
            "Repo-relative evidence packet path under docs/smoke/v6.0/")
        {
            IsRequired = true,
        };
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, markdown");

        var command = new Command("validate", "Validate a public-safe v6.0 office pilot evidence packet")
        {
            rootOpt, pathOpt, outputOpt
        };
        command.SetHandler(async (root, path, output) =>
        {
            Environment.ExitCode = await ExecutePilotValidateAsync(root, path, output, Console.Out);
        }, rootOpt, pathOpt, outputOpt);

        return command;
    }

    private static Command CreatePilotRegisterCommand()
    {
        var rootOpt = new Option<string>(
            "--root",
            () => Directory.GetCurrentDirectory(),
            "Repository root");
        var pilotIdOpt = new Option<string>(
            "--pilot-id",
            "Public-safe pilot identifier, for example v6-pilot-2026-office-copy-01")
        {
            IsRequired = true,
        };
        var pathOpt = new Option<string>(
            "--path",
            "Repo-relative evidence packet path under docs/smoke/v6.0/")
        {
            IsRequired = true,
        };
        var yesOpt = new Option<bool>(
            "--yes",
            "Write the completed pilot entry to office-rollout-status.json");
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, markdown");

        var command = new Command("register", "Register a validated v6.0 office pilot evidence packet in rollout status")
        {
            rootOpt, pilotIdOpt, pathOpt, yesOpt, outputOpt
        };
        command.SetHandler(async (root, pilotId, path, yes, output) =>
        {
            Environment.ExitCode = await ExecutePilotRegisterAsync(root, pilotId, path, yes, output, Console.Out);
        }, rootOpt, pilotIdOpt, pathOpt, yesOpt, outputOpt);

        return command;
    }

    private static Command CreateVerifyCommand()
    {
        var rootOpt = new Option<string>(
            "--root",
            () => Directory.GetCurrentDirectory(),
            "Repository root to verify");
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, markdown");
        var tagOpt = new Option<string?>(
            "--tag",
            "Release tag to compare with RevitCliVersion (for example v2.3.0)");
        var strictOpt = new Option<bool>(
            "--strict",
            "Treat warnings as release-blocking failures");

        var command = new Command("verify", "Verify local release-readiness files and CI guardrails")
        {
            rootOpt, outputOpt, tagOpt, strictOpt
        };

        command.SetHandler(async (root, output, tag, strict) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(root, output, tag, strict, Console.Out);
        }, rootOpt, outputOpt, tagOpt, strictOpt);

        return command;
    }

    public static async Task<int> ExecuteVerifyAsync(
        string root,
        string outputFormat,
        string? tag,
        bool strict,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        var report = ReleaseVerifier.Verify(new ReleaseVerifyOptions
        {
            Root = root,
            Tag = tag,
            Strict = strict,
        });

        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptions));
        }
        else if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderMarkdown(report));
        }
        else
        {
            await output.WriteLineAsync(RenderTable(report));
        }

        return report.Success ? 0 : 1;
    }

    public static async Task<int> ExecutePilotScaffoldAsync(
        string root,
        string pilotId,
        string? evidencePacketPath,
        bool force,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        var normalizedRoot = Path.GetFullPath(root);
        var path = string.IsNullOrWhiteSpace(evidencePacketPath)
            ? $"docs/smoke/v6.0/{pilotId}.md"
            : evidencePacketPath.Trim();
        ReleasePilotScaffoldResult result;
        if (!IsPublicSafePilotId(pilotId))
        {
            result = ReleasePilotScaffoldResult.Failed(pilotId, path, "Pilot id must be non-empty and contain only letters, digits, '.', '_', or '-'.");
            await WritePilotScaffoldResultAsync(output, normalizedOutput, result);
            return 1;
        }

        if (!IsPublicSafePilotEvidencePath(path))
        {
            result = ReleasePilotScaffoldResult.Failed(pilotId, path, "Evidence packet path must be repo-relative under docs/smoke/v6.0/ and end with .md.");
            await WritePilotScaffoldResultAsync(output, normalizedOutput, result);
            return 1;
        }

        var fullPath = Path.Combine(normalizedRoot, path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath) && !force)
        {
            result = ReleasePilotScaffoldResult.Failed(pilotId, path, "Evidence packet already exists; use --force to overwrite it.");
            await WritePilotScaffoldResultAsync(output, normalizedOutput, result);
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, RenderPilotEvidencePacket(pilotId));
        result = new ReleasePilotScaffoldResult(
            PilotScaffoldSchemaVersion,
            true,
            pilotId,
            path,
            true,
            force,
            "Created v6.0 office pilot evidence packet scaffold.",
            PilotScaffoldRolloutStatusHint);
        await WritePilotScaffoldResultAsync(output, normalizedOutput, result);
        return 0;
    }

    public static async Task<int> ExecutePilotValidateAsync(
        string root,
        string evidencePacketPath,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        var result = await ValidatePilotEvidencePacketAsync(root, evidencePacketPath);
        await WritePilotValidateResultAsync(output, normalizedOutput, result);
        return result.Success ? 0 : 1;
    }

    public static async Task<int> ExecutePilotRegisterAsync(
        string root,
        string pilotId,
        string evidencePacketPath,
        bool yes,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        var normalizedRoot = Path.GetFullPath(root);
        var path = evidencePacketPath.Trim();
        var issues = new List<ReleasePilotValidateIssue>();
        if (!IsPublicSafePilotId(pilotId))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "pilot-id-safety",
                "error",
                "Pilot id must be non-empty and contain only letters, digits, '.', '_', or '-'.",
                null,
                pilotId));
        }

        var validation = await ValidatePilotEvidencePacketAsync(normalizedRoot, path);
        issues.AddRange(validation.Issues);

        var statusPath = Path.Combine(normalizedRoot, OfficeRolloutStatusPath.Replace('/', Path.DirectorySeparatorChar));
        var status = issues.Count == 0 ? ReadOfficeRolloutStatus(statusPath, issues) : null;
        if (status is not null)
        {
            AddPilotRegisterStatusIssues(status, pilotId, path, issues);
        }

        ReleasePilotRegisterResult result;
        if (issues.Any(issue => issue.Severity == "error") || status is null)
        {
            result = ReleasePilotRegisterResult.From(
                false,
                pilotId,
                path,
                OfficeRolloutStatusPath,
                dryRun: !yes,
                wrote: false,
                minimumOfficePilotCount: status?.MinimumOfficePilotCount ?? 0,
                completedOfficePilotCount: status?.CompletedOfficePilotCount ?? 0,
                officeRolloutCompletion: status?.OfficeRolloutCompletion ?? false,
                productionSupportClaim: status?.ProductionSupportClaim ?? false,
                "Pilot evidence was not registered; fix validation/status issues first.",
                issues);
            await WritePilotRegisterResultAsync(output, normalizedOutput, result);
            return 1;
        }

        var updated = AddCompletedPilot(status, pilotId, path);
        if (yes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!);
            await File.WriteAllTextAsync(statusPath, JsonSerializer.Serialize(updated, JsonOptions) + Environment.NewLine);
        }

        result = ReleasePilotRegisterResult.From(
            true,
            pilotId,
            path,
            OfficeRolloutStatusPath,
            dryRun: !yes,
            wrote: yes,
            updated.MinimumOfficePilotCount,
            updated.CompletedOfficePilotCount,
            updated.OfficeRolloutCompletion,
            updated.ProductionSupportClaim,
            yes
                ? "Registered validated pilot evidence in office-rollout-status.json; production support claim was not auto-enabled."
                : "Dry run only; pass --yes to register this validated pilot evidence.",
            issues);
        await WritePilotRegisterResultAsync(output, normalizedOutput, result);
        return result.Success ? 0 : 1;
    }

    private static async Task<ReleasePilotValidateResult> ValidatePilotEvidencePacketAsync(
        string root,
        string evidencePacketPath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var path = evidencePacketPath.Trim();
        var issues = new List<ReleasePilotValidateIssue>();
        if (!IsPublicSafePilotEvidencePath(path))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "path-safety",
                "error",
                "Evidence packet path must be repo-relative under docs/smoke/v6.0/ and end with .md.",
                null,
                path));
            return ReleasePilotValidateResult.FromIssues(path, issues);
        }

        var fullPath = Path.Combine(normalizedRoot, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "packet-missing",
                "error",
                "Evidence packet file does not exist.",
                null,
                path));
            return ReleasePilotValidateResult.FromIssues(path, issues);
        }

        try
        {
            AddPilotPacketContentIssues(await File.ReadAllTextAsync(fullPath), issues);
        }
        catch (IOException ex)
        {
            issues.Add(new ReleasePilotValidateIssue(
                "packet-unreadable",
                "error",
                $"Evidence packet file could not be read: {ex.Message}",
                null,
                path));
        }
        catch (UnauthorizedAccessException ex)
        {
            issues.Add(new ReleasePilotValidateIssue(
                "packet-unreadable",
                "error",
                $"Evidence packet file could not be read: {ex.Message}",
                null,
                path));
        }

        return ReleasePilotValidateResult.FromIssues(path, issues);
    }

    private static async Task WritePilotScaffoldResultAsync(
        TextWriter output,
        string normalizedOutput,
        ReleasePilotScaffoldResult result)
    {
        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        }
        else if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderPilotScaffoldMarkdown(result));
        }
        else
        {
            await output.WriteLineAsync(RenderPilotScaffoldTable(result));
        }
    }

    private static async Task WritePilotValidateResultAsync(
        TextWriter output,
        string normalizedOutput,
        ReleasePilotValidateResult result)
    {
        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        }
        else if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderPilotValidateMarkdown(result));
        }
        else
        {
            await output.WriteLineAsync(RenderPilotValidateTable(result));
        }
    }

    private static async Task WritePilotRegisterResultAsync(
        TextWriter output,
        string normalizedOutput,
        ReleasePilotRegisterResult result)
    {
        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        }
        else if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderPilotRegisterMarkdown(result));
        }
        else
        {
            await output.WriteLineAsync(RenderPilotRegisterTable(result));
        }
    }

    private static void AddPilotPacketContentIssues(
        string packet,
        List<ReleasePilotValidateIssue> issues)
    {
        foreach (var phrase in RequiredPilotEvidencePacketPhrases)
        {
            if (!packet.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ReleasePilotValidateIssue(
                    "missing-required-evidence",
                    "error",
                    $"Evidence packet is missing required phrase: {phrase}",
                    null,
                    phrase));
            }
        }

        foreach (var (label, lineNumber) in FindBlankPilotFields(packet))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "blank-scaffold-field",
                "error",
                $"Evidence packet still has a blank scaffold field: {label}",
                lineNumber,
                label));
        }

        foreach (var (line, lineNumber) in FindPublicSafetyFindings(packet))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "public-safety-path",
                "error",
                "Evidence packet appears to contain a local absolute path; replace it with a public-safe identifier.",
                lineNumber,
                line.Trim()));
        }
    }

    private static OfficeRolloutStatusDocument? ReadOfficeRolloutStatus(
        string statusPath,
        List<ReleasePilotValidateIssue> issues)
    {
        if (!File.Exists(statusPath))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "rollout-status-missing",
                "error",
                "docs/smoke/v6.0/office-rollout-status.json is missing.",
                null,
                OfficeRolloutStatusPath));
            return null;
        }

        try
        {
            var status = JsonSerializer.Deserialize<OfficeRolloutStatusDocument>(
                File.ReadAllText(statusPath),
                JsonOptions);
            if (status is null)
            {
                issues.Add(new ReleasePilotValidateIssue(
                    "rollout-status-unreadable",
                    "error",
                    "office-rollout-status.json did not deserialize to a status object.",
                    null,
                    OfficeRolloutStatusPath));
                return null;
            }

            NormalizeOfficeRolloutStatus(status);
            return status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add(new ReleasePilotValidateIssue(
                "rollout-status-unreadable",
                "error",
                $"office-rollout-status.json could not be read: {ex.Message}",
                null,
                OfficeRolloutStatusPath));
            return null;
        }
    }

    private static void AddPilotRegisterStatusIssues(
        OfficeRolloutStatusDocument status,
        string pilotId,
        string evidencePacketPath,
        List<ReleasePilotValidateIssue> issues)
    {
        if (!string.Equals(status.SchemaVersion, OfficeRolloutStatusSchemaVersion, StringComparison.Ordinal))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "rollout-status-schema",
                "error",
                $"office-rollout-status.json must use schema {OfficeRolloutStatusSchemaVersion}.",
                null,
                status.SchemaVersion));
        }

        if (status.MinimumOfficePilotCount < 2 ||
            status.CompletedOfficePilotCount != status.CompletedPilots.Length ||
            status.CompletedPilotIds.Length != status.CompletedPilots.Length)
        {
            issues.Add(new ReleasePilotValidateIssue(
                "rollout-status-counts",
                "error",
                "office-rollout-status.json must have minimumOfficePilotCount>=2 and matching completed pilot counts.",
                null,
                OfficeRolloutStatusPath));
        }

        if (!RequiredEvidenceComplete(status.RequiredEvidence))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "rollout-status-required-evidence",
                "error",
                "office-rollout-status.json requiredEvidence must require every command, review, signoff, support-review, and postmortem field.",
                null,
                OfficeRolloutStatusPath));
        }

        if (status.CompletedPilotIds.Contains(pilotId, StringComparer.Ordinal) ||
            status.CompletedPilots.Any(pilot => string.Equals(pilot.PilotId, pilotId, StringComparison.Ordinal)))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "pilot-duplicate",
                "error",
                "Pilot id is already registered in office-rollout-status.json.",
                null,
                pilotId));
        }

        if (status.CompletedPilots.Any(pilot => string.Equals(pilot.EvidencePacketPath, evidencePacketPath, StringComparison.Ordinal)))
        {
            issues.Add(new ReleasePilotValidateIssue(
                "pilot-path-duplicate",
                "error",
                "Evidence packet path is already registered in office-rollout-status.json.",
                null,
                evidencePacketPath));
        }
    }

    private static OfficeRolloutStatusDocument AddCompletedPilot(
        OfficeRolloutStatusDocument status,
        string pilotId,
        string evidencePacketPath)
    {
        var completedPilots = status.CompletedPilots
            .Append(CompletedOfficePilotStatus.For(pilotId, evidencePacketPath))
            .ToArray();
        status.CompletedPilots = completedPilots;
        status.CompletedPilotIds = status.CompletedPilotIds.Append(pilotId).ToArray();
        status.CompletedOfficePilotCount = completedPilots.Length;
        if (status.CompletedOfficePilotCount < status.MinimumOfficePilotCount)
        {
            status.OfficeRolloutCompletion = false;
            status.ProductionSupportClaim = false;
        }

        return status;
    }

    private static void NormalizeOfficeRolloutStatus(OfficeRolloutStatusDocument status)
    {
        status.SchemaVersion ??= "";
        status.CompletedPilotIds ??= Array.Empty<string>();
        status.CompletedPilots ??= Array.Empty<CompletedOfficePilotStatus>();
        status.RequiredEvidence ??= new OfficeRolloutRequiredEvidence();
        foreach (var pilot in status.CompletedPilots)
        {
            pilot.PilotId ??= "";
            pilot.EvidencePacketPath ??= "";
        }
    }

    private static bool RequiredEvidenceComplete(OfficeRolloutRequiredEvidence evidence) =>
        evidence.Doctor &&
        evidence.Status &&
        evidence.Workbench &&
        evidence.Release &&
        evidence.LedgerQuery &&
        evidence.LedgerValidate &&
        evidence.LedgerStatsAnalyticsSnapshot &&
        evidence.LedgerTimelineAnalyticsSnapshot &&
        evidence.JournalVerify &&
        evidence.RollbackResult &&
        evidence.UserReview &&
        evidence.BimManagerSignoff &&
        evidence.ProjectCopyOwnerSignoff &&
        evidence.SupportTicketReview &&
        evidence.MultiUserRolloutPostmortem;

    private static bool IsPublicSafePilotId(string pilotId) =>
        !string.IsNullOrWhiteSpace(pilotId) &&
        pilotId.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');

    private static bool IsPublicSafePilotEvidencePath(string path)
    {
        var trimmed = path.Trim();
        return !trimmed.Contains('\\', StringComparison.Ordinal) &&
            !trimmed.Contains(':', StringComparison.Ordinal) &&
            !trimmed.StartsWith("/", StringComparison.Ordinal) &&
            !trimmed.Contains("../", StringComparison.Ordinal) &&
            !trimmed.Contains("/..", StringComparison.Ordinal) &&
            trimmed.StartsWith("docs/smoke/v6.0/", StringComparison.Ordinal) &&
            trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Label, int LineNumber)> FindBlankPilotFields(string packet)
    {
        var lines = packet.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
                continue;

            var label = trimmed[2..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            if (label.Length > 0 && value.Length == 0)
                yield return (label, i + 1);
        }
    }

    private static IEnumerable<(string Line, int LineNumber)> FindPublicSafetyFindings(string packet)
    {
        var lines = packet.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (ContainsLocalAbsolutePath(line))
                yield return (line, i + 1);
        }
    }

    private static bool ContainsLocalAbsolutePath(string line) =>
        ContainsWindowsDrivePath(line) ||
        line.Contains(@"\\", StringComparison.Ordinal) ||
        line.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("/mnt/", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWindowsDrivePath(string line)
    {
        for (var i = 0; i + 2 < line.Length; i++)
        {
            var previousIsWord = i > 0 && char.IsLetterOrDigit(line[i - 1]);
            if (char.IsLetter(line[i]) &&
                !previousIsWord &&
                line[i + 1] == ':' &&
                (line[i + 2] == '\\' || line[i + 2] == '/'))
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderPilotEvidencePacket(string pilotId) => $$"""
        # RevitCli v6.0 Office Pilot {{pilotId}}

        Use this packet only for a controlled project-copy pilot. Keep raw
        machine names, model paths, receipt paths, package paths, client names,
        and project names in private notes.

        ## Scope

        - Pilot identifier: {{pilotId}}
        - Date/time:
        - Commit:
        - CLI version:
        - Installed add-in version:
        - Live add-in version:
        - Revit year/build:
        - Machine class:
        - Public model identifier:
        - Project class:
        - Model type: project copy / linked-workshared copy
        - Profile identifier:
        - Operator role:

        ## Required Commands

        Attach public-safe paths or identifiers for:

        - `doctor --check-version 2026 --output json`
        - `status --output json`
        - `workbench verify --contract workbench-contract.v2 --dir . --output json`
        - `release verify --strict --output json`
        - `ledger query --source ledger --output json`
        - `ledger validate --source ledger --output json`
        - `ledger stats --source ledger --analytics-snapshot .revitcli/analytics/ledger-stats.json --output json`
        - `ledger timeline --source ledger --analytics-snapshot .revitcli/analytics/ledger-timeline.json --output json`
        - `journal verify --output json`

        ## Live Operation Evidence

        For each approved live operation, record:

        - Command:
        - Dry-run artifact identifier:
        - Apply artifact identifier:
        - Receipt identifier:
        - Ledger operation identifier:
        - Replay command, when applicable:
        - Rollback result:
        - Final verification command:
        - Failures:
        - Safe retry status:
        - Remediation attempted:

        ## User Review

        - Time saved:
        - False positives:
        - Confusing output:
        - Missing evidence:
        - Trust blockers:
        - Go-forward decision:
        - Follow-up owner:
        - BIM manager signoff:
        - Project-copy owner signoff:
        - Support ticket review:
        - Multi-user rollout postmortem:

        ## Rollout Status Entry

        After private review, add this pilot to
        `docs/smoke/v6.0/office-rollout-status.json` only when every required
        evidence item above is complete.

        Boundary summary: no SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, no database runtime, no central production model mutation, and no production support claim without completed office rollout pilots.
        """;

    private static string RenderPilotScaffoldTable(ReleasePilotScaffoldResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("Release pilot scaffold");
        writer.WriteLine($"Result:  {(result.Success ? "PASS" : "FAIL")}");
        writer.WriteLine($"Pilot:   {result.PilotId}");
        writer.WriteLine($"Path:    {result.EvidencePacketPath}");
        writer.WriteLine($"Wrote:   {result.Wrote.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Force:   {result.Force.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Message: {result.Message}");
        writer.WriteLine($"Rollout: {result.RolloutStatusHint}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderPilotScaffoldMarkdown(ReleasePilotScaffoldResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Release Pilot Scaffold");
        writer.WriteLine();
        writer.WriteLine($"- Status: `{(result.Success ? "PASS" : "FAIL")}`");
        writer.WriteLine($"- Pilot: `{EscapeInlineCode(result.PilotId)}`");
        writer.WriteLine($"- Evidence packet: `{EscapeInlineCode(result.EvidencePacketPath)}`");
        writer.WriteLine($"- Wrote: `{result.Wrote.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Force: `{result.Force.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Message: {result.Message}");
        writer.WriteLine($"- Rollout status: {result.RolloutStatusHint}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderPilotValidateTable(ReleasePilotValidateResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("Release pilot validate");
        writer.WriteLine($"Result:   {(result.Success ? "PASS" : "FAIL")}");
        writer.WriteLine($"Path:     {result.EvidencePacketPath}");
        writer.WriteLine($"Errors:   {result.ErrorCount}");
        writer.WriteLine($"Warnings: {result.WarningCount}");
        if (result.Issues.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"{"Severity",-8} {"Issue",-26} Message");
            writer.WriteLine(new string('-', 90));
            foreach (var issue in result.Issues)
                writer.WriteLine($"{issue.Severity,-8} {Truncate(issue.Id, 26),-26} {issue.Message}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderPilotValidateMarkdown(ReleasePilotValidateResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Release Pilot Validate");
        writer.WriteLine();
        writer.WriteLine($"- Status: `{(result.Success ? "PASS" : "FAIL")}`");
        writer.WriteLine($"- Evidence packet: `{EscapeInlineCode(result.EvidencePacketPath)}`");
        writer.WriteLine($"- Errors: `{result.ErrorCount}`");
        writer.WriteLine($"- Warnings: `{result.WarningCount}`");
        writer.WriteLine();
        writer.WriteLine("## Issues");
        if (result.Issues.Length == 0)
        {
            writer.WriteLine("- None.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine();
        writer.WriteLine("| Severity | Issue | Line | Message |");
        writer.WriteLine("|---|---|---:|---|");
        foreach (var issue in result.Issues)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(issue.Severity)} | {EscapeTableCell(issue.Id)} | {(issue.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")} | {EscapeTableCell(issue.Message)} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderPilotRegisterTable(ReleasePilotRegisterResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("Release pilot register");
        writer.WriteLine($"Result:    {(result.Success ? "PASS" : "FAIL")}");
        writer.WriteLine($"Pilot:     {result.PilotId}");
        writer.WriteLine($"Path:      {result.EvidencePacketPath}");
        writer.WriteLine($"Status:    {result.StatusPath}");
        writer.WriteLine($"Dry run:   {result.DryRun.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Wrote:     {result.Wrote.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Completed: {result.CompletedOfficePilotCount}/{result.MinimumOfficePilotCount}");
        writer.WriteLine($"Message:   {result.Message}");
        if (result.Issues.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"{"Severity",-8} {"Issue",-26} Message");
            writer.WriteLine(new string('-', 90));
            foreach (var issue in result.Issues)
                writer.WriteLine($"{issue.Severity,-8} {Truncate(issue.Id, 26),-26} {issue.Message}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderPilotRegisterMarkdown(ReleasePilotRegisterResult result)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Release Pilot Register");
        writer.WriteLine();
        writer.WriteLine($"- Status: `{(result.Success ? "PASS" : "FAIL")}`");
        writer.WriteLine($"- Pilot: `{EscapeInlineCode(result.PilotId)}`");
        writer.WriteLine($"- Evidence packet: `{EscapeInlineCode(result.EvidencePacketPath)}`");
        writer.WriteLine($"- Rollout status: `{EscapeInlineCode(result.StatusPath)}`");
        writer.WriteLine($"- Dry run: `{result.DryRun.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Wrote: `{result.Wrote.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Completed pilots: `{result.CompletedOfficePilotCount}/{result.MinimumOfficePilotCount}`");
        writer.WriteLine($"- Office rollout completion claim: `{result.OfficeRolloutCompletion.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Production support claim: `{result.ProductionSupportClaim.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Message: {result.Message}");
        writer.WriteLine();
        writer.WriteLine("## Issues");
        if (result.Issues.Length == 0)
        {
            writer.WriteLine("- None.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine();
        writer.WriteLine("| Severity | Issue | Message |");
        writer.WriteLine("|---|---|---|");
        foreach (var issue in result.Issues)
            writer.WriteLine($"| {EscapeTableCell(issue.Severity)} | {EscapeTableCell(issue.Id)} | {EscapeTableCell(issue.Message)} |");

        return writer.ToString().TrimEnd();
    }

    private sealed record ReleasePilotScaffoldResult(
        string SchemaVersion,
        bool Success,
        string PilotId,
        string EvidencePacketPath,
        bool Wrote,
        bool Force,
        string Message,
        string RolloutStatusHint)
    {
        public static ReleasePilotScaffoldResult Failed(string pilotId, string evidencePacketPath, string message) =>
            new(
                PilotScaffoldSchemaVersion,
                false,
                pilotId,
                evidencePacketPath,
                false,
                false,
                message,
                PilotScaffoldRolloutStatusHint);
    }

    private sealed record ReleasePilotValidateResult(
        string SchemaVersion,
        bool Success,
        string EvidencePacketPath,
        int ErrorCount,
        int WarningCount,
        ReleasePilotValidateIssue[] Issues)
    {
        public static ReleasePilotValidateResult FromIssues(
            string evidencePacketPath,
            List<ReleasePilotValidateIssue> issues)
        {
            var errorCount = issues.Count(issue => issue.Severity == "error");
            var warningCount = issues.Count(issue => issue.Severity == "warning");
            return new ReleasePilotValidateResult(
                PilotValidateSchemaVersion,
                errorCount == 0,
                evidencePacketPath,
                errorCount,
                warningCount,
                issues.ToArray());
        }
    }

    private sealed record ReleasePilotValidateIssue(
        string Id,
        string Severity,
        string Message,
        int? LineNumber,
        string? Excerpt);

    private sealed record ReleasePilotRegisterResult(
        string SchemaVersion,
        bool Success,
        string PilotId,
        string EvidencePacketPath,
        string StatusPath,
        bool DryRun,
        bool Wrote,
        int MinimumOfficePilotCount,
        int CompletedOfficePilotCount,
        bool OfficeRolloutCompletion,
        bool ProductionSupportClaim,
        int ErrorCount,
        int WarningCount,
        string Message,
        ReleasePilotValidateIssue[] Issues)
    {
        public static ReleasePilotRegisterResult From(
            bool success,
            string pilotId,
            string evidencePacketPath,
            string statusPath,
            bool dryRun,
            bool wrote,
            int minimumOfficePilotCount,
            int completedOfficePilotCount,
            bool officeRolloutCompletion,
            bool productionSupportClaim,
            string message,
            List<ReleasePilotValidateIssue> issues)
        {
            var errorCount = issues.Count(issue => issue.Severity == "error");
            var warningCount = issues.Count(issue => issue.Severity == "warning");
            return new ReleasePilotRegisterResult(
                PilotRegisterSchemaVersion,
                success && errorCount == 0,
                pilotId,
                evidencePacketPath,
                statusPath,
                dryRun,
                wrote,
                minimumOfficePilotCount,
                completedOfficePilotCount,
                officeRolloutCompletion,
                productionSupportClaim,
                errorCount,
                warningCount,
                message,
                issues.ToArray());
        }
    }

    private sealed class OfficeRolloutStatusDocument
    {
        public string SchemaVersion { get; set; } = OfficeRolloutStatusSchemaVersion;
        public int MinimumOfficePilotCount { get; set; }
        public int CompletedOfficePilotCount { get; set; }
        public string[] CompletedPilotIds { get; set; } = Array.Empty<string>();
        public CompletedOfficePilotStatus[] CompletedPilots { get; set; } = Array.Empty<CompletedOfficePilotStatus>();
        public bool OfficeRolloutCompletion { get; set; }
        public bool ProductionSupportClaim { get; set; }
        public OfficeRolloutRequiredEvidence RequiredEvidence { get; set; } = new();
    }

    private sealed class CompletedOfficePilotStatus
    {
        public string PilotId { get; set; } = "";
        public string EvidencePacketPath { get; set; } = "";
        public bool Doctor { get; set; }
        public bool Status { get; set; }
        public bool Workbench { get; set; }
        public bool Release { get; set; }
        public bool LedgerQuery { get; set; }
        public bool LedgerValidate { get; set; }
        public bool LedgerStatsAnalyticsSnapshot { get; set; }
        public bool LedgerTimelineAnalyticsSnapshot { get; set; }
        public bool JournalVerify { get; set; }
        public bool RollbackResult { get; set; }
        public bool UserReview { get; set; }
        public bool BimManagerSignoff { get; set; }
        public bool ProjectCopyOwnerSignoff { get; set; }
        public bool SupportTicketReview { get; set; }
        public bool MultiUserRolloutPostmortem { get; set; }

        public static CompletedOfficePilotStatus For(string pilotId, string evidencePacketPath) =>
            new()
            {
                PilotId = pilotId,
                EvidencePacketPath = evidencePacketPath,
                Doctor = true,
                Status = true,
                Workbench = true,
                Release = true,
                LedgerQuery = true,
                LedgerValidate = true,
                LedgerStatsAnalyticsSnapshot = true,
                LedgerTimelineAnalyticsSnapshot = true,
                JournalVerify = true,
                RollbackResult = true,
                UserReview = true,
                BimManagerSignoff = true,
                ProjectCopyOwnerSignoff = true,
                SupportTicketReview = true,
                MultiUserRolloutPostmortem = true,
            };
    }

    private sealed class OfficeRolloutRequiredEvidence
    {
        public bool Doctor { get; set; }
        public bool Status { get; set; }
        public bool Workbench { get; set; }
        public bool Release { get; set; }
        public bool LedgerQuery { get; set; }
        public bool LedgerValidate { get; set; }
        public bool LedgerStatsAnalyticsSnapshot { get; set; }
        public bool LedgerTimelineAnalyticsSnapshot { get; set; }
        public bool JournalVerify { get; set; }
        public bool RollbackResult { get; set; }
        public bool UserReview { get; set; }
        public bool BimManagerSignoff { get; set; }
        public bool ProjectCopyOwnerSignoff { get; set; }
        public bool SupportTicketReview { get; set; }
        public bool MultiUserRolloutPostmortem { get; set; }
    }

    private static string RenderTable(ReleaseVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Release verification");
        writer.WriteLine($"Root:    {report.Root}");
        writer.WriteLine($"Version: {report.Version ?? "(missing)"}");
        if (!string.IsNullOrWhiteSpace(report.Tag))
        {
            writer.WriteLine($"Tag:     {report.Tag}");
        }

        writer.WriteLine($"Result:  {(report.Success ? "PASS" : "FAIL")} ({report.ErrorCount} error, {report.WarningCount} warning)");
        writer.WriteLine();
        writer.WriteLine($"{"Status",-7} {"Check",-34} Message");
        writer.WriteLine(new string('-', 100));

        foreach (var check in report.Checks
            .OrderBy(check => check.Status switch
            {
                ReleaseVerifyStatus.Error => 0,
                ReleaseVerifyStatus.Warning => 1,
                _ => 2,
            })
            .ThenBy(check => check.Id, StringComparer.Ordinal))
        {
            writer.WriteLine($"{FormatStatus(check.Status),-7} {Truncate(check.Id, 34),-34} {check.Message}");
        }

        writer.WriteLine();
        writer.WriteLine("Note: release verify checks local release files and CI guardrails only; real Revit smoke remains a Windows/Revit checklist gate.");
        writer.WriteLine("For v5.0 RC handoff, use --strict so disclosed NO-GO smoke gaps block the release.");
        return writer.ToString().TrimEnd();
    }

    private static string RenderMarkdown(ReleaseVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Release Verification");
        writer.WriteLine();
        writer.WriteLine($"- Status: `{(report.Success ? "PASS" : "FAIL")}`");
        writer.WriteLine($"- Root: `{EscapeInlineCode(report.Root)}`");
        writer.WriteLine($"- Version: `{EscapeInlineCode(report.Version ?? "(missing)")}`");
        if (!string.IsNullOrWhiteSpace(report.Tag))
            writer.WriteLine($"- Tag: `{EscapeInlineCode(report.Tag)}`");
        writer.WriteLine($"- Strict: `{report.Strict.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Errors: `{report.ErrorCount}`");
        writer.WriteLine($"- Warnings: `{report.WarningCount}`");
        writer.WriteLine();

        AppendChecksMarkdown(writer, "Errors", report.Checks
            .Where(check => check.Status == ReleaseVerifyStatus.Error));
        AppendChecksMarkdown(writer, "Warnings", report.Checks
            .Where(check => check.Status == ReleaseVerifyStatus.Warning));
        AppendChecksMarkdown(writer, "Passing Checks", report.Checks
            .Where(check => check.Status == ReleaseVerifyStatus.Ok));

        writer.WriteLine();
        writer.WriteLine("## Gate Scope");
        writer.WriteLine();
        writer.WriteLine("- `release verify` checks local release files and CI guardrails only.");
        writer.WriteLine("- Real Revit smoke remains a separate Windows/Revit checklist gate.");
        writer.WriteLine("- For v5.0 RC handoff, run `release verify --strict`; disclosed NO-GO smoke gaps become blockers.");
        return writer.ToString().TrimEnd();
    }

    private static void AppendChecksMarkdown(
        TextWriter writer,
        string title,
        IEnumerable<ReleaseVerifyCheck> checks)
    {
        writer.WriteLine($"## {title}");
        var ordered = checks
            .OrderBy(check => check.Id, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0)
        {
            writer.WriteLine("- None.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine();
        writer.WriteLine("| Status | Check | Path | Message |");
        writer.WriteLine("|---|---|---|---|");
        foreach (var check in ordered)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(FormatStatus(check.Status))} | {EscapeTableCell(check.Id)} | {EscapeTableCell(check.Path ?? "-")} | {EscapeTableCell(check.Message)} |");
        }

        writer.WriteLine();
    }

    private static string FormatStatus(ReleaseVerifyStatus status) => status switch
    {
        ReleaseVerifyStatus.Ok => "OK",
        ReleaseVerifyStatus.Warning => "WARN",
        ReleaseVerifyStatus.Error => "ERROR",
        _ => status.ToString().ToUpperInvariant(),
    };

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value[..Math.Max(0, max - 1)] + "…";
    }

    private static string EscapeInlineCode(string value)
    {
        return value
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeTableCell(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }
}
