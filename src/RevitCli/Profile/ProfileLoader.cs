using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Profile;

public static class ProfileLoader
{
    public const string FileName = ".revitcli.yml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Discover .revitcli.yml by walking up from startDir.
    /// Returns null if not found.
    /// </summary>
    public static string? Discover(string? startDir = null)
    {
        var dir = startDir ?? Directory.GetCurrentDirectory();

        while (dir != null)
        {
            var candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Load and parse a profile from the given path.
    /// Resolves single- or multi-parent inheritance via <c>extends</c> with
    /// either replace (default) or deep-merge semantics.
    /// </summary>
    public static ProjectProfile Load(string path)
        => Load(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static ProjectProfile Load(string path, HashSet<string> visited)
    {
        var canonical = Path.GetFullPath(path);
        if (!visited.Add(canonical))
            throw new InvalidOperationException($"Circular profile inheritance detected: {canonical}");

        if (!File.Exists(canonical))
            throw new FileNotFoundException($"Profile not found: {canonical}");

        var yaml = File.ReadAllText(canonical);
        var profile = Deserializer.Deserialize<ProjectProfile>(yaml)
            ?? throw new InvalidOperationException($"Failed to parse profile: {canonical}");

        NormalizeExtends(profile, canonical);
        ValidateProfile(profile, canonical);

        // Resolve inheritance. Parents are merged left-to-right so the right-most
        // entry wins on conflicts under the existing replace semantics. The
        // single-string path stays byte-identical: ExtendsList has exactly one
        // element and the merge calls reduce to one Merge() call against the
        // child — same result as the previous implementation.
        if (profile.ExtendsList.Count > 0)
        {
            var baseDir = Path.GetDirectoryName(canonical)!;
            var strategy = profile.ExtendsStrategy;

            // Accumulate the merged base by folding parents in declaration order
            // so later entries override earlier ones (left-to-right).
            ProjectProfile? mergedBase = null;
            foreach (var rawParent in profile.ExtendsList)
            {
                var basePath = Path.GetFullPath(Path.Combine(baseDir, rawParent));

                // Path-escape guard: the parent must live under the same
                // directory as the child, matching the v1.0–v1.8 rule that
                // prevents a profile from reaching outside the project root via
                // ../../etc/passwd-style paths. Multi-extends does not relax it.
                if (!basePath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetDirectoryName(basePath), baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Profile 'extends' path escapes the profile directory: {rawParent}");
                }

                // Walk into the parent with the visited set carried through. This
                // is what catches DAG cycles: if grandparent loops back to a
                // already-visited node anywhere in the depth-first traversal we
                // throw at the offending edge.
                var loadedParent = Load(basePath, visited);
                mergedBase = mergedBase == null
                    ? loadedParent
                    : Merge(mergedBase, loadedParent, strategy);
            }

            profile = Merge(mergedBase!, profile, strategy);
        }

        // After leaving this branch of the DAG, allow other branches to revisit
        // the same parent (diamond inheritance is legal — only true cycles fail).
        visited.Remove(canonical);

        return profile;
    }

    /// <summary>
    /// Convert <see cref="ProjectProfile.ExtendsRaw"/> (string | sequence) into
    /// the canonical <see cref="ProjectProfile.ExtendsList"/>. Throws on shapes
    /// the loader does not understand so the user gets a clear error rather
    /// than a silent empty list.
    /// </summary>
    private static void NormalizeExtends(ProjectProfile profile, string path)
    {
        profile.ExtendsList.Clear();

        if (profile.ExtendsRaw == null)
            return;

        switch (profile.ExtendsRaw)
        {
            case string single when !string.IsNullOrWhiteSpace(single):
                profile.ExtendsList.Add(single);
                break;
            case string:
                // Empty string — treat as absence so 'extends:' followed by a
                // bare value still loads.
                break;
            case System.Collections.IEnumerable seq when profile.ExtendsRaw is not string:
                foreach (var item in seq)
                {
                    if (item == null)
                        throw new InvalidOperationException(
                            $"Profile {path}: extends list contains a null entry.");
                    var asString = item.ToString();
                    if (string.IsNullOrWhiteSpace(asString))
                        throw new InvalidOperationException(
                            $"Profile {path}: extends list contains an empty entry.");
                    profile.ExtendsList.Add(asString!);
                }
                if (profile.ExtendsList.Count == 0)
                    throw new InvalidOperationException(
                        $"Profile {path}: extends array must contain at least one entry.");
                break;
            default:
                throw new InvalidOperationException(
                    $"Profile {path}: extends must be a string or list of strings, got {profile.ExtendsRaw.GetType().Name}.");
        }
    }

    private static readonly HashSet<string> ValidSeverities = new(StringComparer.OrdinalIgnoreCase)
        { "error", "warning", "info" };

    private static readonly HashSet<string> ValidFailOn = new(StringComparer.OrdinalIgnoreCase)
        { "error", "warning" };

    private static readonly HashSet<string> ValidFixStrategies = new(StringComparer.OrdinalIgnoreCase)
        { "setParam", "renameByPattern" };

    private static readonly HashSet<string> ValidExtendsStrategies = new(StringComparer.OrdinalIgnoreCase)
        { "replace", "deep-merge" };

    private static void ValidateProfile(ProjectProfile profile, string path)
    {
        if (!string.IsNullOrEmpty(profile.ExtendsStrategyRaw)
            && !ValidExtendsStrategies.Contains(profile.ExtendsStrategyRaw))
        {
            throw new InvalidOperationException(
                $"Profile {path}: extendsStrategy must be 'replace' or 'deep-merge', got '{profile.ExtendsStrategyRaw}'.");
        }

        foreach (var (name, check) in profile.Checks)
        {
            if (!ValidFailOn.Contains(check.FailOn))
                throw new InvalidOperationException(
                    $"Profile {path}: checks.{name}.failOn must be 'error' or 'warning', got '{check.FailOn}'");

            foreach (var req in check.RequiredParameters)
            {
                if (!ValidSeverities.Contains(req.Severity))
                    throw new InvalidOperationException(
                        $"Profile {path}: checks.{name}.requiredParameters severity must be error/warning/info, got '{req.Severity}'");
            }

            foreach (var naming in check.Naming)
            {
                if (!ValidSeverities.Contains(naming.Severity))
                    throw new InvalidOperationException(
                        $"Profile {path}: checks.{name}.naming severity must be error/warning/info, got '{naming.Severity}'");
            }
        }

        ValidateFixes(profile, path);
    }

    private static void ValidateFixes(ProjectProfile profile, string path)
    {
        if (profile.Fixes == null)
            throw new InvalidOperationException($"Profile {path}: fixes must be a list");

        for (var i = 0; i < profile.Fixes.Count; i++)
        {
            var fix = profile.Fixes[i];
            var prefix = $"Profile {path}: fixes[{i}]";

            if (fix == null)
                throw new InvalidOperationException($"{prefix} must be an object");

            if (string.IsNullOrWhiteSpace(fix.Strategy))
                throw new InvalidOperationException($"{prefix}.strategy is required");

            if (!ValidFixStrategies.Contains(fix.Strategy))
                throw new InvalidOperationException(
                    $"{prefix}.strategy '{fix.Strategy}' is not supported. Supported strategies: setParam, renameByPattern");

            if (fix.MaxChanges.HasValue && fix.MaxChanges.Value <= 0)
                throw new InvalidOperationException($"{prefix}.maxChanges must be greater than 0");

            if (string.Equals(fix.Strategy, "setParam", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(fix.Parameter))
                    throw new InvalidOperationException($"{prefix}.parameter is required for setParam");
                if (fix.Value == null)
                    throw new InvalidOperationException($"{prefix}.value is required for setParam");
            }

            if (string.Equals(fix.Strategy, "renameByPattern", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(fix.Parameter))
                    throw new InvalidOperationException($"{prefix}.parameter is required for renameByPattern");
                if (string.IsNullOrWhiteSpace(fix.Match))
                    throw new InvalidOperationException($"{prefix}.match is required for renameByPattern");
                if (fix.Replace == null)
                    throw new InvalidOperationException($"{prefix}.replace is required for renameByPattern");
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(fix.Match);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"{prefix}.match is invalid regex: {ex.Message}", ex);
                }
            }
        }
    }

    /// <summary>
    /// Discover and load profile. Returns null if no profile found.
    /// </summary>
    public static ProjectProfile? DiscoverAndLoad(string? startDir = null)
    {
        var path = Discover(startDir);
        if (path == null)
            return null;

        return Load(path);
    }

    /// <summary>
    /// Merge base profile with child according to the requested strategy.
    /// <para>
    /// <see cref="ExtendsStrategy.Replace"/> (default): defaults field-merge,
    /// dictionaries are union'd by key with the child overwriting on conflict
    /// (named entries are not deep-merged), fixes append. This is the
    /// byte-identical v1.0–v1.8 behaviour.
    /// </para>
    /// <para>
    /// <see cref="ExtendsStrategy.DeepMerge"/>: same shape, but for the four
    /// dictionary sections (checks, exports, publish, schedules) the entries
    /// are deep-merged on collision — child fields win, parent fields absent
    /// from the child are inherited, list-typed fields under each entry
    /// concatenate (parent first, then child).
    /// </para>
    /// </summary>
    private static ProjectProfile Merge(ProjectProfile baseProfile, ProjectProfile child, ExtendsStrategy strategy)
    {
        var merged = new ProjectProfile
        {
            Version = child.Version > 0 ? child.Version : baseProfile.Version,
            // Strategy on the child wins so a deep-merge child can pull in a
            // replace-mode parent without the parent's strategy leaking back.
            ExtendsStrategyRaw = child.ExtendsStrategyRaw ?? baseProfile.ExtendsStrategyRaw,
            Defaults = new ProfileDefaults
            {
                OutputDir = child.Defaults.OutputDir ?? baseProfile.Defaults.OutputDir,
                Notify = child.Defaults.Notify ?? baseProfile.Defaults.Notify,
            },
        };

        if (strategy == ExtendsStrategy.DeepMerge)
        {
            DeepMergeDict(merged.Checks, baseProfile.Checks, child.Checks, MergeCheck);
            DeepMergeDict(merged.Exports, baseProfile.Exports, child.Exports, MergeExport);
            DeepMergeDict(merged.Publish, baseProfile.Publish, child.Publish, MergePublish);
            DeepMergeDict(merged.Schedules, baseProfile.Schedules, child.Schedules, MergeSchedule);
        }
        else
        {
            // Replace semantics: child entry wholly replaces parent entry on key
            // collision. We still inherit parent-only keys so a child that
            // narrows the surface does not lose the rest of the parent.
            foreach (var kvp in baseProfile.Checks) merged.Checks[kvp.Key] = kvp.Value;
            foreach (var kvp in child.Checks) merged.Checks[kvp.Key] = kvp.Value;

            foreach (var kvp in baseProfile.Exports) merged.Exports[kvp.Key] = kvp.Value;
            foreach (var kvp in child.Exports) merged.Exports[kvp.Key] = kvp.Value;

            foreach (var kvp in baseProfile.Publish) merged.Publish[kvp.Key] = kvp.Value;
            foreach (var kvp in child.Publish) merged.Publish[kvp.Key] = kvp.Value;

            foreach (var kvp in baseProfile.Schedules) merged.Schedules[kvp.Key] = kvp.Value;
            foreach (var kvp in child.Schedules) merged.Schedules[kvp.Key] = kvp.Value;
        }

        merged.Fixes.AddRange(baseProfile.Fixes);
        merged.Fixes.AddRange(child.Fixes);

        return merged;
    }

    /// <summary>
    /// Merge two same-keyed dictionaries by key. Keys present in only one side
    /// pass through; keys present in both go through the per-entry merger so
    /// list fields can be concatenated cleanly.
    /// </summary>
    private static void DeepMergeDict<TValue>(
        IDictionary<string, TValue> destination,
        IDictionary<string, TValue> baseDict,
        IDictionary<string, TValue> childDict,
        Func<TValue, TValue, TValue> entryMerger)
    {
        foreach (var kvp in baseDict)
            destination[kvp.Key] = kvp.Value;

        foreach (var kvp in childDict)
        {
            if (destination.TryGetValue(kvp.Key, out var existing))
                destination[kvp.Key] = entryMerger(existing, kvp.Value);
            else
                destination[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Per-check deep merge: scalar fields use child-wins; list fields
    /// concatenate parent first, then child. Suppressions append the same way
    /// so a child that adds a one-off suppression does not erase the parent's
    /// shared list.
    /// </summary>
    private static CheckDefinition MergeCheck(CheckDefinition baseCheck, CheckDefinition childCheck)
    {
        var result = new CheckDefinition
        {
            // FailOn always carries a value from the deserializer because it
            // has a constructor default; treating "error" as a sentinel for
            // "absent" would be wrong, so we take the child's value verbatim
            // — this matches the documented child-wins-on-conflict rule.
            FailOn = !string.IsNullOrEmpty(childCheck.FailOn) ? childCheck.FailOn : baseCheck.FailOn,
        };
        result.AuditRules.AddRange(baseCheck.AuditRules);
        result.AuditRules.AddRange(childCheck.AuditRules);
        result.RequiredParameters.AddRange(baseCheck.RequiredParameters);
        result.RequiredParameters.AddRange(childCheck.RequiredParameters);
        result.Naming.AddRange(baseCheck.Naming);
        result.Naming.AddRange(childCheck.Naming);
        result.Suppressions.AddRange(baseCheck.Suppressions);
        result.Suppressions.AddRange(childCheck.Suppressions);
        return result;
    }

    private static ExportPreset MergeExport(ExportPreset baseExport, ExportPreset childExport)
    {
        return new ExportPreset
        {
            Format = !string.IsNullOrEmpty(childExport.Format) ? childExport.Format : baseExport.Format,
            // Lists: child wins outright when set so a child that wants to
            // narrow the sheet list does not silently inherit the parent's.
            // This matches the field-level child-wins rule and matches how
            // users typically write override layers.
            Sheets = childExport.Sheets ?? baseExport.Sheets,
            Views = childExport.Views ?? baseExport.Views,
            OutputDir = childExport.OutputDir ?? baseExport.OutputDir,
        };
    }

    private static PublishPipeline MergePublish(PublishPipeline basePub, PublishPipeline childPub)
    {
        var result = new PublishPipeline
        {
            Precheck = childPub.Precheck ?? basePub.Precheck,
            // Incremental: bool POCOs default to false, so the deserializer
            // cannot tell "child omitted the field" from "child wrote
            // incremental: false". The previous tautological ternary
            // collapses to `||`, which is what we keep here. Documented
            // limitation: once any ancestor enables incremental, descendants
            // cannot opt back out; promote the field to bool? if a future
            // PR needs explicit child override.
            Incremental = childPub.Incremental || basePub.Incremental,
            BaselinePath = childPub.BaselinePath ?? basePub.BaselinePath,
            // SinceMode: child-wins when child sets ANY non-empty value,
            // including the literal "content" (the previous code excluded
            // "content" from the child branch, so a child explicitly
            // reverting to the default would be silently overridden by a
            // parent's "meta" — broken child-wins semantics).
            SinceMode = !string.IsNullOrEmpty(childPub.SinceMode)
                ? childPub.SinceMode
                : (!string.IsNullOrEmpty(basePub.SinceMode) ? basePub.SinceMode : "content"),
        };
        // Presets concatenate so a child can append additional output formats
        // to the parent's pipeline without re-listing them.
        result.Presets.AddRange(basePub.Presets);
        result.Presets.AddRange(childPub.Presets);
        return result;
    }

    private static ScheduleTemplate MergeSchedule(ScheduleTemplate baseSched, ScheduleTemplate childSched)
    {
        return new ScheduleTemplate
        {
            Category = !string.IsNullOrEmpty(childSched.Category) ? childSched.Category : baseSched.Category,
            Fields = childSched.Fields ?? baseSched.Fields,
            Filter = childSched.Filter ?? baseSched.Filter,
            Sort = childSched.Sort ?? baseSched.Sort,
            SortDescending = childSched.SortDescending || baseSched.SortDescending,
            Name = childSched.Name ?? baseSched.Name,
        };
    }
}
