using System;
using System.IO;

namespace RevitCli.Diagnostics;

/// <summary>
/// Resolves the on-disk install directory for a given Revit year by consulting,
/// in order:
/// <list type="number">
///   <item><description><c>REVITCLI_REVIT&lt;year&gt;_INSTALL_DIR</c> (RevitCli override)</description></item>
///   <item><description><c>Revit&lt;year&gt;InstallDir</c> (Autodesk / csproj convention)</description></item>
///   <item><description>The default <c>%ProgramFiles%\Autodesk\Revit &lt;year&gt;</c></description></item>
/// </list>
/// Mirrors the resolution that <c>RevitCli.Addin.Tests.csproj</c> performs at
/// build time so <c>revitcli doctor</c> reports the same path as the project
/// references rather than the hardcoded default.
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
        getEnv ??= Environment.GetEnvironmentVariable;

        var revitCliOverride = getEnv($"REVITCLI_REVIT{year}_INSTALL_DIR");
        if (!string.IsNullOrWhiteSpace(revitCliOverride))
            return revitCliOverride!.Trim();

        var autodeskVar = getEnv($"Revit{year}InstallDir");
        if (!string.IsNullOrWhiteSpace(autodeskVar))
            return autodeskVar!.Trim();

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
