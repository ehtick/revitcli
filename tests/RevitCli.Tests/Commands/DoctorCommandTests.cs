using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Config;
using RevitCli.Diagnostics;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class DoctorCommandTests
{
    /// <summary>
    /// Restores a process env var to its prior value (or removes it if it
    /// wasn't set) when disposed. Lets tests mutate
    /// <c>Revit&lt;year&gt;InstallDir</c> / <c>REVITCLI_REVIT&lt;year&gt;_INSTALL_DIR</c>
    /// without leaking into other tests.
    /// </summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvVarScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }

    private static DoctorEnvironment CreateDoctorEnvironment(int revitYear = 2026)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli_doctor_{System.Guid.NewGuid():N}");
        var userProfile = Path.Combine(root, "user");
        var appData = Path.Combine(root, "appdata");
        var revitDir = Path.Combine(root, $"Revit {revitYear}");
        Directory.CreateDirectory(userProfile);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(revitDir);
        File.WriteAllText(Path.Combine(revitDir, "RevitAPI.dll"), "");
        File.WriteAllText(Path.Combine(revitDir, "RevitAPIUI.dll"), "");

        var environment = new DoctorEnvironment
        {
            UserProfile = userProfile,
            AppData = appData,
            TargetRevitYear = revitYear,
            RevitInstallDir = revitDir,
            Revit2026InstallDir = revitYear == 2026 ? revitDir : null
        };

        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        return environment;
    }

    private static string CurrentCliAssemblyPath() =>
        typeof(DoctorCommand).Assembly.Location;

    private static void WriteAddinManifest(DoctorEnvironment environment, string assemblyPath)
    {
        var addins = Path.GetDirectoryName(environment.ManifestPath)!;
        Directory.CreateDirectory(addins);
        File.WriteAllText(environment.ManifestPath,
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Assembly>{assemblyPath}</Assembly>
  </AddIn>
</RevitAddIns>");
    }

    private static DoctorEnvironment WithCliVersion(DoctorEnvironment environment, string cliVersion)
    {
        return new DoctorEnvironment
        {
            UserProfile = environment.UserProfile,
            AppData = environment.AppData,
            TargetRevitYear = environment.TargetRevitYear,
            RevitInstallDir = environment.RevitInstallDir,
            Revit2026InstallDir = environment.Revit2026InstallDir,
            CliVersion = cliVersion
        };
    }

    [Fact]
    public async Task Execute_ServerDown_PrintsFail()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var config = new CliConfig();
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, config, writer, CreateDoctorEnvironment());

        var output = writer.ToString();
        Assert.Contains("FAIL", output);
        Assert.Contains("Server URL", output);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Execute_ServerUp_PrintsOk()
    {
        var environment = CreateDoctorEnvironment();
        var status = new StatusInfo { RevitVersion = "2026", RevitYear = 2026, AddinVersion = environment.CliVersion, DocumentName = "Test.rvt" };
        var response = ApiResponse<StatusInfo>.Ok(status);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var config = new CliConfig();
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, config, writer, environment);

        var output = writer.ToString();
        Assert.Contains("Connected to Revit 2026", output);
        Assert.Contains("Test.rvt", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Execute_CheckVersion2025_UsesVersionSpecificInstallAndManifest()
    {
        var environment = CreateDoctorEnvironment(2025);
        var status = new StatusInfo
        {
            RevitVersion = "2025",
            RevitYear = 2025,
            AddinVersion = environment.CliVersion,
            DocumentName = "Test.rvt"
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Revit 2025 API DLLs found", output);
        Assert.Contains(Path.Combine("Addins", "2025", "RevitCli.addin"), output);
        Assert.Contains("Connected to Revit 2025", output);
    }

    [Fact]
    public async Task Execute_UnsupportedCheckVersion_ReturnsFailureBeforePrechecks()
    {
        var baseline = CreateDoctorEnvironment();
        var environment = new DoctorEnvironment
        {
            UserProfile = baseline.UserProfile,
            AppData = baseline.AppData,
            TargetRevitYear = 2023,
            RevitInstallDir = baseline.RevitInstallDir,
            Revit2026InstallDir = baseline.Revit2026InstallDir,
            CliVersion = baseline.CliVersion
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(
            new StatusInfo { RevitVersion = "2023", RevitYear = 2023 })));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported Revit version 2023", writer.ToString());
    }

    [Fact]
    public async Task Execute_LiveAddinMajorMinorMismatch_ReturnsFailure()
    {
        var environment = CreateDoctorEnvironment();
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "1.0.0",
            DocumentName = "Test.rvt"
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Live Add-in version", writer.ToString());
        Assert.Contains("does not match CLI", writer.ToString());
    }

    [Fact]
    public async Task Execute_LiveAddinPatchMismatch_WarnsButDoesNotFail()
    {
        var environment = WithCliVersion(CreateDoctorEnvironment(), "1.3.0");
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "1.3.1",
            DocumentName = "Test.rvt"
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(0, exitCode);
        Assert.Contains("WARN: Live Add-in patch version differs from CLI", writer.ToString());
        Assert.Contains("Restart Revit only if this update changed add-in", writer.ToString());
    }

    [Fact]
    public async Task Execute_LiveAddinMetadataMismatch_InformsButDoesNotWarn()
    {
        var environment = WithCliVersion(CreateDoctorEnvironment(), "1.3.0+newcli");
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "1.3.0+oldaddin",
            DocumentName = "Test.rvt"
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("INFO: Live Add-in build metadata differs from CLI", output);
        Assert.Contains("CLI-only updates do not require restarting Revit", output);
        Assert.DoesNotContain("WARN: Live Add-in version metadata", output);
    }

    [Fact]
    public async Task Execute_LiveAddinMissingVersion_ReturnsFailure()
    {
        var environment = CreateDoctorEnvironment();
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "",
            DocumentName = "Test.rvt"
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Live Add-in version", writer.ToString());
        Assert.Contains("reinstall the Revit 2026 add-in", writer.ToString());
    }

    [Fact]
    public async Task Execute_LiveAddinUnparseableVersionWithUnparseableCliVersion_ReturnsFailure()
    {
        var environment = WithCliVersion(CreateDoctorEnvironment(), "dev");
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "garbage",
            DocumentName = "Test.rvt"
        };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Live Add-in version cannot be parsed", writer.ToString());
        Assert.Contains("reinstall the Revit 2026 add-in", writer.ToString());
    }

    [Fact]
    public async Task Execute_InstalledAddinMajorMinorMismatch_ReturnsFailure()
    {
        var environment = WithCliVersion(CreateDoctorEnvironment(), "9.9.0");
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo { RevitVersion = "2026", RevitYear = 2026, AddinVersion = "9.9.0" };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Installed Add-in version", writer.ToString());
        Assert.Contains("does not match CLI", writer.ToString());
    }

    [Fact]
    public async Task Execute_PrintsCliAndInstalledAddinVersions_WhenManifestAssemblyExists()
    {
        var environment = CreateDoctorEnvironment();
        WriteAddinManifest(environment, CurrentCliAssemblyPath());
        var status = new StatusInfo { RevitVersion = "2026", RevitYear = 2026, AddinVersion = environment.CliVersion };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        var output = writer.ToString();
        Assert.Contains("CLI version", output);
        Assert.Contains("Installed Add-in version", output);
    }

    [Fact]
    public async Task Execute_WrongRevitYear_ReturnsFailure()
    {
        var status = new StatusInfo { RevitVersion = "2025", RevitYear = 2025, DocumentName = "Test.rvt" };
        var response = ApiResponse<StatusInfo>.Ok(status);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, CreateDoctorEnvironment());

        Assert.Equal(1, exitCode);
        Assert.Contains("Revit 2026", writer.ToString());
        Assert.Contains("2025", writer.ToString());
    }

    [Fact]
    public async Task Execute_MissingRevitApiDlls_PrintsRevit2026PrecheckFailure()
    {
        var environment = CreateDoctorEnvironment();
        File.Delete(Path.Combine(environment.Revit2026InstallDir!, "RevitAPI.dll"));
        var status = new StatusInfo { RevitVersion = "2026", DocumentName = "Test.rvt" };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Revit 2026 API", writer.ToString());
        Assert.Contains("RevitAPI.dll", writer.ToString());
    }

    [Fact]
    public async Task Execute_MissingManifest_PrintsAddinManifestFailure()
    {
        var environment = CreateDoctorEnvironment();
        File.Delete(environment.ManifestPath);
        var status = new StatusInfo { RevitVersion = "2026", DocumentName = "Test.rvt" };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("Add-in manifest", writer.ToString());
        Assert.Contains("RevitCli.addin", writer.ToString());
    }

    [Fact]
    public async Task Execute_StaleServerInfo_PrintsStalePidFailure()
    {
        var environment = CreateDoctorEnvironment();
        var serverDir = Path.GetDirectoryName(environment.ServerInfoPath)!;
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(environment.ServerInfoPath, JsonSerializer.Serialize(new ServerInfo
        {
            Port = 17839,
            Pid = int.MaxValue,
            RevitVersion = "2026",
            Token = "abc"
        }));
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("stale", writer.ToString(), System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pid", writer.ToString(), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RevitInstallDirResolver_PrefersRevitCliOverrideEnvVar()
    {
        // Per-test sandbox so the explicit override has somewhere safe to point.
        var root = Path.Combine(Path.GetTempPath(), $"revitcli_resolver_{Guid.NewGuid():N}");
        var revitCliDir = Path.Combine(root, "revitcli-override");
        var autodeskDir = Path.Combine(root, "autodesk-convention");

        using var revitCli = new EnvVarScope("REVITCLI_REVIT2026_INSTALL_DIR", revitCliDir);
        using var autodesk = new EnvVarScope("Revit2026InstallDir", autodeskDir);

        Assert.Equal(revitCliDir, RevitInstallDirResolver.Resolve(2026));
    }

    [Fact]
    public void RevitInstallDirResolver_HonorsAutodeskConventionEnvVar()
    {
        // Reproduces the bug: Revit installed at a non-default path that the
        // user advertised via the Autodesk-convention Revit<year>InstallDir.
        var customDir = Path.Combine("D:", "revit2026", "Revit 2026");

        using var revitCli = new EnvVarScope("REVITCLI_REVIT2026_INSTALL_DIR", null);
        using var autodesk = new EnvVarScope("Revit2026InstallDir", customDir);

        Assert.Equal(customDir, RevitInstallDirResolver.Resolve(2026));
    }

    [Fact]
    public void RevitInstallDirResolver_UsesConfiguredPathWhenEnvVarsUnset()
    {
        var configuredDir = Path.Combine("D:", "revit2026", "Revit 2026");

        using var revitCli = new EnvVarScope("REVITCLI_REVIT2026_INSTALL_DIR", null);
        using var autodesk = new EnvVarScope("Revit2026InstallDir", null);

        Assert.Equal(configuredDir, RevitInstallDirResolver.Resolve(2026, configuredDir));
    }

    [Fact]
    public void RevitInstallDirResolver_FallsBackToProgramFilesDefault()
    {
        using var revitCli = new EnvVarScope("REVITCLI_REVIT2026_INSTALL_DIR", null);
        using var autodesk = new EnvVarScope("Revit2026InstallDir", null);

        var resolved = RevitInstallDirResolver.Resolve(2026);

        Assert.EndsWith(Path.Combine("Autodesk", "Revit 2026"), resolved);
        Assert.Equal(RevitInstallDirResolver.DefaultInstallDir(2026), resolved);
    }

    [Fact]
    public void RevitInstallDirResolver_TreatsWhitespaceOnlyEnvVarAsUnset()
    {
        using var revitCli = new EnvVarScope("REVITCLI_REVIT2025_INSTALL_DIR", "   ");
        using var autodesk = new EnvVarScope("Revit2025InstallDir", null);

        Assert.Equal(RevitInstallDirResolver.DefaultInstallDir(2025), RevitInstallDirResolver.Resolve(2025));
    }

    [Fact]
    public async Task Execute_MissingDllsReportsResolvedPath_NotHardcodedDefault()
    {
        // Simulate: Revit installed at a non-default path, env var advertises
        // it, but we deliberately leave the DLLs absent so the precheck fails.
        // The FAIL message must mention the env-var path so users see whether
        // their override was honored.
        var customDir = Path.Combine(Path.GetTempPath(), $"revitcli_doctor_{Guid.NewGuid():N}", "Revit 2026");
        Directory.CreateDirectory(customDir);

        var environment = CreateDoctorEnvironment();
        environment = new DoctorEnvironment
        {
            UserProfile = environment.UserProfile,
            AppData = environment.AppData,
            TargetRevitYear = environment.TargetRevitYear,
            RevitInstallDir = customDir,
            Revit2026InstallDir = customDir,
            CliVersion = environment.CliVersion
        };
        WriteAddinManifest(environment, CurrentCliAssemblyPath());

        var status = new StatusInfo { RevitVersion = "2026", RevitYear = 2026, AddinVersion = environment.CliVersion };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains(customDir, output);
        Assert.DoesNotContain("C:\\Program Files\\Autodesk\\Revit 2026", output);
    }

    [Fact]
    public async Task Execute_ServerInfoPidBelongsToNonRevitProcess_ReturnsFailure()
    {
        var environment = CreateDoctorEnvironment();
        var serverDir = Path.GetDirectoryName(environment.ServerInfoPath)!;
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(environment.ServerInfoPath, JsonSerializer.Serialize(new ServerInfo
        {
            Port = 17839,
            Pid = System.Environment.ProcessId,
            RevitVersion = "2026",
            Token = "abc"
        }));
        var status = new StatusInfo { RevitVersion = "2026", RevitYear = 2026, DocumentName = "Test.rvt" };
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await DoctorCommand.ExecuteAsync(client, new CliConfig(), writer, environment);

        Assert.Equal(1, exitCode);
        Assert.Contains("not Revit", writer.ToString(), System.StringComparison.OrdinalIgnoreCase);
    }
}
