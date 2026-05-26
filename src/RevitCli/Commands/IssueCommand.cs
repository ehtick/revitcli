using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Shared;
using RevitCli.Workflows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Commands;

public static class IssueCommand
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static Command Create(RevitClient client)
    {
        var command = new Command("issue", "Run terminal issue preflight, diff, and package contracts");
        command.AddCommand(CreatePreflightCommand());
        command.AddCommand(CreateDiffCommand(client));
        command.AddCommand(CreatePackageCommand());
        return command;
    }

    private static Command CreatePreflightCommand()
    {
        var profileOpt = new Option<string>("--profile", "Issue profile YAML") { IsRequired = true };
        var outputOpt = new Option<string>("--output", () => "markdown", "Output format: table|json|markdown");
        var failOnOpt = new Option<string>("--fail-on", () => "error", "Fail on: warning|error");
        var command = new Command("preflight", "Review issue readiness before export/package work")
        {
            profileOpt,
            outputOpt,
            failOnOpt
        };

        command.SetHandler(async (string profile, string output, string failOn) =>
        {
            Environment.ExitCode = await ExecutePreflightAsync(profile, output, failOn, Console.Out);
        }, profileOpt, outputOpt, failOnOpt);
        return command;
    }

    private static Command CreateDiffCommand(RevitClient client)
    {
        var fromOpt = new Option<string>("--from", "Baseline snapshot JSON file") { IsRequired = true };
        var toOpt = new Option<string>("--to", () => "current", "Current snapshot JSON file, or 'current' to capture from Revit");
        var reviewOpt = new Option<bool>("--review", "Include grouped anomaly/notable/routine review evidence");
        var outputOpt = new Option<string>("--output", () => "markdown", "Output format: table|json|markdown");
        var reportOpt = new Option<string?>("--report", "Write issue diff report to a file");
        var maxRowsOpt = new Option<int>("--max-rows", () => 20, "Maximum review groups shown in table/markdown");
        var command = new Command("diff", "Create an issue-scoped diff report from snapshots")
        {
            fromOpt,
            toOpt,
            reviewOpt,
            outputOpt,
            reportOpt,
            maxRowsOpt
        };

        command.SetHandler(async (
            string from,
            string to,
            bool review,
            string output,
            string? report,
            int maxRows) =>
        {
            Environment.ExitCode = await ExecuteDiffAsync(
                client,
                from,
                to,
                review,
                output,
                report,
                maxRows,
                Console.Out);
        }, fromOpt, toOpt, reviewOpt, outputOpt, reportOpt, maxRowsOpt);
        return command;
    }

    private static Command CreatePackageCommand()
    {
        var profileOpt = new Option<string>("--profile", "Issue profile YAML") { IsRequired = true };
        var bundlePathOpt = new Option<string>("--bundle-path", "Issue package zip path") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", "Plan the issue package without writing zip or receipt");
        var signJournalOpt = new Option<bool>("--sign-journal", "Sign the local journal before packaging");
        var includeReceiptsOpt = new Option<bool>("--include-receipts", () => true, "Include child receipts in the package");
        var outputOpt = new Option<string>("--output", () => "markdown", "Output format: table|json|markdown");
        var command = new Command("package", "Package issue deliverables, receipts, and traceability evidence")
        {
            profileOpt,
            bundlePathOpt,
            dryRunOpt,
            signJournalOpt,
            includeReceiptsOpt,
            outputOpt
        };

        command.SetHandler(async (
            string profile,
            string bundlePath,
            bool dryRun,
            bool signJournal,
            bool includeReceipts,
            string output) =>
        {
            Environment.ExitCode = await ExecutePackageAsync(
                profile,
                bundlePath,
                dryRun,
                signJournal,
                includeReceipts,
                output,
                Console.Out);
        }, profileOpt, bundlePathOpt, dryRunOpt, signJournalOpt, includeReceiptsOpt, outputOpt);
        return command;
    }

    internal static async Task<int> ExecutePreflightAsync(
        string profilePath,
        string outputFormat,
        string failOn,
        TextWriter output)
    {
        if (!TryNormalizeOutput(outputFormat, output, out var normalizedOutput))
            return 1;
        if (!TryNormalizeFailOn(failOn, output, out var normalizedFailOn))
            return 1;

        IssueProfile profile;
        string fullProfilePath;
        try
        {
            profile = LoadProfile(profilePath, out fullProfilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var report = BuildPreflightReport(profile, fullProfilePath, normalizedFailOn);
        await WriteRenderedAsync(output, report, normalizedOutput);
        return report.ShouldFail ? 2 : 0;
    }

    internal static async Task<int> ExecuteDiffAsync(
        RevitClient client,
        string fromPath,
        string toPath,
        bool review,
        string outputFormat,
        string? reportPath,
        int maxRows,
        TextWriter output)
    {
        if (!TryNormalizeOutput(outputFormat, output, out var normalizedOutput))
            return 1;
        if (maxRows < 1)
        {
            await output.WriteLineAsync("Error: --max-rows must be at least 1.");
            return 1;
        }

        ModelSnapshot fromSnapshot;
        try
        {
            fromSnapshot = await ReadSnapshotFileAsync(fromPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            await output.WriteLineAsync($"Error: failed to read baseline snapshot: {ex.Message}");
            return 1;
        }

        var capturedCurrent = false;
        ModelSnapshot toSnapshot;
        if (string.Equals(toPath, "current", StringComparison.OrdinalIgnoreCase))
        {
            var response = await client.CaptureSnapshotAsync(new SnapshotRequest());
            if (!response.Success || response.Data == null)
            {
                await output.WriteLineAsync($"Error: {response.Error ?? "snapshot capture returned no data."}");
                return 1;
            }

            toSnapshot = response.Data;
            capturedCurrent = true;
        }
        else
        {
            try
            {
                toSnapshot = await ReadSnapshotFileAsync(toPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                await output.WriteLineAsync($"Error: failed to read current snapshot: {ex.Message}");
                return 1;
            }
        }

        IssueDiffReport report;
        try
        {
            var diff = SnapshotDiffer.Diff(
                fromSnapshot,
                toSnapshot,
                Path.GetFileName(fromPath),
                capturedCurrent ? "current" : Path.GetFileName(toPath));
            report = IssueDiffReport.From(diff, fromPath, toPath, capturedCurrent, review);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var rendered = Render(report, normalizedOutput, maxRows);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var reportFullPath = Path.GetFullPath(reportPath);
            var dir = Path.GetDirectoryName(reportFullPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(reportFullPath, rendered);
            await output.WriteLineAsync($"Issue diff report saved to {reportFullPath}");
        }
        else
        {
            await output.WriteLineAsync(rendered);
        }

        return 0;
    }

    internal static async Task<int> ExecutePackageAsync(
        string profilePath,
        string bundlePath,
        bool dryRun,
        bool signJournal,
        bool includeReceipts,
        string outputFormat,
        TextWriter output)
    {
        if (!TryNormalizeOutput(outputFormat, output, out var normalizedOutput))
            return 1;

        IssueProfile profile;
        string fullProfilePath;
        try
        {
            profile = LoadProfile(profilePath, out fullProfilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var projectRoot = ResolveProjectRoot(fullProfilePath);
        var report = BuildPackageReport(profile, fullProfilePath, projectRoot, bundlePath, dryRun, signJournal, includeReceipts);
        if (!dryRun && report.Valid)
        {
            if (signJournal)
                await SignJournalForPackageAsync(projectRoot, report);
            if (report.Valid)
                TryWriteIssuePackage(report);
        }

        await WriteRenderedAsync(output, report, normalizedOutput);
        return report.Success ? 0 : 1;
    }

    private static IssuePreflightReport BuildPreflightReport(IssueProfile profile, string profilePath, string failOn)
    {
        var report = new IssuePreflightReport
        {
            ProfilePath = profilePath,
            FailOn = failOn
        };

        if (!IsSupportedProfileSchema(profile.SchemaVersion))
        {
            report.Issues.Add(new IssueContractIssue(
                "error",
                "profile-schema-invalid",
                $"schemaVersion must be issue-profile.v1, got '{profile.SchemaVersion}'.",
                profilePath,
                null));
        }

        foreach (var artifact in profile.Artifacts)
        {
            var resolvedPath = ResolveProfileRelativePath(profilePath, artifact.Path);
            var severity = NormalizeSeverity(artifact.Severity, "warning");
            var exists = File.Exists(resolvedPath) || Directory.Exists(resolvedPath);
            report.Checks.Add(new IssuePreflightCheck
            {
                Name = string.IsNullOrWhiteSpace(artifact.Name) ? artifact.Path : artifact.Name,
                Kind = "artifact",
                Severity = severity,
                Status = exists ? "pass" : "fail",
                Path = resolvedPath,
                Message = exists ? "Artifact exists." : "Required issue artifact is missing."
            });
            if (!exists)
            {
                report.Issues.Add(new IssueContractIssue(
                    severity,
                    "artifact-missing",
                    $"Issue artifact not found: {resolvedPath}",
                    resolvedPath,
                    null));
            }
        }

        var reviewedPlanPaths = GetReviewedMutationPlanPaths(profile, profilePath);
        foreach (var check in profile.Checks)
        {
            var severity = NormalizeSeverity(check.Severity, "warning");
            var hiddenMutation = TryCreateHiddenMutationIssue(
                check.Command,
                profilePath,
                reviewedPlanPaths,
                "Issue command",
                out var hiddenMutationIssue);
            report.Checks.Add(new IssuePreflightCheck
            {
                Name = string.IsNullOrWhiteSpace(check.Name) ? check.Command : check.Name,
                Kind = "command",
                Severity = severity,
                Status = hiddenMutation ? "fail" : "planned",
                Command = check.Command,
                Message = hiddenMutation
                    ? hiddenMutationIssue.Message
                    : "Command is safe to review or execute through its own contract."
            });
            if (hiddenMutation)
            {
                report.Issues.Add(hiddenMutationIssue);
            }
        }

        foreach (var command in profile.Package.Commands)
        {
            if (TryCreateHiddenMutationIssue(
                command,
                profilePath,
                reviewedPlanPaths,
                "Package command",
                out var hiddenMutationIssue))
            {
                report.Issues.Add(hiddenMutationIssue);
            }
        }

        foreach (var plan in profile.MutationPlans)
        {
            var resolvedPlan = ResolveProfileRelativePath(profilePath, plan.PlanPath);
            var planExists = File.Exists(resolvedPlan);
            report.Checks.Add(new IssuePreflightCheck
            {
                Name = string.IsNullOrWhiteSpace(plan.Name) ? plan.PlanPath : plan.Name,
                Kind = "mutation-plan",
                Severity = "error",
                Status = planExists ? "pass" : "fail",
                Path = resolvedPlan,
                Message = planExists
                    ? "Mutation plan is explicit and reviewable."
                    : "Explicit mutation plan is missing."
            });
            if (!planExists)
            {
                report.Issues.Add(new IssueContractIssue(
                    "error",
                    "mutation-plan-missing",
                    $"Mutation plan not found: {resolvedPlan}",
                    resolvedPlan,
                    null));
            }
        }

        var projectRoot = ResolveProjectRoot(profilePath);
        var manifest = DeliveryManifestReader.Read(projectRoot);
        report.Checks.Add(new IssuePreflightCheck
        {
            Name = "deliverables-manifest",
            Kind = "deliverables",
            Severity = "warning",
            Status = manifest.Exists && manifest.Valid ? "pass" : "fail",
            Path = manifest.ManifestPath,
            Message = manifest.Exists
                ? $"Delivery manifest entries={manifest.EntryCount}, errors={manifest.Stats.ErrorCount}."
                : "Delivery manifest has not been created yet."
        });
        foreach (var issue in manifest.Issues)
        {
            report.Issues.Add(new IssueContractIssue(
                issue.Severity,
                issue.Code,
                issue.Message,
                manifest.ManifestPath,
                null));
        }

        report.CommandPaths.Add($"revitcli issue preflight --profile {Quote(profilePath)} --output markdown");
        report.CommandPaths.Add(profile.Diff.Baseline == null
            ? "revitcli issue diff --from baseline.json --to current --review --output markdown"
            : $"revitcli issue diff --from {Quote(ResolveProfileRelativePath(profilePath, profile.Diff.Baseline))} --to current --review --output markdown");
        report.CommandPaths.Add($"revitcli issue package --profile {Quote(profilePath)} --bundle-path {Quote(profile.Package.BundlePath ?? "deliverables/issue-package.zip")} --dry-run --include-receipts true --output markdown");
        return report;
    }

    private static IssuePackageReport BuildPackageReport(
        IssueProfile profile,
        string profilePath,
        string projectRoot,
        string bundlePath,
        bool dryRun,
        bool signJournal,
        bool includeReceipts)
    {
        var resolvedBundlePath = ResolvePath(projectRoot, bundlePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var receiptPath = Path.Combine(projectRoot, ".revitcli", "receipts", $"issue-package-{timestamp}.json");
        var report = new IssuePackageReport
        {
            ProfilePath = profilePath,
            ProjectDirectory = projectRoot,
            BundlePath = resolvedBundlePath,
            ReceiptPath = receiptPath,
            DryRun = dryRun,
            IncludeReceipts = includeReceipts,
            SignJournal = signJournal
        };

        AddPackagePlannedActions(report, profile);
        var preflight = BuildPreflightReport(profile, profilePath, "error");
        report.PreflightCheckCount = preflight.CheckCount;
        report.PreflightErrorCount = preflight.ErrorCount;
        report.PreflightWarningCount = preflight.WarningCount;
        foreach (var issue in preflight.Issues.Where(ShouldCarryPreflightIssueIntoPackage))
            AddIssueOnce(report.Issues, issue);

        DeliveryBundleReport bundleReport;
        try
        {
            bundleReport = DeliveryBundlePlanner.Plan(projectRoot, resolvedBundlePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            report.Issues.Add(new IssueContractIssue(
                "error",
                "bundle-plan-failed",
                $"Failed to plan deliverables bundle: {ex.Message}",
                resolvedBundlePath,
                null));
            return report;
        }

        report.ManifestPath = bundleReport.ManifestPath;
        foreach (var issue in bundleReport.Issues)
        {
            AddIssueOnce(report.Issues, new IssueContractIssue(
                issue.Severity,
                issue.Code,
                issue.Message,
                bundleReport.ManifestPath,
                null));
        }

        var files = includeReceipts
            ? bundleReport.Files
            : bundleReport.Files.Where(file => !string.Equals(file.Kind, "receipt", StringComparison.OrdinalIgnoreCase));
        report.Files.AddRange(files.Select(file => new IssuePackageFile
        {
            Kind = file.Kind,
            SourcePath = file.SourcePath,
            ArchivePath = file.ArchivePath,
            Bytes = file.Bytes,
            Sha256 = file.Sha256,
            LineNumber = file.LineNumber
        }));

        if (signJournal)
        {
            var signaturePath = Path.Combine(projectRoot, ".revitcli", "journal.jsonl.sig");
            report.JournalSignaturePath = signaturePath;
            if (dryRun)
            {
                report.CommandPaths.Add($"revitcli journal sign --dir {Quote(projectRoot)}");
            }
        }

        if (File.Exists(resolvedBundlePath) && !dryRun)
        {
            report.Issues.Add(new IssueContractIssue(
                "error",
                "bundle-exists",
                $"Bundle already exists: {resolvedBundlePath}",
                resolvedBundlePath,
                null));
        }

        report.CommandPaths.Add($"revitcli deliverables verify --dir {Quote(projectRoot)} --output markdown");
        report.CommandPaths.Add($"revitcli deliverables bundle --dir {Quote(projectRoot)} --bundle-path {Quote(resolvedBundlePath)} --dry-run --output markdown");
        report.CommandPaths.Add($"revitcli issue package --profile {Quote(profilePath)} --bundle-path {Quote(resolvedBundlePath)} --include-receipts {includeReceipts.ToString().ToLowerInvariant()}");
        return report;
    }

    private static async Task SignJournalForPackageAsync(string projectRoot, IssuePackageReport report)
    {
        var signOutput = new StringWriter();
        var exitCode = await JournalCommand.ExecuteSignAsync(projectRoot, null, null, null, null, "json", signOutput);
        if (exitCode != 0)
        {
            report.Issues.Add(new IssueContractIssue(
                "error",
                "journal-sign-failed",
                signOutput.ToString().Trim(),
                Path.Combine(projectRoot, ".revitcli", "journal.jsonl"),
                "revitcli journal sign"));
            return;
        }

        var signaturePath = Path.Combine(projectRoot, ".revitcli", "journal.jsonl.sig");
        report.JournalSignaturePath = signaturePath;
        if (File.Exists(signaturePath))
        {
            var info = new FileInfo(signaturePath);
            report.Files.Add(new IssuePackageFile
            {
                Kind = "journal-signature",
                SourcePath = signaturePath,
                ArchivePath = ".revitcli/journal.jsonl.sig",
                Bytes = info.Length,
                Sha256 = ComputeSha256Hex(signaturePath)
            });
        }
    }

    private static void WriteIssuePackage(IssuePackageReport report)
    {
        var bundleDir = Path.GetDirectoryName(report.BundlePath);
        if (!string.IsNullOrWhiteSpace(bundleDir))
            Directory.CreateDirectory(bundleDir);

        using (var archive = ZipFile.Open(report.BundlePath, ZipArchiveMode.Create))
        {
            foreach (var file in report.Files)
                archive.CreateEntryFromFile(file.SourcePath, file.ArchivePath, CompressionLevel.Optimal);
        }

        report.BundleWritten = true;
        report.BundleHash = ComputeSha256Hex(report.BundlePath);
        report.WrittenAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        var receiptDir = Path.GetDirectoryName(report.ReceiptPath);
        if (!string.IsNullOrWhiteSpace(receiptDir))
            Directory.CreateDirectory(receiptDir);
        report.ReceiptWritten = true;
        File.WriteAllText(report.ReceiptPath, JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel));
    }

    private static void TryWriteIssuePackage(IssuePackageReport report)
    {
        try
        {
            WriteIssuePackage(report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or NotSupportedException)
        {
            var failureCode = report.BundleWritten && !report.ReceiptWritten
                ? "receipt-write-failed"
                : "bundle-write-failed";
            var failurePath = string.Equals(failureCode, "receipt-write-failed", StringComparison.Ordinal)
                ? report.ReceiptPath
                : report.BundlePath;
            TryDeleteFile(report.BundlePath);
            TryDeleteFile(report.ReceiptPath);
            report.BundleWritten = false;
            report.BundleHash = null;
            report.WrittenAtUtc = null;
            report.ReceiptWritten = false;
            AddIssueOnce(report.Issues, new IssueContractIssue(
                "error",
                failureCode,
                $"Failed to write issue package: {ex.Message}",
                failurePath,
                "revitcli issue package"));
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void AddPackagePlannedActions(IssuePackageReport report, IssueProfile profile)
    {
        report.PlannedActions.Add("preflight-checks");
        report.PlannedActions.Add("delivery-manifest");
        report.PlannedActions.Add("receipt-trace");
        report.PlannedActions.Add("bundle");
        if (report.SignJournal)
            report.PlannedActions.Add("journal-sign");

        var commands = profile.Checks
            .Select(check => check.Command)
            .Concat(profile.Package.Commands)
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Select(command => command.Trim().ToLowerInvariant())
            .ToArray();
        if (commands.Any(command => command.Contains("schedules batch-export", StringComparison.Ordinal) ||
            command.Contains("schedule export", StringComparison.Ordinal)))
        {
            report.PlannedActions.Add("schedule-export");
        }

        if (commands.Any(command => command.Contains(" export ", StringComparison.Ordinal) ||
            command.StartsWith("revitcli export ", StringComparison.Ordinal) ||
            command.StartsWith("export ", StringComparison.Ordinal) ||
            command.Contains(" publish ", StringComparison.Ordinal) ||
            command.StartsWith("revitcli publish ", StringComparison.Ordinal) ||
            command.StartsWith("publish ", StringComparison.Ordinal)))
        {
            report.PlannedActions.Add("export");
        }
    }

    private static bool ShouldCarryPreflightIssueIntoPackage(IssueContractIssue issue) =>
        issue.Code is "profile-schema-invalid" or "artifact-missing" or "command-parse-failed" or "command-shell-operator" or "hidden-model-mutation" or "mutation-plan-missing";

    private static void AddIssueOnce(List<IssueContractIssue> issues, IssueContractIssue issue)
    {
        if (issues.Any(existing =>
            string.Equals(existing.Code, issue.Code, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Message, issue.Message, StringComparison.Ordinal)))
        {
            return;
        }

        issues.Add(issue);
    }

    private static async Task<ModelSnapshot> ReadSnapshotFileAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"snapshot not found: {path}", path);
        return JsonSerializer.Deserialize<ModelSnapshot>(
            await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("Snapshot deserializes to null.");
    }

    private static IssueProfile LoadProfile(string path, out string fullPath)
    {
        fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Issue profile not found: {fullPath}", fullPath);
        var profile = Yaml.Deserialize<IssueProfile>(File.ReadAllText(fullPath))
            ?? throw new InvalidOperationException($"Failed to parse issue profile: {fullPath}");
        profile.Normalize();
        return profile;
    }

    private static bool IsSupportedProfileSchema(string? schemaVersion) =>
        string.Equals(schemaVersion, "issue-profile.v1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(schemaVersion, "1", StringComparison.OrdinalIgnoreCase);

    private static string ResolveProjectRoot(string profilePath)
    {
        var profileDir = Path.GetDirectoryName(Path.GetFullPath(profilePath)) ?? Directory.GetCurrentDirectory();
        return string.Equals(Path.GetFileName(profileDir), ".revitcli", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(profileDir) ?? Directory.GetCurrentDirectory()
            : Directory.GetCurrentDirectory();
    }

    private static string ResolveProfileRelativePath(string profilePath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(ResolveProjectRoot(profilePath), path));
    }

    private static string ResolvePath(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));

    private static HashSet<string> GetReviewedMutationPlanPaths(IssueProfile profile, string profilePath) =>
        profile.MutationPlans
            .Select(plan => ResolveProfileRelativePath(profilePath, plan.PlanPath))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool TryCreateHiddenMutationIssue(
        string? command,
        string profilePath,
        IReadOnlySet<string> reviewedPlanPaths,
        string context,
        out IssueContractIssue issue)
    {
        issue = new IssueContractIssue("", "", "", null, null);
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (WorkflowCommandLine.ContainsShellOperator(command))
        {
            issue = new IssueContractIssue(
                "error",
                "command-shell-operator",
                $"{context} must be a single revitcli command without shell operators: {command}",
                null,
                command);
            return true;
        }

        if (!TryTokenizeCommand(command, out var tokens, out var parseError))
        {
            issue = new IssueContractIssue(
                "error",
                "command-parse-failed",
                $"{context} could not be parsed: {parseError}",
                null,
                command);
            return true;
        }

        if (IsPlanApplyCommand(tokens, out var planPath))
        {
            if (CommandBoolOptionIsTrue(tokens, "--dry-run"))
                return false;

            if (ReferencesReviewedPlan(profilePath, planPath, reviewedPlanPaths))
                return false;

            issue = new IssueContractIssue(
                "error",
                "hidden-model-mutation",
                $"{context} must reference an existing mutationPlans entry before plan apply writes: {command}",
                string.IsNullOrWhiteSpace(planPath) ? null : ResolveProfileRelativePath(profilePath, planPath),
                command);
            return true;
        }

        if (!IsHiddenModelMutation(tokens))
            return false;

        issue = new IssueContractIssue(
            "error",
            "hidden-model-mutation",
            $"{context} must reference a reviewed plan or dry-run path before model writes: {command}",
            null,
            command);
        return true;
    }

    private static bool TryTokenizeCommand(string command, out IReadOnlyList<string> tokens, out string error)
    {
        try
        {
            tokens = WorkflowCommandLine.Tokenize(command);
            error = "";
            return true;
        }
        catch (FormatException ex)
        {
            tokens = Array.Empty<string>();
            error = ex.Message;
            return false;
        }
    }

    private static bool IsHiddenModelMutation(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return false;

        if (CommandBoolOptionIsTrue(tokens, "--dry-run") ||
            CommandHasOption(tokens, "--plan-output"))
        {
            return false;
        }

        var offset = tokens.Count > 0 && string.Equals(tokens[0], "revitcli", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        if (offset >= tokens.Count)
            return false;

        if (CommandStartsWith(tokens, offset, "rollback"))
        {
            return false;
        }

        if (CommandStartsWith(tokens, offset, "set") ||
            CommandStartsWith(tokens, offset, "import"))
        {
            return true;
        }

        if (CommandStartsWith(tokens, offset, "fix") &&
            CommandBoolOptionIsTrue(tokens, "--apply"))
        {
            return true;
        }

        if (CommandStartsWith(tokens, offset, "schedule", "create"))
        {
            return true;
        }

        if (CommandStartsWith(tokens, offset, "family", "purge") &&
            CommandBoolOptionIsTrue(tokens, "--apply"))
        {
            return true;
        }

        return false;
    }

    private static bool IsPlanApplyCommand(IReadOnlyList<string> tokens, out string? planPath)
    {
        planPath = null;
        var offset = tokens.Count > 0 && string.Equals(tokens[0], "revitcli", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        if (tokens.Count <= offset + 1 ||
            !string.Equals(tokens[offset], "plan", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tokens[offset + 1], "apply", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = offset + 2; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, "--", StringComparison.Ordinal))
            {
                planPath = tokens.Skip(i + 1).FirstOrDefault();
                break;
            }

            if (IsPlanApplyOptionWithValue(token))
            {
                if (!OptionUsesEqualsValue(token) && i + 1 < tokens.Count)
                    i++;
                continue;
            }

            if (IsPlanApplyBooleanOption(token))
            {
                if (!OptionUsesEqualsValue(token) &&
                    i + 1 < tokens.Count &&
                    IsBooleanLiteral(tokens[i + 1]))
                {
                    i++;
                }

                continue;
            }

            if (token.StartsWith("-", StringComparison.Ordinal))
                continue;

            planPath = token;
            break;
        }
        return true;
    }

    private static bool IsPlanApplyOptionWithValue(string token) =>
        IsOptionToken(token, "--max-changes") ||
        IsOptionToken(token, "--profile") ||
        IsOptionToken(token, "--high-impact-threshold");

    private static bool IsPlanApplyBooleanOption(string token) =>
        IsOptionToken(token, "--yes") ||
        IsOptionToken(token, "--dry-run") ||
        IsOptionToken(token, "--confirm-high-impact") ||
        IsOptionToken(token, "--allow-inferred");

    private static bool OptionUsesEqualsValue(string token) =>
        token.Contains('=');

    private static bool IsOptionToken(string token, string option) =>
        string.Equals(token, option, StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase);

    private static bool IsBooleanLiteral(string token) =>
        string.Equals(token, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(token, "false", StringComparison.OrdinalIgnoreCase);

    private static bool CommandBoolOptionIsTrue(IReadOnlyList<string> tokens, string option)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, option, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < tokens.Count && IsBooleanLiteral(tokens[i + 1]))
                    return string.Equals(tokens[i + 1], "true", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            if (token.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = token[(option.Length + 1)..];
                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool CommandHasOption(IReadOnlyList<string> tokens, string option) =>
        tokens.Any(token => IsOptionToken(token, option));

    private static bool CommandStartsWith(IReadOnlyList<string> tokens, int offset, params string[] commandTokens)
    {
        if (tokens.Count < offset + commandTokens.Length)
            return false;

        for (var i = 0; i < commandTokens.Length; i++)
        {
            if (!string.Equals(tokens[offset + i], commandTokens[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool ReferencesReviewedPlan(
        string profilePath,
        string? planPath,
        IReadOnlySet<string> reviewedPlanPaths) =>
        !string.IsNullOrWhiteSpace(planPath) &&
        reviewedPlanPaths.Contains(ResolveProfileRelativePath(profilePath, planPath));

    private static string NormalizeSeverity(string? severity, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(severity) ? fallback : severity.Trim().ToLowerInvariant();
        return value is "error" or "warning" or "info" ? value : fallback;
    }

    private static bool TryNormalizeOutput(string outputFormat, TextWriter output, out string normalized)
    {
        if (TerminalOutputFormat.TryNormalize(outputFormat, out normalized, "table", "json", "markdown"))
            return true;
        output.WriteLine("Error: --output must be 'table', 'json', or 'markdown'.");
        return false;
    }

    private static bool TryNormalizeFailOn(string failOn, TextWriter output, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(failOn) ? "error" : failOn.Trim().ToLowerInvariant();
        if (normalized is "warning" or "error")
            return true;
        output.WriteLine("Error: --fail-on must be 'warning' or 'error'.");
        return false;
    }

    private static async Task WriteRenderedAsync(TextWriter output, object report, string format) =>
        await output.WriteLineAsync(Render(report, format, 20));

    private static string Render(object report, string format, int maxRows) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderMarkdown(report, maxRows),
            _ => RenderTable(report, maxRows)
        };

    private static string RenderTable(object report, int maxRows) =>
        report switch
        {
            IssuePreflightReport preflight => $"Issue preflight ({preflight.SchemaVersion}): checks={preflight.CheckCount}, errors={preflight.ErrorCount}, warnings={preflight.WarningCount}",
            IssueDiffReport diff => $"Issue diff ({diff.SchemaVersion}): changes={diff.TotalChanges}, highest={diff.HighestSeverity}",
            IssuePackageReport package => $"Issue package ({package.SchemaVersion}): files={package.FileCount}, errors={package.ErrorCount}, written={package.BundleWritten.ToString().ToLowerInvariant()}",
            _ => report.ToString() ?? ""
        };

    private static string RenderMarkdown(object report, int maxRows)
    {
        var lines = new List<string>();
        switch (report)
        {
            case IssuePreflightReport preflight:
                lines.Add("# Issue Preflight");
                lines.Add("");
                lines.Add($"- Schema: `{preflight.SchemaVersion}`");
                lines.Add($"- Status: `{(preflight.Success ? "pass" : "fail")}`");
                lines.Add($"- Checks: `{preflight.CheckCount}`");
                lines.Add($"- Errors: `{preflight.ErrorCount}`");
                lines.Add($"- Warnings: `{preflight.WarningCount}`");
                lines.Add("");
                lines.Add("| Check | Kind | Status | Severity | Evidence |");
                lines.Add("|---|---|---|---|---|");
                foreach (var check in preflight.Checks)
                    lines.Add($"| {EscapeCell(check.Name)} | `{check.Kind}` | `{check.Status}` | `{check.Severity}` | {EscapeCell(check.Message)} |");
                AppendIssuesMarkdown(lines, preflight.Issues);
                AppendCommandsMarkdown(lines, preflight.CommandPaths);
                break;

            case IssueDiffReport diff:
                lines.Add("# Issue Diff");
                lines.Add("");
                lines.Add($"- Schema: `{diff.SchemaVersion}`");
                lines.Add($"- From: `{EscapeInline(diff.FromPath)}`");
                lines.Add($"- To: `{EscapeInline(diff.ToPath)}`");
                lines.Add($"- Total changes: `{diff.TotalChanges}`");
                lines.Add($"- Highest severity: `{diff.HighestSeverity}`");
                lines.Add("");
                lines.Add("| Severity | Scope | Name | Change | Count | Recommendation |");
                lines.Add("|---|---|---|---|---:|---|");
                foreach (var group in diff.Groups.Take(maxRows))
                    lines.Add($"| `{group.Severity}` | `{group.Scope}` | {EscapeCell(group.Name)} | `{group.ChangeType}` | {group.Count} | {EscapeCell(group.Recommendation ?? "")} |");
                AppendCommandsMarkdown(lines, diff.CommandPaths);
                break;

            case IssuePackageReport package:
                lines.Add("# Issue Package");
                lines.Add("");
                lines.Add($"- Schema: `{package.SchemaVersion}`");
                lines.Add($"- Status: `{(package.Success ? "pass" : "fail")}`");
                lines.Add($"- Dry run: `{package.DryRun.ToString().ToLowerInvariant()}`");
                lines.Add($"- Bundle: `{EscapeInline(package.BundlePath)}`");
                lines.Add($"- Receipt: `{EscapeInline(package.ReceiptPath)}`");
                lines.Add($"- Files: `{package.FileCount}`");
                lines.Add($"- Planned actions: `{EscapeInline(string.Join(", ", package.PlannedActions))}`");
                lines.Add($"- Bundle hash: `{EscapeInline(package.BundleHash ?? "-")}`");
                lines.Add("");
                lines.Add("| Kind | Archive path | Bytes | Source |");
                lines.Add("|---|---|---:|---|");
                foreach (var file in package.Files.Take(maxRows))
                    lines.Add($"| `{file.Kind}` | `{EscapeInline(file.ArchivePath)}` | {file.Bytes} | `{EscapeInline(file.SourcePath)}` |");
                AppendIssuesMarkdown(lines, package.Issues);
                AppendCommandsMarkdown(lines, package.CommandPaths);
                break;
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static void AppendIssuesMarkdown(List<string> lines, IReadOnlyList<IssueContractIssue> issues)
    {
        if (issues.Count == 0)
            return;
        lines.Add("");
        lines.Add("## Issues");
        lines.Add("");
        lines.Add("| Severity | Category | Code | Safe retry | Message | Remediation |");
        lines.Add("|---|---|---|---|---|---|");
        foreach (var issue in issues)
            lines.Add($"| `{issue.Severity}` | `{issue.Category}` | `{issue.Code}` | `{issue.SafeRetry.ToString().ToLowerInvariant()}` | {EscapeCell(issue.Message)} | {EscapeCell(issue.Remediation)} |");
    }

    private static void AppendCommandsMarkdown(List<string> lines, IReadOnlyList<string> commands)
    {
        if (commands.Count == 0)
            return;
        lines.Add("");
        lines.Add("## Commands");
        lines.Add("");
        foreach (var command in commands)
            lines.Add($"- `{EscapeInline(command)}`");
    }

    private static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static string EscapeCell(string? value) =>
        (value ?? "")
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string EscapeInline(string? value) =>
        (value ?? "").Replace("`", "\\`", StringComparison.Ordinal);

    public sealed class IssueProfile
    {
        public string SchemaVersion { get; set; } = "issue-profile.v1";
        public List<IssueProfileCheck> Checks { get; set; } = new();
        public List<IssueProfileArtifact> Artifacts { get; set; } = new();
        public List<IssueProfileMutationPlan> MutationPlans { get; set; } = new();
        public IssueProfileDiff Diff { get; set; } = new();
        public IssueProfilePackage Package { get; set; } = new();

        public void Normalize()
        {
            Checks ??= new();
            Artifacts ??= new();
            MutationPlans ??= new();
            Diff ??= new();
            Package ??= new();
            Package.Normalize();
        }
    }

    public sealed class IssueProfileCheck
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Severity { get; set; } = "warning";
    }

    public sealed class IssueProfileArtifact
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Severity { get; set; } = "warning";
    }

    public sealed class IssueProfileMutationPlan
    {
        public string Name { get; set; } = "";
        public string PlanPath { get; set; } = "";
    }

    public sealed class IssueProfileDiff
    {
        public string? Baseline { get; set; }
    }

    public sealed class IssueProfilePackage
    {
        public string? BundlePath { get; set; }
        public List<string> Commands { get; set; } = new();

        public void Normalize()
        {
            Commands ??= new();
        }
    }

    public sealed class IssuePreflightReport
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "issue-preflight-report.v1";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        [JsonPropertyName("profilePath")]
        public string ProfilePath { get; set; } = "";

        [JsonPropertyName("failOn")]
        public string FailOn { get; set; } = "error";

        [JsonPropertyName("success")]
        public bool Success => !ShouldFail;

        [JsonPropertyName("shouldFail")]
        public bool ShouldFail => ErrorCount > 0 || (string.Equals(FailOn, "warning", StringComparison.OrdinalIgnoreCase) && WarningCount > 0);

        [JsonPropertyName("checkCount")]
        public int CheckCount => Checks.Count;

        [JsonPropertyName("errorCount")]
        public int ErrorCount => Issues.Count(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

        [JsonPropertyName("warningCount")]
        public int WarningCount => Issues.Count(issue => string.Equals(issue.Severity, "warning", StringComparison.OrdinalIgnoreCase));

        [JsonPropertyName("noHiddenMutation")]
        public bool NoHiddenMutation => Issues.All(issue =>
            !string.Equals(issue.Code, "hidden-model-mutation", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issue.Code, "command-shell-operator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issue.Code, "command-parse-failed", StringComparison.OrdinalIgnoreCase));

        [JsonPropertyName("checks")]
        public List<IssuePreflightCheck> Checks { get; } = new();

        [JsonPropertyName("issues")]
        public List<IssueContractIssue> Issues { get; } = new();

        [JsonPropertyName("commandPaths")]
        public List<string> CommandPaths { get; } = new();
    }

    public sealed class IssuePreflightCheck
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";

        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    public sealed class IssueDiffReport
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "issue-diff-report.v1";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        [JsonPropertyName("fromPath")]
        public string FromPath { get; set; } = "";

        [JsonPropertyName("toPath")]
        public string ToPath { get; set; } = "";

        [JsonPropertyName("capturedCurrent")]
        public bool CapturedCurrent { get; set; }

        [JsonPropertyName("review")]
        public bool Review { get; set; }

        [JsonPropertyName("totalChanges")]
        public int TotalChanges { get; set; }

        [JsonPropertyName("highestSeverity")]
        public string HighestSeverity { get; set; } = "none";

        [JsonPropertyName("severityCounts")]
        public Dictionary<string, int> SeverityCounts { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("groups")]
        public List<DiffReviewGroup> Groups { get; set; } = new();

        [JsonPropertyName("recommendedActions")]
        public List<string> RecommendedActions { get; set; } = new();

        [JsonPropertyName("commandPaths")]
        public List<string> CommandPaths { get; set; } = new();

        public static IssueDiffReport From(SnapshotDiff diff, string fromPath, string toPath, bool capturedCurrent, bool review)
        {
            var reviewReport = DiffReviewRenderer.Build(diff);
            return new IssueDiffReport
            {
                FromPath = Path.GetFullPath(fromPath),
                ToPath = capturedCurrent ? "current" : Path.GetFullPath(toPath),
                CapturedCurrent = capturedCurrent,
                Review = review,
                TotalChanges = reviewReport.TotalChanges,
                HighestSeverity = reviewReport.HighestSeverity,
                SeverityCounts = reviewReport.SeverityCounts,
                Warnings = reviewReport.Warnings,
                Groups = review ? reviewReport.Groups : new List<DiffReviewGroup>(),
                RecommendedActions = review ? reviewReport.RecommendedActions : new List<string>(),
                CommandPaths = new List<string>
                {
                    $"revitcli issue diff --from {Quote(Path.GetFullPath(fromPath))} --to {(capturedCurrent ? "current" : Quote(Path.GetFullPath(toPath)))} --review --output markdown"
                }
            };
        }
    }

    public sealed class IssuePackageReport
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "issue-package-receipt.v1";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        [JsonPropertyName("writtenAtUtc")]
        public string? WrittenAtUtc { get; set; }

        [JsonPropertyName("success")]
        public bool Success => Valid && (DryRun || BundleWritten);

        [JsonPropertyName("valid")]
        public bool Valid => ErrorCount == 0;

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; }

        [JsonPropertyName("includeReceipts")]
        public bool IncludeReceipts { get; set; } = true;

        [JsonPropertyName("signJournal")]
        public bool SignJournal { get; set; }

        [JsonPropertyName("profilePath")]
        public string ProfilePath { get; set; } = "";

        [JsonPropertyName("projectDirectory")]
        public string ProjectDirectory { get; set; } = "";

        [JsonPropertyName("manifestPath")]
        public string ManifestPath { get; set; } = "";

        [JsonPropertyName("bundlePath")]
        public string BundlePath { get; set; } = "";

        [JsonPropertyName("bundleHash")]
        public string? BundleHash { get; set; }

        [JsonPropertyName("receiptPath")]
        public string ReceiptPath { get; set; } = "";

        [JsonPropertyName("journalSignaturePath")]
        public string? JournalSignaturePath { get; set; }

        [JsonPropertyName("bundleWritten")]
        public bool BundleWritten { get; set; }

        [JsonPropertyName("receiptWritten")]
        public bool ReceiptWritten { get; set; }

        [JsonPropertyName("preflightCheckCount")]
        public int PreflightCheckCount { get; set; }

        [JsonPropertyName("preflightErrorCount")]
        public int PreflightErrorCount { get; set; }

        [JsonPropertyName("preflightWarningCount")]
        public int PreflightWarningCount { get; set; }

        [JsonPropertyName("fileCount")]
        public int FileCount => Files.Count;

        [JsonPropertyName("receiptCount")]
        public int ReceiptCount => Files.Count(file => string.Equals(file.Kind, "receipt", StringComparison.OrdinalIgnoreCase));

        [JsonPropertyName("deliverableCount")]
        public int DeliverableCount => Files.Count(file => string.Equals(file.Kind, "deliverable", StringComparison.OrdinalIgnoreCase));

        [JsonPropertyName("errorCount")]
        public int ErrorCount => Issues.Count(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

        [JsonPropertyName("files")]
        public List<IssuePackageFile> Files { get; } = new();

        [JsonPropertyName("plannedActions")]
        public List<string> PlannedActions { get; } = new();

        [JsonPropertyName("issues")]
        public List<IssueContractIssue> Issues { get; } = new();

        [JsonPropertyName("commandPaths")]
        public List<string> CommandPaths { get; } = new();
    }

    public sealed class IssuePackageFile
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("sourcePath")]
        public string SourcePath { get; set; } = "";

        [JsonPropertyName("archivePath")]
        public string ArchivePath { get; set; } = "";

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("lineNumber")]
        public int? LineNumber { get; set; }
    }

    public sealed record IssueContractIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("command")] string? Command)
    {
        [JsonPropertyName("category")]
        public string Category => ResolveCategory(Code);

        [JsonPropertyName("remediation")]
        public string Remediation => ResolveRemediation(Code);

        [JsonPropertyName("safeRetry")]
        public bool SafeRetry => ResolveSafeRetry(Code);

        private static string ResolveCategory(string code) =>
            code.ToLowerInvariant() switch
            {
                "profile-schema-invalid" => "profile",
                "artifact-missing" => "artifact",
                "command-parse-failed" => "command",
                "command-shell-operator" => "command",
                "hidden-model-mutation" => "safety",
                "mutation-plan-missing" => "mutation-plan",
                "bundle-plan-failed" => "package",
                "bundle-exists" => "package",
                "bundle-write-failed" => "package",
                "journal-sign-failed" => "journal",
                var value when value.Contains("receipt", StringComparison.Ordinal) => "receipt",
                var value when value.Contains("manifest", StringComparison.Ordinal) => "deliverables",
                _ => "issue-contract"
            };

        private static string ResolveRemediation(string code) =>
            code.ToLowerInvariant() switch
            {
                "profile-schema-invalid" => "Use schemaVersion: issue-profile.v1.",
                "artifact-missing" => "Create the artifact or lower its profile severity to info if it is optional.",
                "command-parse-failed" => "Fix command quoting so issue preflight can parse and classify the command.",
                "command-shell-operator" => "Split chained shell commands into explicit issue profile checks so each command can be classified before execution.",
                "hidden-model-mutation" => "Generate an explicit mutation plan, review it, and reference it under mutationPlans before package/apply.",
                "mutation-plan-missing" => "Create the referenced plan file before issue preflight or package.",
                "bundle-plan-failed" => "Fix deliverables manifest or package path issues, then rerun the dry-run.",
                "bundle-exists" => "Choose a new bundle path or remove the existing bundle intentionally.",
                "bundle-write-failed" => "Fix package output permissions or path problems, then rerun package.",
                "journal-sign-failed" => "Fix local journal/signature inputs and rerun journal sign or package.",
                var value when value.Contains("receipt", StringComparison.Ordinal) => "Regenerate or restore the referenced receipt before packaging.",
                var value when value.Contains("manifest", StringComparison.Ordinal) => "Regenerate or verify the delivery manifest before packaging.",
                _ => "Review the issue details and rerun the command after correcting the local artifact."
            };

        private static bool ResolveSafeRetry(string code) =>
            code.ToLowerInvariant() switch
            {
                "artifact-missing" => true,
                "bundle-plan-failed" => true,
                "bundle-write-failed" => true,
                "journal-sign-failed" => true,
                var value when value.Contains("receipt", StringComparison.Ordinal) => true,
                var value when value.Contains("manifest", StringComparison.Ordinal) => true,
                _ => false
            };
    }
}
