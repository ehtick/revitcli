using System;
using System.IO;

namespace RevitCli.Diagnostics;

/// <summary>
/// Resolves the on-disk install directory for a given Revit year by consulting,
/// in order:
/// <list type="number">
///   <item><description><c>REVITCLI_REVIT&lt;year&gt;_INSTALL_DIR</c> — RevitCli-only override; no csproj reads this variable. It is a superset over the Autodesk convention, intended as an escape hatch when callers want to redirect <c>doctor</c> without touching MSBuild.</description></item>
///   <item><description><c>Revit&lt;year&gt;InstallDir</c> — Autodesk convention; <c>RevitCli.Addin.Tests.csproj</c> reads this same variable at build time.</description></item>
///   <item><description><c>config.json</c> value <c>revit&lt;year&gt;InstallDir</c>.</description></item>
///   <item><description>The default <c>%ProgramFiles%\Autodesk\Revit &lt;year&gt;</c>.</description></item>
/// </list>
/// When only tier 2 is set, <c>revitcli doctor</c> reports the same path
/// <c>RevitCli.Addin.Tests.csproj</c> uses at build time. Tier 1 is a
/// RevitCli-specific override that is not shared with any csproj.
/// </summary>
internal static class RevitInstallDirResolver
{
    /// <summary>
    /// Resolves the install directory for the given Revit year. The returned
    /// path is the value actually checked, so callers can include it verbatim
    /// in diagnostic messages.
    /// </summary>
    /// <param name="year">The Revit year (e.g. 2024, 2025, 2026).</param>
    /// <param name="getEnv">
    /// Optional environment variable accessor; defaults to
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>. Override in
    /// tests to avoid mutating process-wide state.
    /// </param>
    public static string Resolve(int year, Func<string, string?>? getEnv = null)
    {
        return Resolve(year, configuredInstallDir: null, getEnv);
    }

    public static string Resolve(
        int year,
        string? configuredInstallDir,
        Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        var revitCliOverride = getEnv($"REVITCLI_REVIT{year}_INSTALL_DIR");
        if (!string.IsNullOrWhiteSpace(revitCliOverride))
            return revitCliOverride!.Trim();

        var autodeskVar = getEnv($"Revit{year}InstallDir");
        if (!string.IsNullOrWhiteSpace(autodeskVar))
            return autodeskVar!.Trim();

        if (!string.IsNullOrWhiteSpace(configuredInstallDir))
            return configuredInstallDir!.Trim();

        return DefaultInstallDir(year);
    }

    /// <summary>
    /// The canonical default <c>C:\Program Files\Autodesk\Revit &lt;year&gt;</c>
    /// (or the equivalent under the localized Program Files folder).
    /// </summary>
    public static string DefaultInstallDir(int year)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Autodesk",
            $"Revit {year}");
    }
}
