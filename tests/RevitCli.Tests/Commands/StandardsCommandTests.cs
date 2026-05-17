using System.Text.Json;
using RevitCli.Commands;

namespace RevitCli.Tests.Commands;

public sealed class StandardsCommandTests : IDisposable
{
    private readonly string _root;

    public StandardsCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-standards-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CreateValidProject(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Validate_CleanManifest_ReturnsZero()
    {
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "table", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Standards validation", text);
        Assert.Contains("Pack version: 2026.4.0", text);
        Assert.Contains("Compatibility: RevitCli >=0.1.0; Revit 2024, 2025, 2026", text);
        Assert.Contains("Status: OK", text);
        Assert.Contains("No issues.", text);
    }

    [Fact]
    public async Task Validate_JsonOutput_EmitsReport()
    {
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "json", output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal("office", root.GetProperty("name").GetString());
        Assert.Equal("2026.4.0", root.GetProperty("packVersion").GetString());
        Assert.Equal(">=0.1.0", root.GetProperty("compatibility").GetProperty("revitCli").GetString());
        Assert.Equal(3, root.GetProperty("compatibility").GetProperty("revitYears").GetArrayLength());
        Assert.Empty(root.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task Validate_MarkdownOutput_PrintsHandoffReport()
    {
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "markdown", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Standards Validation", text);
        Assert.Contains("- Status: `OK`", text);
        Assert.Contains("Pack version", text);
        Assert.Contains("## Issues", text);
        Assert.Contains("- None.", text);
    }

    [Fact]
    public async Task Validate_MissingPackMetadata_ReturnsWarningsButPasses()
    {
        WriteStandards("""
version: 1
name: office
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty]
""");
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "json", output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.True(document.RootElement.GetProperty("valid").GetBoolean());
        var issues = document.RootElement.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Contains(issues, issue => issue.GetProperty("path").GetString() == "packVersion");
        Assert.Contains(issues, issue => issue.GetProperty("path").GetString() == "compatibility.revitCli");
    }

    [Fact]
    public async Task Validate_IncompatibleCliVersion_ReturnsFailure()
    {
        WriteStandards("""
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=999.0.0"
  revitYears: [2024, 2025, 2026]
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty]
""");
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("compatibility.revitCli", output.ToString());
        Assert.Contains("requires RevitCli >=999.0.0", output.ToString());
    }

    [Fact]
    public async Task Validate_MissingWorkflow_ReturnsFailureWithRequirementPath()
    {
        File.Delete(Path.Combine(_root, ".revitcli", "workflows", "pre-issue.yml"));
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Status: FAIL", output.ToString());
        Assert.Contains("required.workflows[0]", output.ToString());
        Assert.Contains("workflow not found: pre-issue", output.ToString());
    }

    [Fact]
    public async Task Validate_UnknownFamilyRule_ReturnsFailure()
    {
        WriteStandards("""
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Portable standards pack for CLI-only validation.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [missing-rule]
""");
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("required.familyRules[0]", output.ToString());
        Assert.Contains("unknown built-in family rule", output.ToString());
    }

    [Fact]
    public async Task Validate_MissingScheduleTemplate_ReturnsFailure()
    {
        WriteStandards("""
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Portable standards pack for CLI-only validation.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [windows]
  familyRules: [name-non-empty]
""");
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteValidateAsync(null, _root, "json", output);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var issues = document.RootElement.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Contains(
            issues,
            issue => issue.GetProperty("path").GetString() == "required.scheduleTemplates[0]" &&
                     issue.GetProperty("message").GetString()!.Contains("windows"));
    }

    [Fact]
    public async Task Install_LocalPack_DryRunShowsPlanWithoutWriting()
    {
        var source = Path.Combine(_root, "source-pack");
        var project = Path.Combine(_root, "install-target-dry-run");
        Directory.CreateDirectory(project);
        CreateStandardsPack(source);
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteInstallAsync(
            source,
            project,
            refSpec: null,
            subPath: null,
            force: false,
            dryRun: true,
            outputFormat: "table",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Standards install plan", text);
        Assert.Contains("CREATE", text);
        Assert.False(File.Exists(Path.Combine(project, ".revitcli", "standards.yml")));
        Assert.False(File.Exists(Path.Combine(project, ".revitcli.yml")));
    }

    [Fact]
    public async Task Install_MarkdownDryRun_PrintsReviewablePlan()
    {
        var source = Path.Combine(_root, "source-pack-markdown");
        var project = Path.Combine(_root, "install-target-markdown");
        Directory.CreateDirectory(project);
        CreateStandardsPack(source);
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteInstallAsync(
            source,
            project,
            refSpec: null,
            subPath: null,
            force: false,
            dryRun: true,
            outputFormat: "markdown",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Standards Install Plan", text);
        Assert.Contains("## Changes", text);
        Assert.Contains("`CREATE`", text);
        Assert.False(File.Exists(Path.Combine(project, ".revitcli", "standards.yml")));
    }

    [Fact]
    public async Task Install_LocalPack_CopiesGovernedFilesAndValidates()
    {
        var source = Path.Combine(_root, "source-pack");
        var project = Path.Combine(_root, "install-target");
        Directory.CreateDirectory(project);
        CreateStandardsPack(source);
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteInstallAsync(
            source,
            project,
            refSpec: null,
            subPath: null,
            force: false,
            dryRun: false,
            outputFormat: "table",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(project, ".revitcli", "standards.yml")));
        Assert.True(File.Exists(Path.Combine(project, ".revitcli.yml")));
        Assert.True(File.Exists(Path.Combine(project, ".revitcli", "workflows", "pre-issue.yml")));
        Assert.True(Directory.Exists(Path.Combine(project, "deliverables")));
        Assert.Contains("Validation: OK", text);
    }

    [Fact]
    public async Task Install_ExistingFileRequiresForce()
    {
        var source = Path.Combine(_root, "source-pack");
        var project = Path.Combine(_root, "install-target-existing");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, ".revitcli.yml"), "version: 1\n");
        CreateStandardsPack(source);
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteInstallAsync(
            source,
            project,
            refSpec: null,
            subPath: null,
            force: false,
            dryRun: false,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("--force", output.ToString());
    }

    [Fact]
    public async Task Install_ForceOverwritesExistingFiles()
    {
        var source = Path.Combine(_root, "source-pack");
        var project = Path.Combine(_root, "install-target-force");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, ".revitcli.yml"), "version: 1\nchecks: {}\n");
        CreateStandardsPack(source);
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteInstallAsync(
            source,
            project,
            refSpec: null,
            subPath: null,
            force: true,
            dryRun: false,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.Contains("failOn: error", File.ReadAllText(Path.Combine(project, ".revitcli.yml")));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.True(document.RootElement.GetProperty("validation").GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Install_LocalSubPath_InstallsNestedPack()
    {
        var source = Path.Combine(_root, "source-with-nested-pack");
        var nested = Path.Combine(source, "packs", "office");
        var project = Path.Combine(_root, "install-target-subpath");
        Directory.CreateDirectory(project);
        CreateStandardsPack(nested);
        var output = new StringWriter();

        var exitCode = await StandardsCommand.ExecuteInstallAsync(
            source,
            project,
            refSpec: null,
            subPath: "packs/office",
            force: false,
            dryRun: false,
            outputFormat: "table",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(project, ".revitcli", "standards.yml")));
        Assert.Contains("Validation: OK", output.ToString());
    }

    private static void CreateValidProject(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".revitcli", "workflows"));
        Directory.CreateDirectory(Path.Combine(root, "deliverables"));
        File.WriteAllText(Path.Combine(root, ".revitcli.yml"), """
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  issue:
    precheck: default
    presets: [dwg]
schedules:
  doors:
    category: doors
    fields: [Mark, Fire Rating]
    name: Door Schedule
""");
        File.WriteAllText(Path.Combine(root, ".revitcli", "workflows", "pre-issue.yml"), """
version: 1
name: pre-issue
steps:
  - run: revitcli check issue --output table
    mode: read-only
  - run: revitcli publish issue --dry-run
    mode: dry-run
""");
        WriteDefaultStandards(root);
    }

    private void WriteStandards(string yaml) =>
        File.WriteAllText(Path.Combine(_root, ".revitcli", "standards.yml"), yaml);

    private static void WriteDefaultStandards(string root) =>
        File.WriteAllText(Path.Combine(root, ".revitcli", "standards.yml"), """
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Portable standards pack for CLI-only validation.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty, category-known]
""");

    private static void CreateStandardsPack(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".revitcli", "workflows"));
        File.WriteAllText(Path.Combine(root, ".revitcli.yml"), """
version: 1
checks:
  default:
    failOn: error
exports:
  dwg:
    format: dwg
publish:
  issue:
    precheck: default
    presets: [dwg]
schedules:
  doors:
    category: doors
    fields: [Mark, Fire Rating]
    name: Door Schedule
""");
        File.WriteAllText(Path.Combine(root, ".revitcli", "workflows", "pre-issue.yml"), """
version: 1
name: pre-issue
steps:
  - run: revitcli check issue --output table
    mode: read-only
  - run: revitcli publish issue --dry-run
    mode: dry-run
""");
        File.WriteAllText(Path.Combine(root, ".revitcli", "standards.yml"), """
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Portable standards pack for CLI-only validation.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty, category-known]
""");
    }
}
