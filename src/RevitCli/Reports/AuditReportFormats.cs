using System;
using System.Collections.Generic;
using RevitCli.Shared;

namespace RevitCli.Reports;

/// <summary>
/// Integration seam shared by <c>CheckCommand</c> and <c>CompletionsCommand</c>
/// for the v1.7 CI report formats (SARIF + PR comment). Existing formats
/// (table / json / html) remain handled in their original sites — this helper
/// returns false for them so the legacy renderers stay authoritative.
/// </summary>
public static class AuditReportFormats
{
    public const string Sarif = "sarif";
    public const string PrComment = "pr-comment";

    /// <summary>
    /// Format names this helper knows how to render. Exposed so completions
    /// can extend their static value list without re-listing them.
    /// </summary>
    public static IReadOnlyList<string> SupportedFormats { get; } =
        new[] { Sarif, PrComment };

    /// <summary>
    /// Attempt to render <paramref name="issues"/> using the format identified
    /// by <paramref name="format"/>. Returns <c>true</c> when a v1.7 format
    /// was recognised, <c>false</c> otherwise (callers should fall through to
    /// their existing format switch).
    /// </summary>
    public static bool TryRender(
        string? format,
        IEnumerable<AuditIssue> issues,
        out string content)
        => TryRender(format, issues, documentPath: null, out content);

    /// <summary>
    /// Same as <see cref="TryRender(string?, IEnumerable{AuditIssue}, out string)"/>
    /// but lets callers thread the active document path through into the SARIF
    /// per-result property bag.
    /// </summary>
    public static bool TryRender(
        string? format,
        IEnumerable<AuditIssue> issues,
        string? documentPath,
        out string content)
    {
        content = "";
        if (string.IsNullOrWhiteSpace(format))
            return false;

        var normalized = format.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case Sarif:
                content = SarifWriter.Render(
                    issues,
                    new SarifWriterOptions { DocumentPath = documentPath });
                return true;
            case PrComment:
                content = PrCommentWriter.Render(issues);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the file extension hint a given format prefers when callers
    /// need to infer a default report path. Returns <c>null</c> for unknown
    /// formats so existing inference logic remains in control.
    /// </summary>
    public static string? GetDefaultExtension(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;

        return format.Trim().ToLowerInvariant() switch
        {
            Sarif => ".sarif",
            PrComment => ".md",
            _ => null
        };
    }

    /// <summary>
    /// Maps a report file extension back to a v1.7 format name. Used by
    /// CheckCommand's --report extension inference. Returns null when the
    /// extension is not one of ours.
    /// </summary>
    public static string? InferFormatFromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        var ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        return ext switch
        {
            ".sarif" => Sarif,
            // .md is the round-trip companion to GetDefaultExtension("pr-comment").
            // Without this case `revitcli check --report report.md` would fall
            // through to the default table renderer instead of PrCommentWriter.
            ".md" => PrComment,
            _ => null
        };
    }
}
