using RevitCli.Team;

namespace RevitCli.Tests.Team;

public sealed class TeamPolicyValidatorTests : IDisposable
{
    private readonly string _root;

    public TeamPolicyValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-team-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Validate_HealthyTeamPolicy_ReturnsValid()
    {
        var path = WritePolicy(HealthyPolicy());

        var report = TeamPolicyValidator.Validate(path, _root);

        Assert.True(report.Valid, string.Join("; ", report.Issues.Select(issue => issue.Message)));
        Assert.Equal("team-policy-validation.v1", report.SchemaVersion);
        Assert.Equal(Path.GetFullPath(path), report.PolicyPath);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Validate_DisabledLocalBoundary_ReturnsError()
    {
        var path = WritePolicy(HealthyPolicy().Replace("noSaaS: true", "noSaaS: false", StringComparison.Ordinal));

        var report = TeamPolicyValidator.Validate(path, _root);

        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue => issue.Code == "no-saas-required");
    }

    [Fact]
    public void Validate_RetentionBelowMinimum_ReturnsError()
    {
        var path = WritePolicy(HealthyPolicy()
            .Replace("days: 180", "days: 7", StringComparison.Ordinal)
            .Replace("maxFiles: 5000", "maxFiles: 10", StringComparison.Ordinal));

        var report = TeamPolicyValidator.Validate(path, _root);

        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue => issue.Code == "receipt-retention-days");
        Assert.Contains(report.Issues, issue => issue.Code == "receipt-retention-max-files");
    }

    [Fact]
    public void Validate_MissingRequiredCommand_ReturnsError()
    {
        var path = WritePolicy(HealthyPolicy().Replace("  - journal verify --dir . --output json\n", "", StringComparison.Ordinal));

        var report = TeamPolicyValidator.Validate(path, _root);

        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "required-commands-missing" &&
            issue.Message.Contains("journal verify", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UnsupportedInstallYear_ReturnsError()
    {
        var path = WritePolicy(HealthyPolicy().Replace("    - 2026", "    - 2027", StringComparison.Ordinal));

        var report = TeamPolicyValidator.Validate(path, _root);

        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue => issue.Code == "install-2026-required");
        Assert.Contains(report.Issues, issue => issue.Code == "install-years-unsupported");
    }

    [Fact]
    public void Validate_MissingSupportTemplate_ReturnsError()
    {
        var path = WritePolicy(HealthyPolicy(), writeSupportTemplates: false);

        var report = TeamPolicyValidator.Validate(path, _root);

        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "support-error-report" &&
            issue.Message.Contains("missing or unreadable", StringComparison.OrdinalIgnoreCase));
    }

    private string WritePolicy(string content, bool writeSupportTemplates = true)
    {
        if (writeSupportTemplates)
            WriteSupportTemplates();

        var path = Path.Combine(_root, "team-policy.yml");
        File.WriteAllText(path, content);
        return path;
    }

    private void WriteSupportTemplates()
    {
        WriteFile("docs/smoke/v5.6/install-postmortem-template.md", "# install postmortem\n");
        WriteFile("docs/v5-demo-and-pilot-playbook.md", "# pilot playbook\n");
        WriteFile("docs/smoke/v5.6/support-error-report-template.md", "# support report\n");
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string HealthyPolicy() =>
        """
schemaVersion: team-policy.v1
name: team-pilot
boundaries:
  localFirst: true
  terminalFirst: true
  dryRunFirst: true
  noSaaS: true
  noMcp: true
  noBuiltInLlm: true
  noDashboardCentral: true
install:
  revitYears:
    - 2024
    - 2025
    - 2026
receiptRetention:
  days: 180
  maxFiles: 5000
  paths:
    - .revitcli/receipts
    - .revitcli/workflows/receipts
    - .revitcli/journal.jsonl
requiredCommands:
  - doctor --output json
  - workbench verify --contract workbench-contract.v2 --dir . --output json
  - release verify --strict --output json
  - standards validate --output json
  - journal verify --dir . --output json
support:
  installPostmortemTemplate: docs/smoke/v5.6/install-postmortem-template.md
  userInterviewChecklist: docs/v5-demo-and-pilot-playbook.md
  errorReportTemplate: docs/smoke/v5.6/support-error-report-template.md
""";
}
