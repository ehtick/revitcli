using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using RevitCli.Client;
using RevitCli.Config;
using RevitCli.Diagnostics;
using RevitCli.Output;
using RevitCli.Profile;
using RevitCli.Shared;
using Spectre.Console;

namespace RevitCli.Commands;

public static class DoctorCommand
{
    public static Command Create(RevitClient client, CliConfig config)
    {
        var command = new Command("doctor", "Check RevitCli setup and diagnose issues");
        var checkVersionOpt = new Option<int>(
            "--check-version",
            () => 2026,
            "Revit version to check for local API DLLs, add-in manifest, and live connection: 2024|2025|2026.");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json");
        command.AddOption(checkVersionOpt);
        command.AddOption(outputOpt);

        command.SetHandler(async (int checkVersion, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteAsync(client, config, Console.Out, checkVersion, outputFormat);
        }, checkVersionOpt, outputOpt);

        return command;
    }

    public static Task<int> ExecuteAsync(RevitClient client, CliConfig config, TextWriter output, int checkVersion = 2026)
    {
        return ExecuteAsync(client, config, output, checkVersion, "table");
    }

    public static Task<int> ExecuteAsync(
        RevitClient client,
        CliConfig config,
        TextWriter output,
        string outputFormat)
    {
        return ExecuteAsync(client, config, output, 2026, outputFormat);
    }

    public static Task<int> ExecuteAsync(
        RevitClient client,
        CliConfig config,
        TextWriter output,
        int checkVersion,
        string outputFormat)
    {
        return ExecuteAsync(client, config, output, DoctorEnvironment.Current(checkVersion, config), outputFormat);
    }

    internal static async Task<int> ExecuteAsync(
        RevitClient client,
        CliConfig config,
        TextWriter output,
        DoctorEnvironment environment)
    {
        return await ExecuteAsync(client, config, output, environment, "table");
    }

    internal static async Task<int> ExecuteAsync(
        RevitClient client,
        CliConfig config,
        TextWriter output,
        DoctorEnvironment environment,
        string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json"))
        {
            await output.WriteLineAsync("Error: --output must be 'table' or 'json'.");
            return 1;
        }

        if (normalizedOutput == "json")
            return await ExecuteJsonAsync(client, config, output, environment);

        return await ExecuteTableAsync(client, config, output, environment);
    }

    private static async Task<int> ExecuteTableAsync(
        RevitClient client,
        CliConfig config,
        TextWriter output,
        DoctorEnvironment environment)
    {
        var checkVersion = environment.TargetRevitYear;
        if (!IsSupportedRevitYear(checkVersion))
        {
            await WriteFail(output, $"Unsupported Revit version {checkVersion}. Supported versions: 2024, 2025, 2026.");
            return 1;
        }

        var hasFailure = false;

        // 1. Config file
        var configPath = environment.ConfigPath;
        if (File.Exists(configPath))
            await output.WriteLineAsync($"OK: Configuration file exists ({configPath})");
        else
            await output.WriteLineAsync($"INFO: No configuration file ({configPath}) - using defaults");

        var cliVersion = environment.CliVersion;
        await WriteOk(output, $"CLI version: {cliVersion}");
        var expectedVersion = await ParseExpectedVersion(output, cliVersion);

        // 2. Local prerequisites for the requested Revit version.
        hasFailure |= !await WriteRevitApiCheck(output, environment);
        hasFailure |= !await WriteAddinManifestCheck(output, environment, expectedVersion);

        // 3. Server URL
        await output.WriteLineAsync($"OK: Server URL: {config.ServerUrl}");

        // 4. Server info file
        hasFailure |= !await WriteServerInfoCheck(output, environment);

        // 5. Connection test
        var status = await client.GetStatusAsync();
        if (status.Success)
        {
            await output.WriteLineAsync($"OK: Connected to Revit {status.Data!.RevitVersion}");
            if (!IsTargetRevitVersion(status.Data, checkVersion))
            {
                await output.WriteLineAsync(
                    $"FAIL: Connected Revit version is {status.Data.RevitVersion}; this smoke baseline targets Revit {checkVersion}.");
                hasFailure = true;
            }

            if (!string.IsNullOrWhiteSpace(status.Data.AddinVersion))
            {
                if (!ComponentVersion.TryParse(status.Data.AddinVersion, out _))
                {
                    await WriteFail(output, $"Live Add-in version cannot be parsed: {status.Data.AddinVersion}");
                    await output.WriteLineAsync(LiveAddinVersionHint(checkVersion));
                    hasFailure = true;
                }
                else if (expectedVersion.HasValue)
                {
                    var liveCompatible = await WriteVersionCompatibility(
                        output,
                        "Live Add-in",
                        expectedVersion.Value,
                        status.Data.AddinVersion);
                    if (!liveCompatible)
                    {
                        await output.WriteLineAsync(LiveAddinVersionHint(checkVersion));
                        hasFailure = true;
                    }
                }
                else
                {
                    await WriteOk(output, $"Live Add-in version: {status.Data.AddinVersion}");
                }
            }
            else
            {
                await WriteFail(output, "Live Add-in version is missing from status.");
                await output.WriteLineAsync(LiveAddinVersionHint(checkVersion));
                hasFailure = true;
            }

            if (status.Data.DocumentName != null)
                await output.WriteLineAsync($"OK: Document: {status.Data.DocumentName}");
            else
                await output.WriteLineAsync("INFO: No document open");
        }
        else
        {
            await output.WriteLineAsync($"FAIL: {status.Error}");
            await output.WriteLineAsync($"HINT: Start Revit {checkVersion}, confirm the RevitCli add-in is loaded, open the test model, then rerun 'revitcli doctor --check-version {checkVersion}'.");
            hasFailure = true;
        }

        // 6. Project profile
        hasFailure |= !WriteProfileInfo(null, s => output.WriteLine(s));

        return hasFailure ? 1 : 0;
    }

