using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Profile;

namespace RevitCli.Commands;

/// <summary>
/// `revitcli profile` — v1.9 governance surface over <c>.revitcli.yml</c>.
///
/// Subcommands:
/// <list type="bullet">
///   <item><c>profile validate</c> — schema/reference checker via <see cref="ProfileValidator"/>.</item>
///   <item><c>profile show --resolve</c> — render the merged effective profile + chain header via <see cref="ProfileResolver"/>.</item>
///   <item><c>profile diff</c> — structural diff over the four user-visible top-level dictionaries.</item>
/// </list>
/// All entry points return their exit code from a public <c>Execute*</c>
/// method so xUnit fact tests can drive them without spinning up the full
/// <see cref="System.CommandLine"/> parser.
/// </summary>
public static class ProfileCommand
{
    public static Command Create()
    {
        var command = new Command("profile", "Validate, resolve, and diff .revitcli.yml profiles (v1.9)");
        command.AddCommand(CreateValidateCommand());
        command.AddCommand(CreateShowCommand());
        command.AddCommand(CreateDiffCommand());
        command.AddCommand(CreateInstallCommand());
        return command;
    }

    // ------------------------------------------------------------------
    // validate
    // ------------------------------------------------------------------

    private static Command CreateValidateCommand()
    {
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml profile (default: walk up from cwd)");
        var cmd = new Command("validate", "Run schema and reference checks on a profile")
        {
            profileOpt,
        };

        cmd.SetHandler(async (string? profilePath) =>
        {
            Environment.ExitCode = await ExecuteValidateAsync(profilePath, Console.Out);
        }, profileOpt);

        return cmd;
    }

    public static async Task<int> ExecuteValidateAsync(string? profilePath, TextWriter output)
    {
        string resolved;
        try
        {
            resolved = ResolveProfilePath(profilePath);
        }
        catch (FileNotFoundException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        ProjectProfile profile;
        try
        {
            profile = ProfileLoader.Load(resolved);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or YamlDotNet.Core.YamlException)
        {
            // The loader's own validation throws — surface that as a single
            // error line so the user sees the same severity-bucketed format.
            await output.WriteLineAsync($"[error] (load): {ex.Message}");
            return 1;
        }

        var issues = ProfileValidator.Validate(profile);

        if (issues.Count == 0)
        {
            await output.WriteLineAsync($"OK: {resolved} has no validation issues.");
            return 0;
        }

        // Group printout by severity so the eye lands on errors first; within
        // each group ProfileValidator already returns deterministic ordering.
        var hasError = false;
        foreach (var severity in new[]
                 {
                     ProfileValidationSeverity.Error,
                     ProfileValidationSeverity.Warning,
                     ProfileValidationSeverity.Info,
                 })
        {
            foreach (var issue in issues.Where(i => i.Severity == severity))
            {
                if (issue.Severity == ProfileValidationSeverity.Error)
                    hasError = true;
                await output.WriteLineAsync(
                    $"[{issue.Severity.ToString().ToLowerInvariant()}] {issue.Path}: {issue.Message}");
            }
        }

        var counts = issues
            .GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => g.Count());
        var errors = counts.GetValueOrDefault(ProfileValidationSeverity.Error);
        var warnings = counts.GetValueOrDefault(ProfileValidationSeverity.Warning);
        var infos = counts.GetValueOrDefault(ProfileValidationSeverity.Info);
        await output.WriteLineAsync(
            $"Summary: {errors} error(s), {warnings} warning(s), {infos} info.");

        return hasError ? 1 : 0;
    }

    // ------------------------------------------------------------------
    // show --resolve
    // ------------------------------------------------------------------

    private static Command CreateShowCommand()
    {
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml profile (default: walk up from cwd)");
        var resolveOpt = new Option<bool>("--resolve", () => false,
            "Walk the extends chain and emit the merged effective profile");
        var outputOpt = new Option<string>("--output", () => "yaml", "Output format: yaml | json");

        var cmd = new Command("show", "Print the resolved effective profile (with extends chain)")
        {
            profileOpt,
            resolveOpt,
            outputOpt,
        };

        cmd.SetHandler(async (string? profilePath, bool resolve, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteShowAsync(profilePath, resolve, outputFormat, Console.Out);
        }, profileOpt, resolveOpt, outputOpt);

        return cmd;
    }

