using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace RevitCli.Profile;

public class ProjectProfile
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Raw YAML node for <c>extends</c>. Accepted forms:
    /// <list type="bullet">
    ///   <item>Single string — <c>extends: ./parent.yml</c> (legacy v1.0–v1.8 form, default).</item>
    ///   <item>Sequence of strings — <c>extends: [./base.yml, ./team.yml]</c> (v1.9 multi-extends).</item>
    /// </list>
    /// Normalized into <see cref="ExtendsList"/> by <see cref="ProfileLoader"/>
    /// after deserialization. Consumers should prefer <see cref="ExtendsList"/>
    /// (always populated) or the legacy <see cref="Extends"/> (first parent).
    /// </summary>
    [YamlMember(Alias = "extends")]
    public object? ExtendsRaw { get; set; }

    /// <summary>
    /// Inheritance merge strategy as written in YAML. Accepted values:
    /// <c>replace</c> (default, historical v1.0–v1.8 behaviour) and
    /// <c>deep-merge</c> (v1.9 opt-in additive merge). Validated by the loader;
    /// consumers should read the typed <see cref="ExtendsStrategy"/> property.
    /// </summary>
    [YamlMember(Alias = "extendsStrategy")]
    public string? ExtendsStrategyRaw { get; set; }

    /// <summary>
    /// Typed view of <see cref="ExtendsStrategyRaw"/>. Defaults to
    /// <see cref="Profile.ExtendsStrategy.Replace"/> when unset or unrecognized.
    /// The loader rejects unrecognized literals up front so consumers reading
    /// this getter in production never see the fallback path.
    /// </summary>
    [YamlIgnore]
    public ExtendsStrategy ExtendsStrategy =>
        string.Equals(ExtendsStrategyRaw, "deep-merge", System.StringComparison.OrdinalIgnoreCase)
            ? ExtendsStrategy.DeepMerge
            : ExtendsStrategy.Replace;

    [YamlMember(Alias = "defaults")]
    public ProfileDefaults Defaults { get; set; } = new();

    [YamlMember(Alias = "checks")]
    public Dictionary<string, CheckDefinition> Checks { get; set; } = new();

    [YamlMember(Alias = "exports")]
    public Dictionary<string, ExportPreset> Exports { get; set; } = new();

    [YamlMember(Alias = "publish")]
    public Dictionary<string, PublishPipeline> Publish { get; set; } = new();

    [YamlMember(Alias = "schedules")]
    public Dictionary<string, ScheduleTemplate> Schedules { get; set; } = new();

    [YamlMember(Alias = "fixes")]
    public List<FixRecipe> Fixes { get; set; } = new();

    /// <summary>
    /// Normalized list of <c>extends</c> entries in declaration order. For a
    /// single-string extends, this contains exactly one entry; for an array
    /// extends, it contains every entry verbatim. Empty when no extends clause
    /// is present. Populated by <see cref="ProfileLoader"/> on Load.
    /// </summary>
    [YamlIgnore]
    public List<string> ExtendsList { get; set; } = new();

    /// <summary>
    /// Legacy single-parent accessor. Returns the first entry of
    /// <see cref="ExtendsList"/> or <c>null</c> when none is defined. Existing
    /// code that read <c>profile.Extends</c> as a string keeps working unchanged
    /// — for multi-extends profiles it sees the first parent (left-most).
    /// </summary>
    [YamlIgnore]
    public string? Extends => ExtendsList.Count > 0 ? ExtendsList[0] : null;
}

/// <summary>
/// Inheritance merge strategy for a profile that uses <c>extends</c>.
/// </summary>
public enum ExtendsStrategy
{
    /// <summary>
    /// Default. Each named entry under <c>checks</c>, <c>publish</c>,
    /// <c>exports</c>, and <c>schedules</c> is replaced wholesale by the child;
    /// <c>defaults</c> and <c>fixes</c> are field-merged / appended exactly as
    /// in v1.0–v1.8 (byte-identical behaviour).
    /// </summary>
    Replace = 0,

    /// <summary>
    /// Opt-in. Named entries deep-merge by key — child wins on conflict, parent
    /// keys absent from the child are inherited.
    /// </summary>
    DeepMerge = 1,
}

