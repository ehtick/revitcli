using System.Text.Json;
using RevitCli.Commands;

namespace RevitCli.Tests.Commands;

public sealed class ReleaseCommandTests : IDisposable
{
    private readonly string _root;

    public ReleaseCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-release-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    public async Task Verify_HealthyTreeJson_ReturnsSuccessAndSchema()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", "v2.3.0", strict: false, output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-verify.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("2.3.0", root.GetProperty("version").GetString());
        Assert.Equal("v2.3.0", root.GetProperty("tag").GetString());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:no-addin-build" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:release-verify" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:workbench-verify" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script:v4-workbench" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "release-workflow:no-addin-2025" &&
            check.GetProperty("status").GetString() == "ok");
    }

    [Fact]
    public async Task Verify_UbuntuCiMentionsAddin_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteFile(".github/workflows/ci.yml", """
name: CI
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test tests/RevitCli.Tests/RevitCli.Tests.csproj
      - run: dotnet build src/RevitCli.Addin
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "table", null, strict: false, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("ci:no-addin-build", output.ToString());
        Assert.Contains("FAIL", output.ToString());
    }

    [Fact]
    public async Task Verify_Markdown_PrintsReviewSections()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "markdown", "v2.3.0", strict: false, output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Release Verification", text);
        Assert.Contains("- Status: `PASS`", text);
        Assert.Contains("- Tag: `v2.3.0`", text);
        Assert.Contains("## Errors", text);
        Assert.Contains("- None.", text);
        Assert.Contains("## Passing Checks", text);
        Assert.Contains("| OK | ci:no-addin-build | .github/workflows/ci.yml | Ubuntu CI does not build the Windows/Revit add-in. |", text);
        Assert.Contains("| OK | ci:workbench-verify | .github/workflows/ci.yml | Ubuntu CI runs the v4 workbench contract verifier. |", text);
        Assert.Contains("| OK | smoke-script:v4-workbench | scripts/smoke-revit.ps1 | Real smoke script can run the v4 workbench and live discovery gate. |", text);
        Assert.Contains("## Gate Scope", text);
        Assert.Contains("Real Revit smoke remains a separate Windows/Revit checklist gate.", text);
    }

    [Fact]
    public async Task Verify_TagMismatch_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", "v2.4.0", strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var failed = json.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("id").GetString() == "version:tag-match");
        Assert.Equal("error", failed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Verify_UnknownOutputFormat_ReturnsFailureBeforeReadingFiles()
    {
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "yaml", null, strict: false, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown output format", output.ToString());
    }

    private static void WriteHealthyTree(string root)
    {
        WriteFile(root, "Directory.Build.props", """
<Project>
  <PropertyGroup>
    <RevitCliVersion>2.3.0</RevitCliVersion>
  </PropertyGroup>
</Project>
""");
        WriteFile(root, "CHANGELOG.md", """
# Changelog

## [Unreleased]

### Added - v2.3 inspect/discover

- Release integrity work.
""");
        WriteFile(root, "README.md", """
# RevitCli

See docs/release-checklist.md before release.
Update RevitCliVersion before tagging.
Use docs/revit2026-real-smoke.md for live smoke evidence.
git tag vX.Y.Z
""");
        WriteFile(root, "docs/release-checklist.md", "# Release Checklist");
        WriteFile(root, "docs/architect-terminal-vision.md", "# Vision");
        WriteFile(root, "docs/revit2026-real-smoke.md", "# Smoke\n\njournal evidence with V4Workbench");
        WriteFile(root, "docs/revit-version-compatibility.md", "2024 2025 2026");
        WriteFile(root, ".github/workflows/ci.yml", """
name: CI
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet restore shared/RevitCli.Shared/RevitCli.Shared.csproj
      - run: dotnet build src/RevitCli/RevitCli.csproj --no-restore
      - run: dotnet run --project src/RevitCli/RevitCli.csproj --no-build -- release verify --output json
      - run: dotnet run --project src/RevitCli/RevitCli.csproj --no-build -- workbench verify --dir . --output json
      - run: dotnet test tests/RevitCli.Tests/RevitCli.Tests.csproj --no-build
""");
        WriteFile(root, ".github/workflows/release.yml", """
on:
  push:
    tags:
      - "v*"
jobs:
  release:
    runs-on: [self-hosted, windows, revit2026]
    steps:
      - run: |
          $installDir = $env:REVITCLI_REVIT2026_INSTALL_DIR
          dotnet publish src/RevitCli.Addin -p:RevitYear=2026 "-p:RevitInstallDir=$installDir"
      - run: Get-FileHash ./revitcli-win-x64.zip | Set-Content SHA256SUMS.txt
      - uses: softprops/action-gh-release@v2
""");
        WriteFile(root, ".github/workflows/publish.yml", """
on:
  workflow_dispatch:
    inputs:
      tag:
        required: true
        type: string
jobs:
  publish:
    steps:
      - run: dotnet pack src/RevitCli
      - run: dotnet nuget push --api-key "${{ secrets.NUGET_API_KEY }}"
""");
        WriteFile(root, "scripts/install.ps1", """
param(
  [string]$Revit2024InstallDir,
  [string]$Revit2025InstallDir,
  [string]$Revit2026InstallDir
)
$staged = 'staged'
function Test-PathListContains { }
""");
        WriteFile(root, "scripts/smoke-revit.ps1", "2024 2025 2026 V4Workbench workbench\", \"verify schedule\", \"export");
    }

    private void WriteFile(string relativePath, string content) =>
        WriteFile(_root, relativePath, content);

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
