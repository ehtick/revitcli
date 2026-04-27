using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using RevitCli.Diagnostics;

namespace RevitCli.Commands;

/// <summary>
/// `revitcli ci` command group. Currently exposes a single subcommand,
/// <c>doctor</c>, which detects the surrounding CI provider and prints a
/// ready-to-paste workflow snippet (see docs/roadmap-2026q2-q3.md §4 step 4).
///
/// Future v1.7 follow-ups (CI lint helpers, cache utilities, ...) will hang
/// off this same group so users only need to learn one prefix.
/// </summary>
public static class CiCommand
{
    public static Command Create()
    {
        var ci = new Command("ci", "CI integration helpers (detect provider, emit workflow templates)");
        ci.AddCommand(CreateDoctorCommand());
        return ci;
    }

    private static Command CreateDoctorCommand()
    {
        var doctor = new Command("doctor", "Detect the active CI environment and print a workflow template");
        doctor.SetHandler(async () =>
        {
            // doctor is informational: never fail the run, never affect exit code.
            Environment.ExitCode = await ExecuteDoctorAsync(Console.Out);
        });
        return doctor;
    }

    /// <summary>
    /// Render the doctor report. Exposed for tests so they can capture stdout
    /// without spawning the full CommandLine pipeline.
    /// </summary>
    public static Task<int> ExecuteDoctorAsync(TextWriter output)
        => ExecuteDoctorAsync(output, lookup: null);

    /// <summary>
    /// Test seam: takes an explicit env-var lookup so tests do not have to
    /// mutate the real process environment.
    /// </summary>
    public static async Task<int> ExecuteDoctorAsync(TextWriter output, Func<string, string?>? lookup)
    {
        var detection = CiEnvironment.Detect(lookup);
        var version = CiEnvironment.GetCliVersion();
        var runnerOs = CiEnvironment.ResolveRunnerOs(detection);

        await output.WriteLineAsync("RevitCli CI Doctor");
        await output.WriteLineAsync(new string('=', 40));
        await output.WriteLineAsync($"CLI version : {version}");

        if (!detection.IsCi)
        {
            await output.WriteLineAsync("Provider    : none");
            await output.WriteLineAsync($"Runner OS   : {runnerOs}");
            await output.WriteLineAsync(string.Empty);
            await output.WriteLineAsync(
                "No CI environment detected. Showing GitHub Actions workflow for reference:");
        }
        else
        {
            await output.WriteLineAsync($"Provider    : {detection.DisplayName} ({detection.Provider})");
            await output.WriteLineAsync($"Runner OS   : {runnerOs}");
            await output.WriteLineAsync(string.Empty);
            await output.WriteLineAsync(
                $"Recommended workflow snippet for {detection.DisplayName}:");
        }

        await output.WriteLineAsync(string.Empty);
        await output.WriteLineAsync("--- snippet (begin) ---");
        await output.WriteAsync(CiEnvironment.SuggestSnippet(detection.Provider));
        await output.WriteLineAsync("--- snippet (end) ---");

        await output.WriteLineAsync(string.Empty);
        await output.WriteLineAsync("Known gotchas:");
        foreach (var note in CiEnvironment.Gotchas(detection.Provider))
        {
            await output.WriteLineAsync($"  - {note}");
        }

        return 0;
    }
}
