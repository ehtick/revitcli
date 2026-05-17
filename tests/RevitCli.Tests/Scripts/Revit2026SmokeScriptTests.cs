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
    public void VersionedSmokeScript_PreservesSingleElementJsonArrays()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("return ,@($value)", script);
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
