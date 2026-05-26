using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Team;

internal static class TeamPolicyValidator
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static TeamPolicyValidationReport Validate(string policyPath, string? projectRoot = null)
    {
        var fullPath = Path.GetFullPath(policyPath);
        var root = string.IsNullOrWhiteSpace(projectRoot)
            ? InferProjectRoot(fullPath)
            : Path.GetFullPath(projectRoot);
        var issues = new List<TeamPolicyValidationIssue>();
        TeamPolicyDocument? policy = null;

        try
        {
            if (!File.Exists(fullPath))
            {
                issues.Add(Error("policy-missing", fullPath, "Team policy file is missing."));
                return new TeamPolicyValidationReport("team-policy-validation.v1", fullPath, false, issues);
            }

            policy = Yaml.Deserialize<TeamPolicyDocument>(File.ReadAllText(fullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            issues.Add(Error("policy-invalid-yaml", fullPath, $"Team policy YAML could not be read: {ex.Message}"));
            return new TeamPolicyValidationReport("team-policy-validation.v1", fullPath, false, issues);
        }

        if (policy == null)
        {
            issues.Add(Error("policy-empty", fullPath, "Team policy file is empty."));
            return new TeamPolicyValidationReport("team-policy-validation.v1", fullPath, false, issues);
        }

        if (!string.Equals(policy.SchemaVersion, "team-policy.v1", StringComparison.OrdinalIgnoreCase))
            issues.Add(Error("schema-version", fullPath, "Team policy schemaVersion must be team-policy.v1."));

        AddBoundaryIssues(policy.Boundaries, fullPath, issues);
        AddInstallIssues(policy.Install, fullPath, issues);
        AddReceiptRetentionIssues(policy.ReceiptRetention, fullPath, issues);
        AddRequiredCommandIssues(policy.RequiredCommands, fullPath, issues);
        AddSupportIssues(policy.Support, fullPath, root, issues);

        return new TeamPolicyValidationReport("team-policy-validation.v1", fullPath, issues.Count == 0, issues);
    }

    private static void AddBoundaryIssues(
        TeamPolicyBoundaries? boundaries,
        string path,
        List<TeamPolicyValidationIssue> issues)
    {
        if (boundaries == null)
        {
            issues.Add(Error("boundaries-missing", path, "Team policy must declare local-first boundaries."));
            return;
        }

        if (!boundaries.LocalFirst)
            issues.Add(Error("local-first-required", path, "Team policy must keep localFirst=true."));
        if (!boundaries.TerminalFirst)
            issues.Add(Error("terminal-first-required", path, "Team policy must keep terminalFirst=true."));
        if (!boundaries.DryRunFirst)
            issues.Add(Error("dry-run-first-required", path, "Team policy must keep dryRunFirst=true."));
        if (!boundaries.NoSaaS)
            issues.Add(Error("no-saas-required", path, "Team policy must keep noSaaS=true for v5.6."));
        if (!boundaries.NoMcp)
            issues.Add(Error("no-mcp-required", path, "Team policy must keep noMcp=true for v5.6."));
        if (!boundaries.NoBuiltInLlm)
            issues.Add(Error("no-built-in-llm-required", path, "Team policy must keep noBuiltInLlm=true for v5.6."));
        if (!boundaries.NoDashboardCentral)
            issues.Add(Error("no-dashboard-central-required", path, "Team policy must keep noDashboardCentral=true for v5.6."));
    }

    private static void AddInstallIssues(
        TeamPolicyInstall? install,
        string path,
        List<TeamPolicyValidationIssue> issues)
    {
        if (install == null)
        {
            issues.Add(Error("install-missing", path, "Team policy must declare supported Revit years."));
            return;
        }

        var years = (install.RevitYears ?? new List<int>()).Distinct().OrderBy(year => year).ToArray();
        if (years.Length == 0)
            issues.Add(Error("install-years-missing", path, "Team policy install.revitYears must not be empty."));
        if (!years.Contains(2026))
            issues.Add(Error("install-2026-required", path, "Team pilot policy must include Revit 2026 as the first live support year."));

        var unsupported = years.Where(year => year is not (2024 or 2025 or 2026)).ToArray();
        if (unsupported.Length > 0)
            issues.Add(Error("install-years-unsupported", path, $"Unsupported Revit year(s): {string.Join(", ", unsupported)}."));
    }

    private static void AddReceiptRetentionIssues(
        TeamReceiptRetention? retention,
        string path,
        List<TeamPolicyValidationIssue> issues)
    {
        if (retention == null)
        {
            issues.Add(Error("receipt-retention-missing", path, "Team policy must declare receipt retention."));
            return;
        }

        if (retention.Days < 30)
            issues.Add(Error("receipt-retention-days", path, "receiptRetention.days must be at least 30."));
        if (retention.MaxFiles < 100)
            issues.Add(Error("receipt-retention-max-files", path, "receiptRetention.maxFiles must be at least 100."));

        var requiredPaths = new[]
        {
            ".revitcli/receipts",
            ".revitcli/workflows/receipts",
            ".revitcli/journal.jsonl"
        };
        var paths = (retention.Paths ?? new List<string>())
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requiredPaths
            .Where(required => !paths.Contains(NormalizePath(required)))
            .ToArray();
        if (missing.Length > 0)
            issues.Add(Error("receipt-retention-paths", path, $"receiptRetention.paths is missing: {string.Join(", ", missing)}."));
    }

    private static void AddRequiredCommandIssues(
        List<string> commands,
        string path,
        List<TeamPolicyValidationIssue> issues)
    {
        var required = new[]
        {
            "doctor --output json",
            "workbench verify --contract workbench-contract.v2 --dir . --output json",
            "release verify --strict --output json",
            "standards validate --output json",
            "journal verify --dir . --output json"
        };
        var commandSet = (commands ?? new List<string>())
            .Select(NormalizeCommand)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = required
            .Where(command => !commandSet.Contains(NormalizeCommand(command)))
            .ToArray();
        if (missing.Length > 0)
            issues.Add(Error("required-commands-missing", path, $"requiredCommands is missing: {string.Join(", ", missing)}."));
    }

    private static void AddSupportIssues(
        TeamSupportPolicy? support,
        string path,
        string projectRoot,
        List<TeamPolicyValidationIssue> issues)
    {
        if (support == null)
        {
            issues.Add(Error("support-missing", path, "Team policy must declare support evidence paths."));
            return;
        }

        AddRequiredSupportPath(
            support.InstallPostmortemTemplate,
            "docs/smoke/v5.6/install-postmortem-template.md",
            "support-install-postmortem",
            "support.installPostmortemTemplate",
            path,
            projectRoot,
            issues);
        AddRequiredSupportPath(
            support.UserInterviewChecklist,
            "docs/v5-demo-and-pilot-playbook.md",
            "support-user-interview",
            "support.userInterviewChecklist",
            path,
            projectRoot,
            issues);
        AddRequiredSupportPath(
            support.ErrorReportTemplate,
            "docs/smoke/v5.6/support-error-report-template.md",
            "support-error-report",
            "support.errorReportTemplate",
            path,
            projectRoot,
            issues);
    }

    private static void AddRequiredSupportPath(
        string? actual,
        string required,
        string code,
        string field,
        string path,
        string projectRoot,
        List<TeamPolicyValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            issues.Add(Error(code, path, $"{field} is required and must be {required}."));
            return;
        }

        if (!string.Equals(NormalizePath(actual), NormalizePath(required), StringComparison.OrdinalIgnoreCase))
            issues.Add(Error(code, path, $"{field} must be {required}."));

        var fullTemplatePath = Path.Combine(projectRoot, ToNativePath(required));
        if (!CanReadFile(fullTemplatePath, out var reason))
            issues.Add(Error(code, path, $"{field} points to {required}, but that file is missing or unreadable under the project root: {reason}."));
    }

    private static string InferProjectRoot(string fullPolicyPath)
    {
        var directory = new FileInfo(fullPolicyPath).Directory;
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs")) ||
                Directory.Exists(Path.Combine(directory.FullName, "profiles")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "README.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetDirectoryName(fullPolicyPath) ?? Directory.GetCurrentDirectory();
    }

    private static bool CanReadFile(string path, out string reason)
    {
        try
        {
            if (!File.Exists(path))
            {
                reason = "file does not exist";
                return false;
            }

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            reason = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static string ToNativePath(string path) =>
        NormalizePath(path).Replace('/', Path.DirectorySeparatorChar);

    private static TeamPolicyValidationIssue Error(string code, string path, string message) =>
        new("error", code, path, message);

    private static string NormalizePath(string value) =>
        (value ?? "").Trim().Replace('\\', '/').TrimEnd('/');

    private static string NormalizeCommand(string value) =>
        string.Join(" ", (value ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

internal sealed class TeamPolicyDocument
{
    public string SchemaVersion { get; set; } = "";
    public string Name { get; set; } = "";
    public TeamPolicyBoundaries? Boundaries { get; set; }
    public TeamPolicyInstall? Install { get; set; }
    public TeamReceiptRetention? ReceiptRetention { get; set; }
    public List<string> RequiredCommands { get; set; } = new();
    public TeamSupportPolicy? Support { get; set; }
}

internal sealed class TeamPolicyBoundaries
{
    public bool LocalFirst { get; set; }
    public bool TerminalFirst { get; set; }
    public bool DryRunFirst { get; set; }
    public bool NoSaaS { get; set; }
    public bool NoMcp { get; set; }
    public bool NoBuiltInLlm { get; set; }
    public bool NoDashboardCentral { get; set; }
}

internal sealed class TeamPolicyInstall
{
    public List<int> RevitYears { get; set; } = new();
}

internal sealed class TeamReceiptRetention
{
    public int Days { get; set; }
    public int MaxFiles { get; set; }
    public List<string> Paths { get; set; } = new();
}

internal sealed class TeamSupportPolicy
{
    public string InstallPostmortemTemplate { get; set; } = "";
    public string UserInterviewChecklist { get; set; } = "";
    public string ErrorReportTemplate { get; set; } = "";
}

internal sealed record TeamPolicyValidationReport(
    string SchemaVersion,
    string PolicyPath,
    bool Valid,
    IReadOnlyList<TeamPolicyValidationIssue> Issues);

internal sealed record TeamPolicyValidationIssue(
    string Severity,
    string Code,
    string Path,
    string Message);
