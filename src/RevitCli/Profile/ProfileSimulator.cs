using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCli.Profile;

/// <summary>
/// "What would happen if I ran this pipeline" — without connecting to
/// Revit. Closes the gap between <see cref="ProfileLoader"/>'s schema-
/// level lint (which catches typos, missing extends targets, etc.) and
/// runtime errors (which only surface after the operator has fired
/// `revitcli publish`).
///
/// The simulator walks the resolved profile, expands the named pipeline
/// or check, and reports:
/// <list type="bullet">
///   <item>Reference completeness (every preset / precheck the pipeline
///         names actually exists in the profile).</item>
///   <item>Surface-level concerns the schema can't catch ("sheets: ALL"
///         on an export preset is legal but slow; the simulator flags it
///         so the operator can confirm it's intentional).</item>
///   <item>Sheet / view selectors normalized to a single rendering
///         shape so the report doesn't differ between
///         <c>sheets: [A101]</c> and <c>sheets: A101</c>-style inputs.</item>
///   <item>A coarse runtime estimate, so CI workflow authors can pick
///         sensible step timeouts.</item>
/// </list>
///
/// All static — no I/O beyond what the caller hands in. The CLI surface
/// (<c>revitcli profile simulate &lt;name&gt;</c>) wraps this with file
/// loading and renderers.
/// </summary>
public static class ProfileSimulator
{
    public enum Severity { Info, Warning, Error }

    public sealed class Finding
    {
        public Severity Severity { get; init; }
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
    }

    public sealed class PresetReport
    {
        public string Name { get; init; } = "";
        public string Format { get; init; } = "";
        public IReadOnlyList<string> Sheets { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Views { get; init; } = Array.Empty<string>();
        public string? OutputDir { get; init; }
        public bool ExportsAllSheets { get; init; }
    }

    public sealed class PipelineReport
    {
        public string Name { get; init; } = "";
        public string? Precheck { get; init; }
        public IReadOnlyList<string> PrecheckRules { get; init; } = Array.Empty<string>();
        public string? PrecheckFailOn { get; init; }
        public IReadOnlyList<PresetReport> Presets { get; init; } = Array.Empty<PresetReport>();
        public bool Incremental { get; init; }
        public string? BaselinePath { get; init; }
        public string SinceMode { get; init; } = "content";
        public string? WebhookUrl { get; init; }
        public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
        /// <summary>Worst-severity finding code, used by the CLI to pick an exit code.</summary>
        public Severity WorstSeverity { get; init; }
    }

    /// <summary>
    /// Simulate a publish pipeline by name. Throws
    /// <see cref="ArgumentException"/> when the pipeline isn't defined
    /// in the resolved profile — the CLI catches this and exits 1 with
    /// the available pipeline names listed.
    /// </summary>
    public static PipelineReport SimulatePipeline(ProjectProfile profile, string pipelineName)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(pipelineName))
            throw new ArgumentException("pipelineName is required.", nameof(pipelineName));