    private static async Task<int> ExecuteJsonAsync(
        RevitClient client,
        CliConfig config,
        TextWriter output,
        DoctorEnvironment environment)
    {
        var capture = new StringWriter();
        var exitCode = await ExecuteTableAsync(client, config, capture, environment);
        var report = CreateReport(config, environment, exitCode, capture.ToString());

        await output.WriteLineAsync(JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyIgnoreNull));
        return report.Valid ? 0 : 1;
    }

    private static DoctorReport CreateReport(
        CliConfig config,
        DoctorEnvironment environment,
        int exitCode,
        string textOutput)
    {
        var checks = new List<DoctorCheck>();
        foreach (var rawLine in textOutput.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (!TryParseDiagnosticLine(line, out var status, out var message))
                continue;

            checks.Add(new DoctorCheck(status, CreateCheckName(status, message), message));
        }

        var valid = !checks.Any(check => check.Status.Equals("fail", StringComparison.OrdinalIgnoreCase));
        return new DoctorReport(
            "doctor.v1",
            exitCode == 0 && valid,
            valid,
            exitCode,
            environment.TargetRevitYear,
            config.ServerUrl,
            checks);
    }

    private static bool TryParseDiagnosticLine(string line, out string status, out string message)
    {
        foreach (var (prefix, normalized) in DiagnosticPrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                status = normalized;
                message = line[prefix.Length..].Trim();
                return true;
            }
        }

        status = "";
        message = "";
        return false;
    }

    private static string CreateCheckName(string status, string message)
    {
        if (status.Equals("hint", StringComparison.OrdinalIgnoreCase))
            return "hint";

        var parenIndex = message.IndexOf(" (", StringComparison.Ordinal);
        var colonIndex = message.IndexOf(": ", StringComparison.Ordinal);
        if (colonIndex > 0 && (parenIndex < 0 || colonIndex < parenIndex))
            return message[..colonIndex].Trim();

        if (parenIndex > 0)
            return message[..parenIndex].Trim();

        return message.Length <= 80 ? message : message[..80].TrimEnd();
    }

    private static bool IsTargetRevitVersion(StatusInfo status, int checkVersion)
    {
        if (status.RevitYear != 0)
            return status.RevitYear == checkVersion;

        return status.RevitVersion.Contains(checkVersion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedRevitYear(int year) => year is 2024 or 2025 or 2026;

    private static string LiveAddinVersionHint(int year) =>
        $"HINT: Close Revit, reinstall the Revit {year} add-in, restart Revit, and rerun doctor.";

    private static async Task<bool> WriteRevitApiCheck(TextWriter output, DoctorEnvironment environment)
    {
        var year = environment.TargetRevitYear;
        var installDir = environment.ResolvedRevitInstallDir;
        var missing = new[] { "RevitAPI.dll", "RevitAPIUI.dll" }
            .Where(dll => !File.Exists(Path.Combine(installDir, dll)))
            .ToArray();

        if (missing.Length == 0)
        {
            await output.WriteLineAsync($"OK: Revit {year} API DLLs found ({installDir})");
            return true;
        }

        await output.WriteLineAsync(
            $"FAIL: Revit {year} API DLLs missing at {installDir}: {string.Join(", ", missing)}");
        await output.WriteLineAsync(
            $"HINT: Install Revit {year} or set REVITCLI_REVIT{year}_INSTALL_DIR / Revit{year}InstallDir / config key Revit{year}InstallDir to the Revit {year} install directory.");
        return false;
    }

    private static async Task<bool> WriteAddinManifestCheck(
        TextWriter output,
        DoctorEnvironment environment,
        ComponentVersion? expectedVersion)
    {
        var manifestPath = environment.ManifestPath;
        if (!File.Exists(manifestPath))
        {
            await WriteFail(output, $"Add-in manifest missing ({manifestPath})");
            await output.WriteLineAsync(
                $"HINT: Build/publish the add-in and install RevitCli.addin under Autodesk\\Revit\\Addins\\{environment.TargetRevitYear}.");
            return false;
        }

        try
        {
            var doc = XDocument.Load(manifestPath);
            var assembly = doc.Descendants("Assembly").FirstOrDefault()?.Value.Trim();
            if (string.IsNullOrWhiteSpace(assembly))
            {
                await WriteFail(output, $"Add-in manifest has no Assembly path ({manifestPath})");
                return false;
            }

            var assemblyPath = Path.IsPathRooted(assembly)
                ? assembly
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, assembly));
            if (!File.Exists(assemblyPath))
            {
                await WriteFail(output, $"Add-in assembly from manifest does not exist ({assemblyPath})");
                return false;
            }

            if (!AssemblyVersionReader.TryRead(assemblyPath, out var installedVersion, out var versionError))
            {
                await WriteFail(output, $"Installed Add-in version cannot be read ({assemblyPath}): {versionError}");
                return false;
            }

            await WriteOk(output, $"Add-in manifest: {manifestPath}");
            await WriteOk(output, $"Add-in assembly: {assemblyPath}");
            if (expectedVersion == null)
            {
                await WriteOk(output, $"Installed Add-in version: {installedVersion}");
                return true;
            }

            return await WriteVersionCompatibility(output, "Installed Add-in", expectedVersion.Value, installedVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            await WriteFail(output, $"Add-in manifest cannot be read ({manifestPath}): {ex.Message}");
            return false;
        }
    }

    private static async Task<ComponentVersion?> ParseExpectedVersion(TextWriter output, string cliVersion)
    {
        if (ComponentVersion.TryParse(cliVersion, out var expectedVersion))
            return expectedVersion;

        await WriteWarn(output, $"CLI version cannot be parsed for installed Add-in compatibility check: {cliVersion}");
        return null;
    }

    private static async Task<bool> WriteVersionCompatibility(
        TextWriter output,
        string componentName,
        ComponentVersion expectedVersion,
        string actualVersionText)
    {
        await WriteOk(output, $"{componentName} version: {actualVersionText}");

        if (!ComponentVersion.TryParse(actualVersionText, out var actualVersion))
        {
            await WriteFail(output, $"{componentName} version cannot be parsed: {actualVersionText}");
            return false;
        }

        var compatibility = ComponentVersion.Compare(expectedVersion, actualVersion);
        switch (compatibility)
        {
            case VersionCompatibility.Compatible:
                return true;
            case VersionCompatibility.MetadataMismatch:
                await WriteInfo(
                    output,
                    $"{componentName} build metadata differs from CLI but protocol version is compatible: actual={actualVersionText}, CLI={expectedVersion}. CLI-only updates do not require restarting Revit.");
                return true;
            case VersionCompatibility.PatchMismatch:
                await WriteWarn(
                    output,
                    $"{componentName} patch version differs from CLI: actual={actualVersionText}, CLI={expectedVersion}. Restart Revit only if this update changed add-in or Revit API behavior.");
                return true;
            case VersionCompatibility.MajorMinorMismatch:
                await WriteFail(
                    output,
                    $"{componentName} version does not match CLI: actual={actualVersionText}, CLI={expectedVersion}");
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(compatibility), compatibility, null);
        }
    }

    private static Task WriteOk(TextWriter output, string message)
    {
        return output.WriteLineAsync($"OK: {message}");
    }

    private static Task WriteWarn(TextWriter output, string message)
    {
        return output.WriteLineAsync($"WARN: {message}");
    }

    private static Task WriteInfo(TextWriter output, string message)
    {
        return output.WriteLineAsync($"INFO: {message}");
    }

    private static Task WriteFail(TextWriter output, string message)
    {
        return output.WriteLineAsync($"FAIL: {message}");
    }

    private static async Task<bool> WriteServerInfoCheck(TextWriter output, DoctorEnvironment environment)
    {
        var serverInfoPath = environment.ServerInfoPath;
        if (!File.Exists(serverInfoPath))
        {
            await output.WriteLineAsync("INFO: No server info file (OK if Revit is not running)");
            return true;
        }

        ServerInfo? info;
        try
        {
            var json = File.ReadAllText(serverInfoPath);
            info = JsonSerializer.Deserialize<ServerInfo>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            await output.WriteLineAsync($"FAIL: Server info file exists but cannot be parsed ({serverInfoPath}): {ex.Message}");
            return false;
        }

        if (info == null)
        {
            await output.WriteLineAsync($"FAIL: Server info file is empty or invalid ({serverInfoPath})");
            return false;
        }

        var failures = new List<string>();
        if (info.Port < 1024 || info.Port > 65535)
            failures.Add($"invalid port={info.Port}");
        if (info.Pid <= 0)
            failures.Add($"invalid pid={info.Pid}");
        if (string.IsNullOrWhiteSpace(info.Token))
            failures.Add("missing token");

        string? processName = null;
        if (info.Pid > 0)
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById(info.Pid);
                if (proc.HasExited)
                    failures.Add($"stale pid={info.Pid}");
                else
                {
                    processName = proc.ProcessName;
                    if (!processName.Contains("Revit", StringComparison.OrdinalIgnoreCase))
                        failures.Add($"pid={info.Pid} belongs to process '{processName}', not Revit");
                }
            }
            catch (ArgumentException)
            {
                failures.Add($"stale pid={info.Pid}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                failures.Add($"cannot inspect pid={info.Pid}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            await output.WriteLineAsync(
                $"FAIL: Server info is stale or invalid ({serverInfoPath}): {string.Join(", ", failures)}");
            await output.WriteLineAsync(
                $"HINT: Close Revit, delete the stale server.json if it remains, restart Revit {environment.TargetRevitYear}, and rerun doctor --check-version {environment.TargetRevitYear}.");
            return false;
        }

        var processSuffix = processName == null ? "" : $", process={processName}";
        await output.WriteLineAsync(
            $"OK: Server info: port={info.Port}, pid={info.Pid}{processSuffix}, started={info.StartedAt}");
        return true;
    }

    private static bool WriteProfileInfo(Action<string>? spectreWrite, Action<string>? plainWrite)
    {
        var profilePath = ProfileLoader.Discover();
        if (profilePath == null)
        {
            var msg = $"No {ProfileLoader.FileName} found in directory tree";
            spectreWrite?.Invoke($"  [blue]\u25cb[/] {msg}");
            plainWrite?.Invoke($"INFO: {msg}");

            // Quickstart guidance
            spectreWrite?.Invoke("");
            spectreWrite?.Invoke("  [yellow]Quick start:[/]");
            spectreWrite?.Invoke("    1. Copy a starter profile to your project root:");
            spectreWrite?.Invoke("       [dim]cp profiles/architectural-issue.yml .revitcli.yml[/]");
            spectreWrite?.Invoke("       [dim]cp profiles/interior-room-data.yml .revitcli.yml[/]");
            spectreWrite?.Invoke("       [dim]cp profiles/general-publish.yml .revitcli.yml[/]");
            spectreWrite?.Invoke("    2. Run: [white]revitcli check[/]");
            spectreWrite?.Invoke("    3. Run: [white]revitcli publish --dry-run[/]");

            plainWrite?.Invoke("");
            plainWrite?.Invoke("Quick start:");
            plainWrite?.Invoke("  1. Copy a starter profile: cp profiles/general-publish.yml .revitcli.yml");
            plainWrite?.Invoke("  2. Run: revitcli check");
            plainWrite?.Invoke("  3. Run: revitcli publish --dry-run");
            return true;
        }

        spectreWrite?.Invoke($"  [green]\u2713[/] Profile: [cyan]{Markup.Escape(profilePath)}[/]");
        plainWrite?.Invoke($"OK: Profile: {profilePath}");

        try
        {
            var profile = ProfileLoader.Load(profilePath);

            if (profile.Checks.Count > 0)
            {
                var checks = string.Join(", ", profile.Checks.Keys);
                spectreWrite?.Invoke($"      Check sets: [white]{Markup.Escape(checks)}[/]");
                plainWrite?.Invoke($"  Check sets: {checks}");
            }

            if (profile.Exports.Count > 0)
            {
                var exports = string.Join(", ", profile.Exports.Keys);
                spectreWrite?.Invoke($"      Export presets: [white]{Markup.Escape(exports)}[/]");
                plainWrite?.Invoke($"  Export presets: {exports}");
            }

            if (profile.Publish.Count > 0)
            {
                var pipelines = string.Join(", ", profile.Publish.Keys);
                spectreWrite?.Invoke($"      Publish pipelines: [white]{Markup.Escape(pipelines)}[/]");
                plainWrite?.Invoke($"  Publish pipelines: {pipelines}");
            }

            if (!string.IsNullOrWhiteSpace(profile.Extends))
            {
                spectreWrite?.Invoke($"      Extends: [dim]{Markup.Escape(profile.Extends)}[/]");
                plainWrite?.Invoke($"  Extends: {profile.Extends}");
            }

            return true;
        }
        catch (Exception ex)
        {
            spectreWrite?.Invoke($"  [red]\u2717[/] Profile parse error: [red]{Markup.Escape(ex.Message)}[/]");
            plainWrite?.Invoke($"FAIL: Profile parse error: {ex.Message}");
            return false;
        }
    }

    private static readonly (string Prefix, string Status)[] DiagnosticPrefixes =
    {
        ("OK:", "ok"),
        ("INFO:", "info"),
        ("WARN:", "warn"),
        ("FAIL:", "fail"),
        ("HINT:", "hint"),
    };

    private sealed record DoctorReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("exitCode")] int ExitCode,
        [property: JsonPropertyName("targetRevitYear")] int TargetRevitYear,
        [property: JsonPropertyName("serverUrl")] string ServerUrl,
        [property: JsonPropertyName("checks")] IReadOnlyList<DoctorCheck> Checks);

    private sealed record DoctorCheck(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("message")] string Message);

}

