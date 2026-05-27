namespace RevitCli.Tests.Scripts;

public sealed class InstallScriptTests
{
    [Fact]
    public void SourceTreeInstall_ExposesPerYearRevitInstallDirOverrides()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "install.ps1"));

        Assert.Contains("[string[]]$RevitYears = @(\"2026\")", script);
        Assert.Contains("$RevitYears | ForEach-Object { $_ -split \",\" }", script);
        Assert.Contains("[string]$Revit2024InstallDir", script);
        Assert.Contains("[string]$Revit2025InstallDir", script);
        Assert.Contains("[string]$Revit2026InstallDir", script);
        Assert.Contains("[switch]$AllowRunningRevit", script);
        Assert.Contains("REVITCLI_REVIT${Year}_INSTALL_DIR", script);
        Assert.Contains("Revit${Year}InstallDir", script);
        Assert.Contains("return Get-FirstNonEmpty -Values @($Revit2024InstallDir, $revitCliEnv, $autodeskEnv)", script);
        Assert.Contains("return Get-FirstNonEmpty -Values @($Revit2025InstallDir, $revitCliEnv, $autodeskEnv)", script);
        Assert.Contains("return Get-FirstNonEmpty -Values @($Revit2026InstallDir, $RevitInstallDir, $revitCliEnv, $autodeskEnv)", script);
        Assert.Contains("$publishArgs += \"-p:RevitInstallDir=$revitInstallDirOverride\"", script);
        Assert.Contains("function Test-PathListContains", script);
        Assert.Contains("function Test-AddinPayloadMatches", script);
        Assert.Contains("function Write-RevitAddinManifest", script);
        Assert.Contains("Write-RevitAddinManifest -Path $manifestPath -AssemblyPath $assemblyPath", script);
        Assert.Contains("Installer will update CLI now and stage add-ins for next Revit restart.", script);
        Assert.Contains("staging Add-in for Revit $year", script);
        Assert.Contains("$stagedAssemblyPath = Join-Path $stagedAddinDir \"RevitCli.Addin.dll\"", script);
        Assert.Contains("Write-RevitAddinManifest -Path $manifestPath -AssemblyPath $stagedAssemblyPath", script);
        Assert.Contains("stagedRevitYears = $stagedYears", script);
        Assert.Contains("active after next Revit restart", script);
        Assert.DoesNotContain("$userPath -notlike \"*$BinDir*\"", script);
        Assert.DoesNotContain("if (-not $Force) {\r\n        $answer = Read-Host \"Continue anyway? (y/N)\"", script);
        Assert.DoesNotContain("($year -eq \"2026\") -and ($RevitInstallDir -ne \"\")", script);
    }

    [Fact]
    public void CurrentSourceRevit2026Handoff_InstallsAndPrintsWslVerification()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "install-current-source-revit2026.ps1"));

        Assert.Contains("install.ps1", script);
        Assert.Contains("-RevitYears", script);
        Assert.Contains("2026", script);
        Assert.Contains("-Revit2026InstallDir", script);
        Assert.Contains("-Force", script);
        Assert.Contains("-AllowRunningRevit", script);
        Assert.Contains("scripts/smoke-revit-wsl.sh --require-current-source", script);
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
