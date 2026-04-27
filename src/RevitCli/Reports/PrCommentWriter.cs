using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RevitCli.Shared;

namespace RevitCli.Reports;

/// <summary>
/// Options accepted by <see cref="PrCommentWriter.Render"/>.
/// </summary>
public sealed class PrCommentOptions
{
    /// <summary>
    /// Optional title rendered as the markdown heading.
    /// </summary>
    public string Title { get; init; } = "RevitCli Check Report";

    /// <summary>
    /// Maximum number of issue rows to include before emitting a truncation footer.
    /// </summary>
    public int MaxRows { get; init; } = 50;

    /// <summary>
    /// When true, issues are grouped under a sub-heading per Revit category.
    /// </summary>
    public bool GroupByCategory { get; init; }
}

/// <summary>
/// Renders <see cref="AuditIssue"/> sequences into markdown PR comments.
/// </summary>
public static class PrCommentWriter
{
    public static string Render(IEnumerable<AuditIssue> issues, PrCommentOptions? options = null)
    {
        options ??= new PrCommentOptions();
        var list = (issues ?? Array.Empty<AuditIssue>()).ToList();
        var nl = Environment.NewLine;
        var builder = new StringBuilder();

        builder.Append("## ").Append(options.Title).Append(nl);
        builder.Append(nl);

        if (list.Count == 0)
        {
            builder.Append("No issues found.").Append(nl);
            return builder.ToString();
        }

        var errorCount = list.Count(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var warningCount = list.Count(i => string.Equals(i.Severity, "warning", StringComparison.OrdinalIgnoreCase));

        builder.Append("**Summary:** ")
            .Append(errorCount).Append(" errors, ")
            .Append(warningCount).Append(" warnings")
            .Append(" (").Append(list.Count).Append(" total)").Append(nl);
        builder.Append(nl);

        var maxRows = options.MaxRows > 0 ? options.MaxRows : 50;
        var truncated = list.Count > maxRows;
        var renderedRows = truncated ? list.Take(maxRows).ToList() : list;

        if (options.GroupByCategory)
            RenderGrouped(builder, renderedRows, nl);
        else
            RenderTable(builder, renderedRows, nl);

        if (truncated)
        {
            builder.Append(nl);
            builder.Append("_+ ").Append(list.Count - maxRows).Append(" more not shown._").Append(nl);
        }

        return builder.ToString();
    }

    private static void RenderGrouped(StringBuilder builder, IEnumerable<AuditIssue> issues, string nl)
    {
        var groups = issues
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? "(uncategorized)" : i.Category!)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var first = true;
        foreach (var group in groups)
        {
            if (!first)
                builder.Append(nl);
            first = false;

            builder.Append("### ").Append(group.Key).Append(nl);
            builder.Append(nl);
            RenderTable(builder, group, nl);
        }
    }

    private static void RenderTable(StringBuilder builder, IEnumerable<AuditIssue> issues, string nl)
    {
        builder.Append("| Rule | Severity | Element | Message |").Append(nl);
        builder.Append("| --- | --- | --- | --- |").Append(nl);

        foreach (var issue in issues)
        {
            builder.Append("| ").Append(EscapeCell(issue.Rule))
                .Append(" | ").Append(EscapeCell(SeverityLabel(issue.Severity)))
                .Append(" | ").Append(EscapeCell(FormatElement(issue)))
                .Append(" | ").Append(EscapeCell(issue.Message))
                .Append(" |").Append(nl);
        }
    }

    private static string FormatElement(AuditIssue issue)
    {
        var hasId = issue.ElementId.HasValue;
        var hasCategory = !string.IsNullOrWhiteSpace(issue.Category);

        if (hasId && hasCategory)
            return $"{issue.Category}:{issue.ElementId}";
        if (hasId)
            return issue.ElementId!.Value.ToString();
        if (hasCategory)
            return issue.Category!;
        return "-";
    }

    private static string SeverityLabel(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return "info";
        return severity.Trim().ToLowerInvariant();
    }

    private static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Replace pipes (table separator), backticks (escape), and embedded
        // newlines so the markdown table stays single-row.
        var sanitized = value
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "\\|");
        return sanitized;
    }
}
