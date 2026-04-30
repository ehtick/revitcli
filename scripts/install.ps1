#Requires -Version 5.1
<#
.SYNOPSIS
    Install RevitCli (CLI + Revit Add-in) for the current user.
.DESCRIPTION
    Copies CLI binaries to %LocalAppData%\RevitCli\bin,
    add-in binaries per Revit year, generates manifests,
    and adds the CLI to PATH.
.PARAMETER RevitYears
    Revit years to install for (e.g. "2024,2025,2026" or 2024,2025,2026).
    Default: 2026.
.PARAMETER Configuration
    Build configuration to publish from source-tree mode.
.PARAMETER RevitInstallDir
    Legacy Revit 2026 install directory override for source-tree builds.
    Prefer Revit2026InstallDir for new scripts.
.PARAMETER Revit2024InstallDir
    Optional Revit 2024 install directory override for source-tree builds.
.PARAMETER Revit2025InstallDir
    Optional Revit 2025 install directory override for source-tree builds.
.PARAMETER Revit2026InstallDir
    Optional Revit 2026 install directory override for source-tree builds.
.PARAMETER SkipBuild
    In source-tree mode, use existing .artifacts/install outputs instead of publishing.
.PARAMETER Force
    Overwrite existing installation without prompting.
.PARAMETER AllowRunningRevit
    Attempt to replace live add-in files even when Revit is running.
    By default, running Revit sessions get CLI updates immediately and staged add-ins for next restart.
#>
param(
    [string[]]$RevitYears = @("2026"),
    [string]$Configuration = "Release",
    [string]$RevitInstallDir = "",
    [string]$Revit2024InstallDir = "",
    [string]$Revit2025InstallDir = "",
    [string]$Revit2026InstallDir = "",
    [switch]$SkipBuild,
    [switch]$Force,
    [switch]$AllowRunningRevit
)

$ErrorActionPreference = "Stop"
$SupportedYears = @("2024", "2025", "2026")

# ── Paths ────────────────────────────────────────────────────────
$InstallRoot  = Join-Path $env:LOCALAPPDATA "RevitCli"
$BinDir       = Join-Path $InstallRoot "bin"
$StagedRoot   = Join-Path $InstallRoot "staged"
$MetadataPath = Join-Path $InstallRoot "install.json"

$ScriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot     = Split-Path -Parent $ScriptDir
$ArtifactsRoot = Join-Path $RepoRoot ".artifacts\install"
$ScriptDirLeaf = Split-Path -Leaf $ScriptDir
$SourceTreeMode = ($ScriptDirLeaf -ieq "scripts") -and (Test-Path -LiteralPath (Join-Path $RepoRoot "revitcli.sln"))
$SrcBin       = if ($SourceTreeMode) { Join-Path $ArtifactsRoot "bin" } else { Join-Path $ScriptDir "bin" }
$SemVerPattern = '^(?:v)?(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-AddinTargetFramework {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Year
    )

    if ($Year -eq "2024") {
        return "net48"
    }

    return "net8.0-windows"
}

function Get-SourceAddinDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Year
    )

    if ($SourceTreeMode) {
        return (Join-Path $ArtifactsRoot "addin\$Year")
    }

    return (Join-Path $ScriptDir "addin\$Year")
}

function Get-FirstNonEmpty {
    param([string[]]$Values)

    foreach ($value in $Values) {
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return ""
}

function Get-RevitInstallDirOverride {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Year
    )

    $revitCliEnv = [Environment]::GetEnvironmentVariable("REVITCLI_REVIT${Year}_INSTALL_DIR")
    $autodeskEnv = [Environment]::GetEnvironmentVariable("Revit${Year}InstallDir")

    switch ($Year) {
        "2024" {
            return Get-FirstNonEmpty -Values @($Revit2024InstallDir, $revitCliEnv, $autodeskEnv)
        }
        "2025" {
            return Get-FirstNonEmpty -Values @($Revit2025InstallDir, $revitCliEnv, $autodeskEnv)
        }
        "2026" {
            return Get-FirstNonEmpty -Values @($Revit2026InstallDir, $RevitInstallDir, $revitCliEnv, $autodeskEnv)
        }
        default {
            return ""
        }
    }
}