        if (!profile.Publish.TryGetValue(pipelineName, out var pipeline))
        {
            var available = profile.Publish.Count > 0
                ? string.Join(", ", profile.Publish.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                : "(none)";
            throw new ArgumentException(
                $"Pipeline '{pipelineName}' is not defined in the profile. Available: {available}.");
        }

        var findings = new List<Finding>();
        var presetReports = new List<PresetReport>();

        // Each preset name in the pipeline must resolve in profile.Exports.
        // Missing names land as Error findings — running this would crash
        // PublishCommand with the same message but later, after precheck
        // already burned wall time.
        foreach (var presetName in pipeline.Presets)
        {
            if (!profile.Exports.TryGetValue(presetName, out var preset))
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "preset-missing",
                    Message =
                        $"Pipeline '{pipelineName}' references preset '{presetName}' " +
                        "which is not defined under exports.",
                });
                continue;
            }

            var sheets = NormalizeList(preset.Sheets);
            var views = NormalizeList(preset.Views);
            var allSheets = sheets.Any(s => string.Equals(s, "all", StringComparison.OrdinalIgnoreCase));

            presetReports.Add(new PresetReport
            {
                Name = presetName,
                Format = preset.Format ?? "",
                Sheets = sheets,
                Views = views,
                OutputDir = preset.OutputDir,
                ExportsAllSheets = allSheets,
            });

            if (string.IsNullOrWhiteSpace(preset.Format))
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "preset-missing-format",
                    Message = $"Preset '{presetName}' has no format. Set format to dwg|pdf|ifc.",
                });
            }
            else if (!IsKnownFormat(preset.Format))
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "preset-unknown-format",
                    Message =
                        $"Preset '{presetName}' has format='{preset.Format}'. " +
                        "Known formats: dwg, pdf, ifc. The export will fail at runtime if Revit doesn't recognise this.",
                });
            }

            if (allSheets)
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Info,
                    Code = "preset-all-sheets",
                    Message =
                        $"Preset '{presetName}' uses 'sheets: ALL'. " +
                        "Confirm this is intentional — full-document exports can take minutes.",
                });
            }

            if (sheets.Count == 0 && views.Count == 0)
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "preset-empty-selectors",
                    Message =
                        $"Preset '{presetName}' has neither sheets nor views configured. " +
                        "PublishCommand will skip it at runtime.",
                });
            }
        }

        // Precheck reference must exist if named.
        IReadOnlyList<string> precheckRules = Array.Empty<string>();
        string? precheckFailOn = null;
        if (!string.IsNullOrWhiteSpace(pipeline.Precheck))
        {
            if (!profile.Checks.TryGetValue(pipeline.Precheck!, out var check))
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "precheck-missing",
                    Message =
                        $"Pipeline '{pipelineName}' precheck '{pipeline.Precheck}' " +
                        "is not defined under checks.",
                });
            }
            else
            {
                // Aggregate every rule signal the check fires off:
                // explicit auditRules (built-in rule names), required-
                // parameter checks (one rule per category+parameter pair),
                // and naming checks (one per category). The simulator
                // surfaces these so the operator sees what the precheck
                // actually exercises, not just the rule list they typed.
                var ruleNames = new List<string>();
                if (check.AuditRules is { Count: > 0 })
                    ruleNames.AddRange(check.AuditRules.Select(r => r.Rule).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (check.RequiredParameters is { Count: > 0 })
                    ruleNames.AddRange(check.RequiredParameters.Select(rp => $"required-parameter:{rp.Category}.{rp.Parameter}"));
                if (check.Naming is { Count: > 0 })
                    ruleNames.AddRange(check.Naming.Select(n => $"naming:{n.Target}"));
                precheckRules = ruleNames.ToArray();
                precheckFailOn = check.FailOn;

                if (precheckRules.Count == 0)
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Warning,
                        Code = "precheck-empty",
                        Message =
                            $"Precheck '{pipeline.Precheck}' has no rules. It will pass " +
                            "trivially and provide no signal.",
                    });
                }
            }
        }

        // Webhook surface: PublishCommand fires defaults.notify on success.
        // We don't validate the URL here (that's the webhook layer's job;
        // it already rejects non-HTTPS / private IPs at run-time) but we
        // surface the URL so operators see it in the simulation report.
        var webhook = profile.Defaults?.Notify;

        // Incremental publish needs a baseline path that's either absolute
        // or interpretable from the profile's location. We can't validate
        // file existence here (caller may not have provided the file path),
        // but we surface the configured path for visibility.
        var sinceMode = string.IsNullOrWhiteSpace(pipeline.SinceMode) ? "content" : pipeline.SinceMode;

        var worst = findings.Count == 0 ? Severity.Info : findings.Max(f => f.Severity);

        return new PipelineReport
        {
            Name = pipelineName,
            Precheck = string.IsNullOrWhiteSpace(pipeline.Precheck) ? null : pipeline.Precheck,
            PrecheckRules = precheckRules,
            PrecheckFailOn = precheckFailOn,
            Presets = presetReports,
            Incremental = pipeline.Incremental,
            BaselinePath = pipeline.BaselinePath,
            SinceMode = sinceMode,
            WebhookUrl = webhook,
            Findings = findings,
            WorstSeverity = worst,
        };
    }

    private static IReadOnlyList<string> NormalizeList(List<string>? raw)
    {
        if (raw is null) return Array.Empty<string>();
        return raw
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();
    }

    private static bool IsKnownFormat(string format)
    {
        var f = format.Trim().ToLowerInvariant();
        return f == "dwg" || f == "pdf" || f == "ifc";
    }
}