    public static async Task<int> ExecuteShowAsync(
        string? profilePath,
        bool resolve,
        string outputFormat,
        TextWriter output)
    {
        if (!resolve)
        {
            // Without --resolve there is no plain-`show` rendering defined yet
            // (deferred); guide the user to the supported invocation.
            await output.WriteLineAsync("Error: 'profile show' currently requires --resolve.");
            return 1;
        }

        string resolved;
        try
        {
            resolved = ResolveProfilePath(profilePath);
        }
        catch (FileNotFoundException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        ProfileRenderFormat format;
        switch ((outputFormat ?? "yaml").ToLowerInvariant())
        {
            case "yaml": format = ProfileRenderFormat.Yaml; break;
            case "json": format = ProfileRenderFormat.Json; break;
            default:
                await output.WriteLineAsync($"Error: --output must be 'yaml' or 'json', got '{outputFormat}'.");
                return 1;
        }

        try
        {
            var rendered = ProfileResolver.Render(resolved, format);
            // Render already terminates with a trailing newline; using Write
            // (not WriteLine) keeps the output byte-identical to what callers
            // would expect when piping into a file.
            await output.WriteAsync(rendered);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException
                                       or YamlDotNet.Core.YamlException
                                       or FileNotFoundException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // diff
    // ------------------------------------------------------------------

    private static Command CreateDiffCommand()
    {
        var aArg = new Argument<string>("a", "First profile path");
        var bArg = new Argument<string>("b", "Second profile path");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");

        var cmd = new Command("diff", "Show structural differences between two profiles")
        {
            aArg,
            bArg,
            outputOpt,
        };

        cmd.SetHandler(async (string a, string b, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteDiffAsync(a, b, outputFormat, Console.Out);
        }, aArg, bArg, outputOpt);

        return cmd;
    }

    public static async Task<int> ExecuteDiffAsync(
        string aPath,
        string bPath,
        string outputFormat,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(aPath) || !File.Exists(aPath))
        {
            await output.WriteLineAsync($"Error: profile not found: {aPath}");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(bPath) || !File.Exists(bPath))
        {
            await output.WriteLineAsync($"Error: profile not found: {bPath}");
            return 1;
        }

        ProjectProfile a, b;
        try
        {
            a = ProfileLoader.Load(aPath);
            b = ProfileLoader.Load(bPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or YamlDotNet.Core.YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var diff = ComputeDiff(a, b);

        var format = (outputFormat ?? "table").ToLowerInvariant();
        string rendered = format switch
        {
            "json" => RenderJson(diff),
            "markdown" => RenderMarkdown(diff),
            "table" => RenderTable(diff),
            _ => string.Empty,
        };

        if (rendered.Length == 0)
        {
            await output.WriteLineAsync($"Error: --output must be 'table', 'json', or 'markdown', got '{outputFormat}'.");
            return 1;
        }

        await output.WriteAsync(rendered);
        if (!rendered.EndsWith("\n", StringComparison.Ordinal))
            await output.WriteLineAsync();
        return 0;
    }

    // ------------------------------------------------------------------
    // diff core
    // ------------------------------------------------------------------

    /// <summary>Single structural delta between two profiles.</summary>
    public sealed record ProfileDiffEntry(string Path, string Change, string A, string B);

    /// <summary>Aggregated structural diff across the four governed sections.</summary>
    public sealed class ProfileDiffResult
    {
        public List<ProfileDiffEntry> Added { get; } = new();
        public List<ProfileDiffEntry> Removed { get; } = new();
        public List<ProfileDiffEntry> Changed { get; } = new();

        public IEnumerable<ProfileDiffEntry> All() => Added.Concat(Removed).Concat(Changed);
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
    }

    internal static ProfileDiffResult ComputeDiff(ProjectProfile a, ProjectProfile b)
    {
        var result = new ProfileDiffResult();

        DiffNamedDictionary("checks", a.Checks, b.Checks, SerializeForDiff, result);
        DiffNamedDictionary("publish", a.Publish, b.Publish, SerializeForDiff, result);
        // The user-visible top-level identifier 'presets.' lines up with the
        // YAML 'exports:' map (each export entry is a publish preset by name).
        DiffNamedDictionary("presets", a.Exports, b.Exports, SerializeForDiff, result);
        DiffDefaults(a.Defaults, b.Defaults, result);

        // Sort each bucket by path so output is deterministic across runs.
        result.Added.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
        result.Removed.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
        result.Changed.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
        return result;
    }

    private static void DiffNamedDictionary<TValue>(
        string sectionName,
        IDictionary<string, TValue> a,
        IDictionary<string, TValue> b,
        Func<TValue, string> serialize,
        ProfileDiffResult result)
        where TValue : class
    {
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in a.Keys) allKeys.Add(k);
        foreach (var k in b.Keys) allKeys.Add(k);

        foreach (var key in allKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var path = $"{sectionName}.{key}";
            var hasA = a.TryGetValue(key, out var av);
            var hasB = b.TryGetValue(key, out var bv);

            if (hasA && !hasB)
            {
                result.Removed.Add(new ProfileDiffEntry(path, "removed", serialize(av!), string.Empty));
            }
            else if (!hasA && hasB)
            {
                result.Added.Add(new ProfileDiffEntry(path, "added", string.Empty, serialize(bv!)));
            }
            else
            {
                var aSer = serialize(av!);
                var bSer = serialize(bv!);
                if (!string.Equals(aSer, bSer, StringComparison.Ordinal))
                {
                    result.Changed.Add(new ProfileDiffEntry(path, "changed", aSer, bSer));
                }
            }
        }
    }

    private static void DiffDefaults(ProfileDefaults a, ProfileDefaults b, ProfileDiffResult result)
    {
        // Field-level diff inside defaults so the user sees which knob changed
        // rather than a single opaque "defaults changed" row. Using fixed field
        // names (not reflection) keeps the surface explicit and StringComparison.Ordinal-safe.
        DiffScalar("defaults.outputDir", a.OutputDir, b.OutputDir, result);
        DiffScalar("defaults.notify", a.Notify, b.Notify, result);
    }

    private static void DiffScalar(string path, string? aVal, string? bVal, ProfileDiffResult result)
    {
        var aHas = !string.IsNullOrEmpty(aVal);
        var bHas = !string.IsNullOrEmpty(bVal);
        var aDisplay = aVal ?? string.Empty;
        var bDisplay = bVal ?? string.Empty;

        if (aHas && !bHas)
            result.Removed.Add(new ProfileDiffEntry(path, "removed", aDisplay, string.Empty));
        else if (!aHas && bHas)
            result.Added.Add(new ProfileDiffEntry(path, "added", string.Empty, bDisplay));
        else if (aHas && bHas && !string.Equals(aDisplay, bDisplay, StringComparison.Ordinal))
            result.Changed.Add(new ProfileDiffEntry(path, "changed", aDisplay, bDisplay));
    }

    /// <summary>
    /// Stable serialization for value comparison and for table cells. JSON keeps
    /// key order, and we compare the raw bytes so semantic equality reduces to
    /// textual equality — good enough for a structural diff and avoids the cost
    /// of a deep semantic comparator.
    /// </summary>
    private static readonly JsonSerializerOptions DiffJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static string SerializeForDiff<T>(T value) where T : class
        => JsonSerializer.Serialize(value, DiffJsonOpts);

    // ------------------------------------------------------------------
    // diff renderers
    // ------------------------------------------------------------------

    private static string RenderTable(ProfileDiffResult diff)
    {
        var sb = new StringBuilder();
        const string pathHeader = "Path";
        const string changeHeader = "Change";
        const string aHeader = "A";
        const string bHeader = "B";

        var rows = diff.All().ToList();
        if (rows.Count == 0)
        {
            sb.Append(pathHeader).Append("  ").Append(changeHeader)
                .Append("  ").Append(aHeader).Append("  ").Append(bHeader).Append('\n');
            sb.Append("(no differences)\n");
            return sb.ToString();
        }

        var pathW = Math.Max(pathHeader.Length, rows.Max(r => r.Path.Length));
        var changeW = Math.Max(changeHeader.Length, rows.Max(r => r.Change.Length));
        var aW = Math.Max(aHeader.Length, rows.Max(r => Truncate(r.A).Length));
        var bW = Math.Max(bHeader.Length, rows.Max(r => Truncate(r.B).Length));

        sb.Append(pathHeader.PadRight(pathW)).Append("  ")
            .Append(changeHeader.PadRight(changeW)).Append("  ")
            .Append(aHeader.PadRight(aW)).Append("  ")
            .Append(bHeader.PadRight(bW)).Append('\n');

        foreach (var row in rows)
        {
            sb.Append(row.Path.PadRight(pathW)).Append("  ")
                .Append(row.Change.PadRight(changeW)).Append("  ")
                .Append(Truncate(row.A).PadRight(aW)).Append("  ")
                .Append(Truncate(row.B).PadRight(bW)).Append('\n');
        }

        return sb.ToString();
    }

    private static string RenderMarkdown(ProfileDiffResult diff)
    {
        var sb = new StringBuilder();
        sb.Append("| Path | Change | A | B |\n");
        sb.Append("| --- | --- | --- | --- |\n");
        var rows = diff.All().ToList();
        foreach (var row in rows)
        {
            sb.Append("| ").Append(EscapeMd(row.Path))
                .Append(" | ").Append(EscapeMd(row.Change))
                .Append(" | ").Append(EscapeMd(Truncate(row.A)))
                .Append(" | ").Append(EscapeMd(Truncate(row.B)))
                .Append(" |\n");
        }
        return sb.ToString();
    }

    private static string RenderJson(ProfileDiffResult diff)
    {
        var payload = new
        {
            added = diff.Added.Select(e => new { path = e.Path, b = e.B }).ToArray(),
            removed = diff.Removed.Select(e => new { path = e.Path, a = e.A }).ToArray(),
            changed = diff.Changed.Select(e => new { path = e.Path, a = e.A, b = e.B }).ToArray(),
        };
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        return JsonSerializer.Serialize(payload, opts) + "\n";
    }

    /// <summary>
    /// Limit per-cell width so a profile with ten suppressions in one check
    /// does not produce a 4000-character table row. The full payload is still
    /// available in the JSON renderer.
    /// </summary>
    private static string Truncate(string value)
    {
        const int max = 80;
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= max) return value;
        return value.Substring(0, max - 3) + "...";
    }

    private static string EscapeMd(string value)
    {
        // Pipe and newline are the load-bearing characters in a Markdown pipe
        // table; escape pipes so cells stay aligned, and replace newlines with
        // spaces so multi-line JSON values do not break the row count.
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    // ------------------------------------------------------------------
    // install
    // ------------------------------------------------------------------

    private static Command CreateInstallCommand()
    {
        var urlArg = new Argument<string>("git-url", "Git URL of the profile bundle (https://, ssh://, file://)");
        var refOpt = new Option<string?>("--ref", "Branch, tag, or commit SHA to check out (default: remote HEAD)");
        var subPathOpt = new Option<string?>("--subpath", "Path inside the repository to copy out (default: full tree)");
        var targetOpt = new Option<string?>("--target",
            "Override the install directory (default: .revitcli/profiles/<derived-name>)");
        var forceOpt = new Option<bool>("--force", () => false,
            "Overwrite the target directory if it already exists");

        var cmd = new Command("install", "Shallow-clone a remote profile bundle into .revitcli/profiles/")
        {
            urlArg,
            refOpt,
            subPathOpt,
            targetOpt,
            forceOpt,
        };

        cmd.SetHandler(async (string url, string? refSpec, string? subPath, string? target, bool force) =>
        {
            Environment.ExitCode = await ExecuteInstallAsync(url, refSpec, subPath, target, force, Console.Out);
        }, urlArg, refOpt, subPathOpt, targetOpt, forceOpt);

        return cmd;
    }

    public static async Task<int> ExecuteInstallAsync(
        string gitUrl,
        string? refSpec,
        string? subPath,
        string? target,
        bool force,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            await output.WriteLineAsync("Error: git-url argument is required.");
            return 1;
        }

        // Default target: .revitcli/profiles/<derived>/. We compute it lazily
        // here (not in the installer) so the chosen path is visible in the
        // error message when --force is missing — the installer never sees the
        // null branch.
        var allowedRoot = Path.GetFullPath(Path.Combine(".revitcli", "profiles"));
        var allowedRootWithSep = allowedRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? allowedRoot
            : allowedRoot + Path.DirectorySeparatorChar;
        string finalTarget;
        if (!string.IsNullOrWhiteSpace(target))
        {
            finalTarget = Path.GetFullPath(target);
            // Confine --target to the workspace's .revitcli/profiles/ tree.
            // `profile install` should be incapable of writing outside the
            // workspace even when the user is convinced they want it to:
            // a stray `--target /etc` paired with `--force` would otherwise
            // happily overwrite system files using whatever permissions the
            // CLI has. Users who genuinely need a different on-disk layout
            // should symlink the .revitcli/profiles/ subdir, not pass an
            // arbitrary path here.
            if (!finalTarget.StartsWith(allowedRootWithSep, StringComparison.Ordinal)
                && !string.Equals(finalTarget, allowedRoot, StringComparison.Ordinal))
            {
                await output.WriteLineAsync(
                    $"Error: --target must resolve inside {allowedRootWithSep}; got '{finalTarget}'.");
                return 1;
            }
        }
        else
        {
            var name = ProfileInstaller.DeriveProfileName(gitUrl);
            // Suffix the ref so multiple revs of the same repo can coexist on
            // disk under .revitcli/profiles/, matching the roadmap's
            // "<name>@<ref>/" layout convention.
            if (!string.IsNullOrWhiteSpace(refSpec))
                name = $"{name}@{SanitizeRefForPath(refSpec!)}";
            finalTarget = Path.GetFullPath(Path.Combine(".revitcli", "profiles", name));
        }

        try
        {
            var result = await ProfileInstaller.InstallAsync(gitUrl, refSpec, subPath, finalTarget, force);
            await output.WriteLineAsync($"Installed profile from {result.SourceUrl}");
            if (!string.IsNullOrEmpty(result.CheckedOutRef))
                await output.WriteLineAsync($"  ref: {result.CheckedOutRef}");
            await output.WriteLineAsync($"  path: {result.TargetDir}");

            // Surface the suggested extends: line so the user can paste it
            // straight into their profile. Use a relative path when possible
            // so the snippet stays portable across machines.
            var cwd = Directory.GetCurrentDirectory();
            string extendsHint = result.TargetDir;
            try
            {
                extendsHint = Path.GetRelativePath(cwd, result.TargetDir).Replace('\\', '/');
            }
            catch { /* fall back to absolute path */ }

            await output.WriteLineAsync($"  add to your .revitcli.yml: extends: {extendsHint}/.revitcli.yml");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException
                                       or LibGit2Sharp.LibGit2SharpException
                                       or ArgumentException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Strip characters that would either break Windows path resolution or
    /// confuse downstream tooling that consumes <c>.revitcli/profiles/</c>.
    /// </summary>
    private static string SanitizeRefForPath(string refSpec)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = refSpec.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
        return new string(chars);
    }

    // ------------------------------------------------------------------
    // shared helpers
    // ------------------------------------------------------------------

    private static string ResolveProfilePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Profile not found: {full}", full);
            return full;
        }

        var discovered = ProfileLoader.Discover();
        if (discovered == null)
            throw new FileNotFoundException(
                $"No {ProfileLoader.FileName} found by walking up from {Directory.GetCurrentDirectory()}.");
        return discovered;
    }
}