function Get-NormalizedPathForComparison {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $pathText = $Path.Trim().Trim('"')
    try {
        $pathText = [System.IO.Path]::GetFullPath($pathText)
    } catch {
        # PATH can contain entries that are not valid filesystem paths.
    }

    $trimChars = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    return $pathText.TrimEnd($trimChars)
}

function Test-PathListContains {
    param(
        [string]$PathList,
        [string]$Path
    )

    $targetPath = Get-NormalizedPathForComparison -Path $Path
    if ($targetPath -eq "") {
        return $false
    }

    $entries = if ([string]::IsNullOrEmpty($PathList)) { @() } else { $PathList -split ";" }
    foreach ($entry in $entries) {
        $entryPath = Get-NormalizedPathForComparison -Path $entry
        if ($entryPath -ne "" -and [string]::Equals($entryPath, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-SafePathSegment {
    param([string]$Value)

    $segment = if ([string]::IsNullOrWhiteSpace($Value)) { "unknown" } else { $Value.Trim() }
    foreach ($char in [System.IO.Path]::GetInvalidFileNameChars()) {
        $segment = $segment.Replace($char, "-")
    }

    return $segment.Replace("+", "_").Replace(":", "-")
}

function Get-ManifestAssemblyPath {
    param([string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        return $null
    }

    try {
        $doc = [xml](Get-Content -Raw -LiteralPath $ManifestPath)
        $assembly = $doc.RevitAddIns.AddIn.Assembly
        if ([string]::IsNullOrWhiteSpace($assembly)) {
            return $null
        }

        if ([System.IO.Path]::IsPathRooted($assembly)) {
            return $assembly
        }

        return [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ManifestPath) $assembly))
    } catch {
        return $null
    }
}

function Test-AddinPayloadMatches {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    if (-not (Test-Path -LiteralPath $SourceDir) -or -not (Test-Path -LiteralPath $DestinationDir)) {
        return $false
    }

    $sourceRoot = [System.IO.Path]::GetFullPath($SourceDir)
    $destRoot = [System.IO.Path]::GetFullPath($DestinationDir)
    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object { $_.Extension -ine ".pdb" })
    if ($sourceFiles.Count -eq 0) {
        return $false
    }

    foreach ($sourceFile in $sourceFiles) {
        $relative = $sourceFile.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')
        $destFile = Join-Path $destRoot $relative
        if (-not (Test-Path -LiteralPath $destFile)) {
            return $false
        }

        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $destHash = (Get-FileHash -LiteralPath $destFile -Algorithm SHA256).Hash
        if ($sourceHash -ne $destHash) {
            return $false
        }
    }

    return $true
}

function Write-RevitAddinManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath
    )

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement("RevitAddIns")
        $writer.WriteStartElement("AddIn")
        $writer.WriteAttributeString("Type", "Application")
        $writer.WriteElementString("Name", "RevitCli")
        $writer.WriteElementString("Assembly", $AssemblyPath)
        $writer.WriteElementString("FullClassName", "RevitCli.Addin.RevitCliApp")
        $writer.WriteElementString("AddInId", "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")
        $writer.WriteElementString("VendorId", "RevitCli")
        $writer.WriteElementString("VendorDescription", "https://github.com/xiaodream551-a11y/revitcli")
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally {
        $writer.Close()
    }
}

function Copy-DirectoryFresh {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [Parameter(Mandatory = $true)]
        [string]$AllowedRoot
    )

    $resolvedAllowedRoot = [System.IO.Path]::GetFullPath($AllowedRoot)
    $resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
    if (-not $resolvedDestination.StartsWith($resolvedAllowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear destination outside install root: $Destination"
    }

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path "$Source\*" -Destination $Destination -Recurse -Force
}

function Test-RevitCliVersion {
    param([string]$Version)
    return ($Version -match $SemVerPattern)
}

