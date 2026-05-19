namespace RevitCli.Tests.Scripts;

public sealed class Revit2026SmokeScriptTests
{
    [Fact]
    public void VersionedSmokeScript_ExposesSupportedVersionSwitchAndDoctorCheck()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("[ValidateSet(\"2024\", \"2025\", \"2026\")]", script);
        Assert.Contains("REVITCLI_REVIT${Version}_INSTALL_DIR", script);
        Assert.Contains("Revit${Version}InstallDir", script);
        Assert.Contains("@(\"doctor\", \"--check-version\", $Version)", script);
        Assert.Contains("Autodesk\\Revit\\Addins\\$Version\\RevitCli.addin", script);
    }

    [Fact]
    public void VersionedSmokeScript_ExposesV4WorkbenchLiveDiscoveryGate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("[switch]$V4Workbench", script);
        Assert.Contains("\"workbench\", \"verify\"", script);
        Assert.Contains("\"workbench\", \"handoff\"", script);
        Assert.Contains("\"inspect\", \"schedules\"", script);
        Assert.Contains("\"inspect\", \"sheets\"", script);
        Assert.Contains("\"schedule\", \"list\"", script);
        Assert.Contains("\"schedule\", \"export\"", script);
        Assert.Contains("v4Workbench = [bool]$V4Workbench", script);
    }

    [Fact]
    public void Legacy2026SmokeScript_UsesVersionCheckAndV4WorkbenchGate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        Assert.Contains("@(\"doctor\", \"--check-version\", \"2026\")", script);
        Assert.Contains("[switch]$V4Workbench", script);
        Assert.Contains("\"workbench\", \"verify\"", script);
        Assert.Contains("\"schedule\", \"export\"", script);
        Assert.Contains("v4Workbench = [bool]$V4Workbench", script);
    }

    [Fact]
    public void AddinProject_SelectsSingleTargetFrameworkPerRevitYear()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "RevitCli.Addin",
            "RevitCli.Addin.csproj"));

        Assert.Contains("$(RevitYear)' == '2024'\">net48</TargetFramework>", project);
        Assert.Contains("$(RevitYear)' != '2024'\">net8.0-windows</TargetFramework>", project);
        Assert.Contains("$(Revit2026InstallDir)</RevitInstallDir>", project);
        Assert.Contains("$(Revit2025InstallDir)</RevitInstallDir>", project);
        Assert.Contains("$(Revit2024InstallDir)</RevitInstallDir>", project);
        Assert.Contains("ValidateRevitApiReferences", project);
        Assert.Contains("Missing RevitAPI.dll", project);
        Assert.Contains("Missing RevitAPIUI.dll", project);
    }

    [Fact]
    public void VersionedSmokeScript_PreservesSingleElementJsonArrays()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("return ,@($value)", script);
    }

    [Fact]
    public void SmokeScripts_RetryTransientRevitCommunicationTimeouts()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            Assert.Contains("Test-TransientRevitCliFailure", script);
            Assert.Contains("Test-RevitCliSmokeCommandCanRetry", script);
            Assert.Contains("[int]$MaxAttempts = 2", script);
            Assert.Contains("HttpClient\\.Timeout", script);
            Assert.Contains("attempt = $attempt", script);
            Assert.Contains("retrySafe = $canRetry", script);
            Assert.Contains("\"set\" { return $CommandArgs -contains \"--dry-run\" }", script);
            Assert.Contains("\"rollback\" { return $false }", script);
        }
    }

    [Fact]
    public void FixApplyCompletionMessage_IsWrittenAfterReportWrite()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        var reportWrittenIndex = script.IndexOf("Smoke report written to $OutputPath", StringComparison.Ordinal);
        var completionIndex = script.IndexOf("Fix apply smoke completed. Review the report", StringComparison.Ordinal);

        Assert.True(reportWrittenIndex >= 0, "Smoke report write message was not found.");
        Assert.True(completionIndex >= 0, "Fix apply completion message was not found.");
        Assert.True(
            completionIndex > reportWrittenIndex,
            "Fix apply completion must not be printed before the smoke report is written.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "revitcli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
