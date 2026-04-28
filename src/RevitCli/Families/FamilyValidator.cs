using System.Collections.Generic;
using System.Linq;
using RevitCli.Shared;

namespace RevitCli.Families;

/// <summary>
/// Local rule engine for <c>revitcli family validate</c>. Runs against
/// a list of <see cref="FamilyInfo"/> already fetched from the addin —
/// no addin round-trip beyond the initial list.
///
/// Rules are intentionally built-in for v1.8. They cover invariants
/// that are cheap to check and where violations almost always indicate
/// a real problem (corrupted import, copy-paste from a non-conforming
/// project, etc.). Profile-driven rules — regex name patterns, custom
/// category allowlists, required shared parameters — are deferred to
/// a future "family governance" PR; they need profile schema changes
/// AND deeper family introspection from the addin (parameter lists,
/// nested family contents).
///
/// Built-in rules:
///   <c>name-non-empty</c>      — Family.Name must be non-empty.
///   <c>name-no-path-chars</c>  — Family.Name must not contain
///                                / \ : * ? " &lt; &gt; |
///                                (the cross-platform "invalid file
///                                name" set; matters because <c>family
///                                export</c> uses Family.Name as the
///                                base of the .rfa filename).
///   <c>category-known</c>      — Category must not be empty or
///                                "&lt;none&gt;" / "Unknown" — those
///                                signal a corrupted import.
///   <c>loadable-or-in-place</c>— Exactly one of IsLoadable / IsInPlace
///                                must be true. Both true is impossible
///                                in Revit; both false means the family
///                                is in an inconsistent state.
/// </summary>
public static class FamilyValidator
{
    /// <summary>The set of rule ids supported by this validator.</summary>
    public static readonly IReadOnlyList<string> AllRuleIds = new[]
    {
        "name-non-empty",
        "name-no-path-chars",
        "category-known",
        "loadable-or-in-place",
    };

    private static readonly char[] PathIllegalChars =
        { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    private static readonly string[] PlaceholderCategories =
        { "", "<none>", "Unknown", "(none)" };

    /// <summary>
    /// Run validation against <paramref name="families"/>. If
    /// <paramref name="enabledRules"/> is null or empty, all rules run;
    /// otherwise only the listed ones (case-insensitive).
    /// </summary>
    public static List<FamilyValidationIssue> Validate(
        IReadOnlyList<FamilyInfo> families,
        IReadOnlyCollection<string>? enabledRules = null)
    {
        var rules = ResolveEnabled(enabledRules);
        var issues = new List<FamilyValidationIssue>();
        foreach (var family in families)
        {
            if (rules.Contains("name-non-empty") && string.IsNullOrWhiteSpace(family.Name))
            {
                issues.Add(MakeIssue(family, "name-non-empty", "error",
                    $"Family id={family.Id} has an empty Name."));
            }

            if (rules.Contains("name-no-path-chars") && !string.IsNullOrEmpty(family.Name)
                && family.Name.IndexOfAny(PathIllegalChars) >= 0)
            {
                issues.Add(MakeIssue(family, "name-no-path-chars", "error",
                    $"Family Name '{family.Name}' contains a character that breaks " +
                    $"filesystem export (one of: / \\ : * ? \" < > |)."));
            }

            if (rules.Contains("category-known")
                && PlaceholderCategories.Contains(family.Category ?? "", System.StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(MakeIssue(family, "category-known", "warning",
                    $"Family '{family.Name}' has placeholder Category '{family.Category}' — " +
                    "likely a corrupted import."));
            }

            if (rules.Contains("loadable-or-in-place"))
            {
                if (family.IsLoadable && family.IsInPlace)
                {
                    issues.Add(MakeIssue(family, "loadable-or-in-place", "error",
                        $"Family '{family.Name}' is reported as BOTH loadable and in-place — " +
                        "this is impossible in Revit; the addin returned inconsistent state."));
                }
                else if (!family.IsLoadable && !family.IsInPlace)
                {
                    issues.Add(MakeIssue(family, "loadable-or-in-place", "warning",
                        $"Family '{family.Name}' is neither loadable nor in-place — " +
                        "may indicate a corrupted family element."));
                }
            }
        }
        return issues;
    }

    private static HashSet<string> ResolveEnabled(IReadOnlyCollection<string>? enabledRules)
    {
        if (enabledRules == null || enabledRules.Count == 0)
            return new HashSet<string>(AllRuleIds, System.StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(enabledRules, System.StringComparer.OrdinalIgnoreCase);
    }

    private static FamilyValidationIssue MakeIssue(FamilyInfo family, string rule, string severity, string message)
        => new()
        {
            FamilyId = family.Id,
            FamilyName = family.Name,
            Category = family.Category,
            Rule = rule,
            Severity = severity,
            Message = message,
        };
}