function Get-RevitCliVersion {
    param([string]$ExePath)

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "RevitCli executable not found: $ExePath"
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $versionOutput = & $ExePath --version 2>&1
        $versionExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $text = ($versionOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($versionExitCode -ne 0) {
        throw "'$ExePath --version' exited $versionExitCode`: $text"
    }

    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^revitcli\s+(.+)$') {
            $version = $Matches[1].Trim()
            if (Test-RevitCliVersion -Version $version) {
                return $version
            }
            throw "RevitCli version is not valid SemVer: $version"
        }
    }

    throw "'$ExePath --version' did not return a 'revitcli <version>' line: $text"
}

function Publish-SourceTreePackage {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Years
    )

    if (-not $SourceTreeMode) {
        return
    }

    if ($SkipBuild) {
        Write-Host "Source-tree mode: skipping build; using $ArtifactsRoot" -ForegroundColor DarkGray
        return
    }

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsRoot)
    if (-not $resolvedArtifactsRoot.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear artifacts outside repository root: $ArtifactsRoot"
    }

    if (Test-Path -LiteralPath $ArtifactsRoot) {
        Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force
    }
    [System.IO.Directory]::CreateDirectory($ArtifactsRoot) | Out-Null

    Write-Host "Publishing CLI from source tree to $SrcBin ..." -ForegroundColor Green
    Invoke-DotNet -Arguments @(
        "publish",
        (Join-Path $RepoRoot "src\RevitCli\RevitCli.csproj"),
        "-c", $Configuration,
        "-o", $SrcBin
    )

    foreach ($year in $Years) {
        $srcAddinYear = Get-SourceAddinDir -Year $year
        $framework = Get-AddinTargetFramework -Year $year
        $publishArgs = @(
            "publish",
            (Join-Path $RepoRoot "src\RevitCli.Addin\RevitCli.Addin.csproj"),
            "-c", $Configuration,
            "-f", $framework,
            "-o", $srcAddinYear,
            "-p:RevitYear=$year"
        )

        $revitInstallDirOverride = Get-RevitInstallDirOverride -Year $year
        if ($revitInstallDirOverride -ne "") {
            Write-Host "Using Revit $year install dir override: $revitInstallDirOverride" -ForegroundColor DarkGray
            $publishArgs += "-p:RevitInstallDir=$revitInstallDirOverride"
        }

        Write-Host "Publishing Add-in for Revit $year from source tree to $srcAddinYear ..." -ForegroundColor Green
        Invoke-DotNet -Arguments $publishArgs
    }
}

# ── Pre-checks ──────────────────────────────────────────────────
Write-Host "RevitCli Installer" -ForegroundColor Cyan
Write-Host ""

# Determine which Revit years to install
if ($PSBoundParameters.ContainsKey("RevitYears")) {
    if (($null -eq $RevitYears) -or ($RevitYears.Count -eq 0) -or (($RevitYears | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -eq 0)) {
        Write-Host "ERROR: No Revit years specified." -ForegroundColor Red
        Write-Host "Supported years: $($SupportedYears -join ', ')"
        exit 1
    }

    $targetYears = @($RevitYears | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() })
    $emptyYearTokens = @($targetYears | Where-Object { $_ -eq "" })
    if ($emptyYearTokens.Count -gt 0) {
        Write-Host "ERROR: Empty Revit year token in -RevitYears '$($RevitYears -join ',')'." -ForegroundColor Red
        Write-Host "Use a comma-separated list like: 2026 or 2024,2025,2026"
        exit 1
    }
} elseif ($SourceTreeMode) {
    $targetYears = @("2026")
} else {
    $targetYears = @()
    foreach ($year in $SupportedYears) {
        $srcAddinYear = Get-SourceAddinDir -Year $year
        $srcAddinDll = Join-Path $srcAddinYear "RevitCli.Addin.dll"
        if (Test-Path -LiteralPath $srcAddinDll) {
            $targetYears += $year
        }
    }
    if ($targetYears.Count -eq 0) {
        Write-Host "ERROR: No packaged add-in DLLs found under addin/<year>/RevitCli.Addin.dll." -ForegroundColor Red
        exit 1
    }
    Write-Host "Detected add-in packages for: $($targetYears -join ', ')" -ForegroundColor DarkGray
}