internal sealed class DoctorEnvironment
{
    public string CliVersion { get; init; } = AssemblyVersionReader.CurrentCliVersion();

    public string UserProfile { get; init; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string AppData { get; init; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public int TargetRevitYear { get; init; } = 2026;

    public string? RevitInstallDir { get; init; }

    public string? Revit2026InstallDir { get; init; }

    public string ConfigPath => Path.Combine(UserProfile, ".revitcli", "config.json");

    public string ServerInfoPath => Path.Combine(UserProfile, ".revitcli", "server.json");

    public string ManifestPath => Path.Combine(
        AppData, "Autodesk", "Revit", "Addins", TargetRevitYear.ToString(), "RevitCli.addin");

    public string ResolvedRevitInstallDir
    {
        get
        {
            var explicitDir = !string.IsNullOrWhiteSpace(RevitInstallDir)
                ? RevitInstallDir
                : (TargetRevitYear == 2026 ? Revit2026InstallDir : null);
            return string.IsNullOrWhiteSpace(explicitDir)
                ? RevitInstallDirResolver.DefaultInstallDir(TargetRevitYear)
                : explicitDir!;
        }
    }

    public string ResolvedRevit2026InstallDir => ResolvedRevitInstallDir;

    public static DoctorEnvironment Current(int targetRevitYear = 2026, CliConfig? config = null)
    {
        return new DoctorEnvironment
        {
            TargetRevitYear = targetRevitYear,
            // Resolve via the shared helper. When only Revit<year>InstallDir is set,
            // doctor reports the same path RevitCli.Addin.Tests.csproj reads at
            // build time. REVITCLI_REVIT<year>_INSTALL_DIR is a RevitCli-only
            // superset (no csproj consults it) and can override doctor without
            // touching MSBuild. If neither env var is set, fall back to
            // config.json before the Program Files default.
            RevitInstallDir = RevitInstallDirResolver.Resolve(
                targetRevitYear,
                config?.GetRevitInstallDir(targetRevitYear))
        };
    }
}