public class ProfileDefaults
{
    [YamlMember(Alias = "outputDir")]
    public string? OutputDir { get; set; }

    [YamlMember(Alias = "notify")]
    public string? Notify { get; set; }
}

public class CheckDefinition
{
    [YamlMember(Alias = "failOn")]
    public string FailOn { get; set; } = "error";

    [YamlMember(Alias = "auditRules")]
    public List<AuditRuleRef> AuditRules { get; set; } = new();

    [YamlMember(Alias = "requiredParameters")]
    public List<RequiredParameterCheck> RequiredParameters { get; set; } = new();

    [YamlMember(Alias = "naming")]
    public List<NamingCheck> Naming { get; set; } = new();

    [YamlMember(Alias = "suppressions")]
    public List<Suppression> Suppressions { get; set; } = new();
}

public class Suppression
{
    [YamlMember(Alias = "rule")]
    public string Rule { get; set; } = "";

    [YamlMember(Alias = "category")]
    public string? Category { get; set; }

    [YamlMember(Alias = "parameter")]
    public string? Parameter { get; set; }

    [YamlMember(Alias = "elementIds")]
    public List<long>? ElementIds { get; set; }

    [YamlMember(Alias = "reason")]
    public string? Reason { get; set; }

    [YamlMember(Alias = "expires")]
    public string? Expires { get; set; }
}

public class FixRecipe
{
    [YamlMember(Alias = "rule")]
    public string? Rule { get; set; }

    [YamlMember(Alias = "category")]
    public string? Category { get; set; }

    [YamlMember(Alias = "parameter")]
    public string? Parameter { get; set; }

    [YamlMember(Alias = "strategy")]
    public string Strategy { get; set; } = "";

    [YamlMember(Alias = "value")]
    public string? Value { get; set; }

    [YamlMember(Alias = "match")]
    public string? Match { get; set; }

    [YamlMember(Alias = "replace")]
    public string? Replace { get; set; }

    [YamlMember(Alias = "maxChanges")]
    public int? MaxChanges { get; set; }
}

public class AuditRuleRef
{
    [YamlMember(Alias = "rule")]
    public string Rule { get; set; } = "";
}

public class RequiredParameterCheck
{
    [YamlMember(Alias = "category")]
    public string Category { get; set; } = "";

    [YamlMember(Alias = "parameter")]
    public string Parameter { get; set; } = "";

    [YamlMember(Alias = "requireNonEmpty")]
    public bool RequireNonEmpty { get; set; } = true;

    [YamlMember(Alias = "severity")]
    public string Severity { get; set; } = "error";
}

public class NamingCheck
{
    [YamlMember(Alias = "target")]
    public string Target { get; set; } = "";

    [YamlMember(Alias = "pattern")]
    public string Pattern { get; set; } = "";

    [YamlMember(Alias = "severity")]
    public string Severity { get; set; } = "warning";
}

public class ExportPreset
{
    [YamlMember(Alias = "format")]
    public string Format { get; set; } = "";

    [YamlMember(Alias = "sheets")]
    public List<string>? Sheets { get; set; }

    [YamlMember(Alias = "views")]
    public List<string>? Views { get; set; }

    [YamlMember(Alias = "outputDir")]
    public string? OutputDir { get; set; }
}

public class PublishPipeline
{
    [YamlMember(Alias = "presets")]
    public List<string> Presets { get; set; } = new();

    [YamlMember(Alias = "precheck")]
    public string? Precheck { get; set; }

    [YamlMember(Alias = "incremental")]
    public bool Incremental { get; set; } = false;

    [YamlMember(Alias = "baselinePath")]
    public string? BaselinePath { get; set; }

    [YamlMember(Alias = "sinceMode")]
    public string SinceMode { get; set; } = "content";
}

public class ScheduleTemplate
{
    [YamlMember(Alias = "category")]
    public string Category { get; set; } = "";

    [YamlMember(Alias = "fields")]
    public List<string>? Fields { get; set; }

    [YamlMember(Alias = "filter")]
    public string? Filter { get; set; }

    [YamlMember(Alias = "sort")]
    public string? Sort { get; set; }

    [YamlMember(Alias = "sortDescending")]
    public bool SortDescending { get; set; }

    [YamlMember(Alias = "name")]
    public string? Name { get; set; }
}