$unsupportedYears = @($targetYears | Where-Object { $SupportedYears -notcontains $_ })
if ($unsupportedYears.Count -gt 0) {
    Write-Host "ERROR: Unsupported Revit year(s): $($unsupportedYears -join ', ')" -ForegroundColor Red
    Write-Host "Supported years: $($SupportedYears -join ', ')"
    exit 1
}

$revitProcess = Get-Process Revit -ErrorAction SilentlyContinue
$RevitRunning = [bool]$revitProcess
if ($RevitRunning) {
    if ($AllowRunningRevit) {
        Write-Host "WARNING: Revit is running. -AllowRunningRevit will attempt live add-in replacement; files may be locked." -ForegroundColor Yellow
    } else {
        Write-Host "Revit is running. Installer will update CLI now and stage add-ins for next Revit restart." -ForegroundColor Yellow
        Write-Host "Running Revit process IDs: $($revitProcess.Id -join ', ')" -ForegroundColor DarkGray
    }
}

Publish-SourceTreePackage -Years $targetYears

# Check source directories
if (-not (Test-Path -LiteralPath $SrcBin)) {
    Write-Host "ERROR: bin/ directory not found in install package." -ForegroundColor Red
    if ($SourceTreeMode) {
        Write-Host "Run without -SkipBuild, or build source-tree artifacts under $ArtifactsRoot."
    } else {
        Write-Host "Make sure you extracted the full ZIP archive."
    }
    exit 1
}

# Validate source add-in directories exist
foreach ($year in $targetYears) {
    $srcAddinYear = Get-SourceAddinDir -Year $year
    $srcAddinDll = Join-Path $srcAddinYear "RevitCli.Addin.dll"
    if (-not (Test-Path -LiteralPath $srcAddinDll)) {
        Write-Host "ERROR: RevitCli.Addin.dll not found for Revit $year in install package." -ForegroundColor Red
        if ($SourceTreeMode) {
            Write-Host "Run without -SkipBuild, or build source-tree artifacts under $ArtifactsRoot."
        }
        exit 1
    }
}

$sourceCliExe = Join-Path $SrcBin "RevitCli.exe"
try {
    $installedVersion = Get-RevitCliVersion -ExePath $sourceCliExe
} catch {
    Write-Host "ERROR: Failed to validate source CLI before modifying installation." -ForegroundColor Red
    Write-Host $_.Exception.Message
    exit 1
}

# Check for existing installation
if ((Test-Path $BinDir) -and -not $Force) {
    Write-Host "Existing installation found at $InstallRoot" -ForegroundColor Yellow
    $answer = Read-Host "Overwrite? (y/N)"
    if ($answer -ne "y") {
        Write-Host "Installation cancelled."
        exit 0
    }
}

# ── Install CLI ─────────────────────────────────────────────────
Write-Host "Installing CLI to $BinDir ..." -ForegroundColor Green
New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
Copy-Item -Path "$SrcBin\*" -Destination $BinDir -Recurse -Force

$installedCliExe = Join-Path $BinDir "RevitCli.exe"
if (-not (Test-Path -LiteralPath $installedCliExe)) {
    Write-Host "ERROR: Installed CLI executable not found at $installedCliExe" -ForegroundColor Red
    exit 1
}

# ── Install Add-in per year ─────────────────────────────────────
$installedYears = @()
$stagedYears = @()
$stagedAddins = @()

