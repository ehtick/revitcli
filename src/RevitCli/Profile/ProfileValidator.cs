using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCli.Profile;

/// <summary>
/// Pure validator over an already-loaded <see cref="ProjectProfile"/>.
///
/// The constructor-free entry point is <see cref="Validate"/>. The check set
/// covers reference completeness, severity-override sanity, and orphan
/// detection. Schema-level validation (e.g. unknown <c>failOn</c> values) is
/// already enforced by <see cref="ProfileLoader"/> on Load and surfaces as
/// thrown exceptions instead of issue list entries — by the time a profile
/// reaches us it has cleared that bar.
/// </summary>
public static class ProfileValidator
{
    private static readonly HashSet<string> ValidSeverities = new(StringComparer.Ordinal)
    {
        "error",
        "warning",
        "info",
    };

    private static readonly HashSet<string> ValidFailOn = new(StringComparer.Ordinal)
    {
        "error",
        "warning",
    };

    /// <summary>
    /// Run every validation check against <paramref name="profile"/> and
    /// return the issues. The list is ordered: errors first, then warnings,
    /// then info; within a severity, by <see cref="ProfileValidationIssue.Path"/>.
    /// </summary>
    public static IReadOnlyList<ProfileValidationIssue> Validate(ProjectProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        var issues = new List<ProfileValidationIssue>();

        ValidateChecks(profile, issues);
        ValidatePublish(profile, issues);
        ValidateOrphans(profile, issues);

        return issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Path, StringComparer.Ordinal)
            .ThenBy(i => i.Message, StringComparer.Ordinal)
            .ToList();
    }

    // ------------------------------------------------------------------
    // checks.* — failOn + per-rule severity
    // ------------------------------------------------------------------

    private static void ValidateChecks(ProjectProfile profile, List<ProfileValidationIssue> issues)
    {
        foreach (var (checkName, check) in profile.Checks)
        {
            var checkPath = $"checks.{checkName}";

            // failOn is normally enforced by the loader, but defend in depth: if
            // we are handed a profile constructed in-memory the loader's check
            // never ran and we still need to flag bad values.
            if (!string.IsNullOrEmpty(check.FailOn) && !ValidFailOn.Contains(check.FailOn))
            {
                issues.Add(new ProfileValidationIssue(
                    ProfileValidationSeverity.Error,
                    $"{checkPath}.failOn",
                    $"failOn must be 'error' or 'warning', got '{check.FailOn}'."));
            }

            // Severity overrides on individual rules (requiredParameters / naming).
            // Each entry effectively names a rule via category+parameter or target;
            // if the entry references something the check has no other coverage of
            // we treat that as a warning ("dead rule").
            for (var i = 0; i < check.RequiredParameters.Count; i++)
            {
                var req = check.RequiredParameters[i];
                var path = $"{checkPath}.requiredParameters[{i}].severity";
                if (!string.IsNullOrEmpty(req.Severity) && !ValidSeverities.Contains(req.Severity))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Error,
                        path,
                        $"severity must be error|warning|info, got '{req.Severity}'."));
                }

                if (string.IsNullOrWhiteSpace(req.Category) || string.IsNullOrWhiteSpace(req.Parameter))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Warning,
                        $"{checkPath}.requiredParameters[{i}]",
                        "severity override has no category/parameter to apply to (dead rule)."));
                }
            }

            for (var i = 0; i < check.Naming.Count; i++)
            {
                var naming = check.Naming[i];
                var path = $"{checkPath}.naming[{i}].severity";
                if (!string.IsNullOrEmpty(naming.Severity) && !ValidSeverities.Contains(naming.Severity))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Error,
                        path,
                        $"severity must be error|warning|info, got '{naming.Severity}'."));
                }

                if (string.IsNullOrWhiteSpace(naming.Target) || string.IsNullOrWhiteSpace(naming.Pattern))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Warning,
                        $"{checkPath}.naming[{i}]",
                        "naming rule lacks target or pattern (dead rule — will never fire)."));
                }
            }

            // auditRules sanity: blank rule name slips through the loader because
            // it does not introspect rule names; flag it here.
            for (var i = 0; i < check.AuditRules.Count; i++)
            {
                var rule = check.AuditRules[i];
                if (string.IsNullOrWhiteSpace(rule.Rule))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Warning,
                        $"{checkPath}.auditRules[{i}].rule",
                        "auditRules entry has empty rule name — it will never match an audit rule."));
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // publish.* — precheck + presets references
    // ------------------------------------------------------------------

    private static void ValidatePublish(ProjectProfile profile, List<ProfileValidationIssue> issues)
    {
        foreach (var (pipelineName, pipeline) in profile.Publish)
        {
            var pipelinePath = $"publish.{pipelineName}";

            if (!string.IsNullOrWhiteSpace(pipeline.Precheck))
            {
                if (!profile.Checks.ContainsKey(pipeline.Precheck))
                {
                    var available = profile.Checks.Count == 0
                        ? "(none defined)"
                        : string.Join(", ", profile.Checks.Keys.OrderBy(k => k, StringComparer.Ordinal));
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Error,
                        $"{pipelinePath}.precheck",
                        $"precheck '{pipeline.Precheck}' does not exist under checks.* (available: {available})."));
                }
            }

            for (var i = 0; i < pipeline.Presets.Count; i++)
            {
                var preset = pipeline.Presets[i];
                if (string.IsNullOrWhiteSpace(preset))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Warning,
                        $"{pipelinePath}.presets[{i}]",
                        "preset entry is empty."));
                    continue;
                }

                if (!profile.Exports.ContainsKey(preset))
                {
                    var available = profile.Exports.Count == 0
                        ? "(none defined)"
                        : string.Join(", ", profile.Exports.Keys.OrderBy(k => k, StringComparer.Ordinal));
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Error,
                        $"{pipelinePath}.presets[{i}]",
                        $"preset '{preset}' does not exist under exports.* (available: {available})."));
                }
            }

            if (pipeline.Presets.Count == 0)
            {
                issues.Add(new ProfileValidationIssue(
                    ProfileValidationSeverity.Warning,
                    $"{pipelinePath}.presets",
                    "publish pipeline has no presets — running it will produce no output."));
            }
        }
    }

    // ------------------------------------------------------------------
    // orphan check sets — info-level
    // ------------------------------------------------------------------

    private static void ValidateOrphans(ProjectProfile profile, List<ProfileValidationIssue> issues)
    {
        if (profile.Checks.Count == 0)
            return;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pipeline in profile.Publish.Values)
        {
            if (!string.IsNullOrWhiteSpace(pipeline.Precheck))
                referenced.Add(pipeline.Precheck);
        }

        // 'default' check is the implicit target of `revitcli check` with no
        // arguments — not orphaned even when nothing references it.
        referenced.Add("default");

        foreach (var checkName in profile.Checks.Keys)
        {
            if (!referenced.Contains(checkName))
            {
                issues.Add(new ProfileValidationIssue(
                    ProfileValidationSeverity.Info,
                    $"checks.{checkName}",
                    "no publish pipeline references this check set (orphan)."));
            }
        }

        // Orphan exports: not bound to any publish.presets entry.
        if (profile.Exports.Count > 0)
        {
            var usedExports = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pipeline in profile.Publish.Values)
            {
                foreach (var preset in pipeline.Presets)
                {
                    if (!string.IsNullOrWhiteSpace(preset))
                        usedExports.Add(preset);
                }
            }

            foreach (var exportName in profile.Exports.Keys)
            {
                if (!usedExports.Contains(exportName))
                {
                    issues.Add(new ProfileValidationIssue(
                        ProfileValidationSeverity.Info,
                        $"exports.{exportName}",
                        "no publish pipeline references this export preset (orphan)."));
                }
            }
        }
    }
}