foreach ($year in $targetYears) {
    $srcAddinYear = Get-SourceAddinDir -Year $year
    $addinDir     = Join-Path $InstallRoot "addin\$year"
    $revitAddins  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $manifestPath = Join-Path $revitAddins "RevitCli.addin"
    $assemblyPath = Join-Path $addinDir "RevitCli.Addin.dll"

    Write-Host "Installing Add-in for Revit $year ..." -ForegroundColor Green

    $manifestAssemblyPath = Get-ManifestAssemblyPath -ManifestPath $manifestPath
    $activeAddinDir = if ([string]::IsNullOrWhiteSpace($manifestAssemblyPath)) {
        $addinDir
    } else {
        Split-Path -Parent $manifestAssemblyPath
    }

    if ($RevitRunning -and -not $AllowRunningRevit) {
        if (Test-AddinPayloadMatches -SourceDir $srcAddinYear -DestinationDir $activeAddinDir) {
            Write-Host "Add-in for Revit $year is already current; keeping loaded files unchanged." -ForegroundColor DarkGray
            $installedYears += $year
            continue
        }

        $stageSegment = "$(Get-Date -Format 'yyyyMMddHHmmss')-$(Get-SafePathSegment -Value $installedVersion)"
        $stagedAddinDir = Join-Path $StagedRoot "addin\$year\$stageSegment"
        $stagedAssemblyPath = Join-Path $stagedAddinDir "RevitCli.Addin.dll"

        Write-Host "Revit is running; staging Add-in for Revit $year to $stagedAddinDir ..." -ForegroundColor Yellow
        Copy-DirectoryFresh -Source $srcAddinYear -Destination $stagedAddinDir -AllowedRoot $InstallRoot

        # Revit only reads the manifest on startup, so this does not affect the
        # current session. The staged add-in becomes active on the next restart.
        New-Item -ItemType Directory -Path $revitAddins -Force | Out-Null
        Write-RevitAddinManifest -Path $manifestPath -AssemblyPath $stagedAssemblyPath

        $stagedYears += $year
        $stagedAddins += @{
            revitYear    = $year
            stagedDir    = $stagedAddinDir
            assemblyPath = $stagedAssemblyPath
            manifestPath = $manifestPath
        }
        continue
    }

    # Copy add-in binaries
    Copy-DirectoryFresh -Source $srcAddinYear -Destination $addinDir -AllowedRoot $InstallRoot

    # Generate Revit manifest
    New-Item -ItemType Directory -Path $revitAddins -Force | Out-Null
    Write-RevitAddinManifest -Path $manifestPath -AssemblyPath $assemblyPath
    $installedYears += $year
}

# ── Add to PATH ─────────────────────────────────────────────────
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if (-not (Test-PathListContains -PathList $userPath -Path $BinDir)) {
    Write-Host "Adding $BinDir to user PATH ..." -ForegroundColor Green
    $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $BinDir } else { "$userPath;$BinDir" }
    [Environment]::SetEnvironmentVariable("PATH", $newUserPath, "User")
    if (-not (Test-PathListContains -PathList $env:PATH -Path $BinDir)) {
        $env:PATH = if ([string]::IsNullOrWhiteSpace($env:PATH)) { $BinDir } else { "$env:PATH;$BinDir" }
    }
} else {
    Write-Host "PATH already contains $BinDir" -ForegroundColor DarkGray
}

# ── Write install metadata ──────────────────────────────────────
$allYears = @($installedYears + $stagedYears | Select-Object -Unique)
$metadata = @{
    version      = $installedVersion
    revitYears   = $allYears
    activeRevitYears = $installedYears
    stagedRevitYears = $stagedYears
    stagedAddins = $stagedAddins
    binDir       = $BinDir
    installRoot  = $InstallRoot
    timestamp    = (Get-Date -Format "o")
} | ConvertTo-Json -Depth 5
Set-Content -Path $MetadataPath -Value $metadata -Encoding UTF8

# ── Done ────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "  Revit years: $($allYears -join ', ')"
Write-Host "  CLI:         $BinDir"
if ($stagedYears.Count -gt 0) {
    Write-Host "  Staged add-ins: $($stagedYears -join ', ') (active after next Revit restart)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
if ($stagedYears.Count -gt 0) {
    Write-Host "  1. Keep using the current Revit session if you only need CLI changes."
    Write-Host "  2. Restart Revit when convenient to activate the staged add-in."
    Write-Host "  3. Open a NEW terminal and run:"
} else {
    Write-Host "  1. Start (or restart) Revit"
    Write-Host "  2. Open a project"
    Write-Host "  3. Open a NEW terminal and run:"
}
Write-Host "       revitcli doctor" -ForegroundColor White
Write-Host "       revitcli status" -ForegroundColor White
