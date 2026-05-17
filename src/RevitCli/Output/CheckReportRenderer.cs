using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Shared;

namespace RevitCli.Output;

public static class CheckReportRenderer
{
    public static string RenderTable(string checkName, int passed, int failed, List<AuditIssue> issues, int suppressed = 0)
    {
        var lines = new List<string>();
        var summary = $"Check '{checkName}': {passed} passed, {failed} failed";
        if (suppressed > 0)
            summary += $", {suppressed} suppressed";
        lines.Add(summary);

        foreach (var issue in issues)
        {
            var prefix = issue.Severity == "error" ? "ERROR" : issue.Severity == "warning" ? "WARN" : "INFO";
            var elementRef = issue.ElementId.HasValue ? $" [Element {issue.ElementId}]" : "";
            lines.Add($"  [{prefix}] {issue.Rule}: {issue.Message}{elementRef}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string RenderJson(
        string checkName,
        int passed,
        int failed,
        List<AuditIssue> issues,
        int suppressed = 0,
        int? exitCode = null,
        string? failOn = null)
    {
        var report = new
        {
            schemaVersion = "check.v1",
            success = exitCode.HasValue ? exitCode.Value == 0 : (bool?)null,
            valid = exitCode.HasValue ? exitCode.Value == 0 : (bool?)null,
            exitCode,
            check = checkName,
            failOn,
            passed,
            failed,
            suppressed,
            timestamp = DateTime.UtcNow.ToString("o"),
            issues = issues.Select(i => new
            {
                rule = i.Rule,
                severity = i.Severity,
                message = i.Message,
                elementId = i.ElementId
            })
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public static string RenderHtml(string checkName, int passed, int failed, List<AuditIssue> issues, int suppressed = 0)
    {
        var errorCount = issues.Count(i => i.Severity == "error");
        var warnCount = issues.Count(i => i.Severity == "warning");
        var infoCount = issues.Count(i => i.Severity == "info");

        var statusColor = failed > 0 ? "#e74c3c" : "#2ecc71";
        var statusText = failed > 0 ? "FAILED" : "PASSED";

        var issueRows = string.Join("\n", issues.Select(i =>
        {
            var sevColor = i.Severity == "error" ? "#e74c3c" : i.Severity == "warning" ? "#f39c12" : "#3498db";
            var elemRef = i.ElementId.HasValue ? i.ElementId.Value.ToString() : "-";
            return $@"<tr>
  <td><span style=""color:{sevColor};font-weight:bold"">{i.Severity.ToUpper()}</span></td>
  <td>{Escape(i.Rule)}</td>
  <td>{Escape(i.Message)}</td>
  <td>{elemRef}</td>
</tr>";
        }));

        var suppressedCard = suppressed > 0
            ? $@"<div class=""card""><div class=""label"">Suppressed</div><div class=""value"" style=""color:#95a5a6"">{suppressed}</div></div>"
            : "";

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>RevitCli Check Report — {Escape(checkName)}</title>
<style>
  body {{ background: #1a1a2e; color: #e0e0e0; font-family: -apple-system, 'Segoe UI', sans-serif; margin: 2rem; }}
  h1 {{ color: #fff; }}
  .summary {{ display: flex; gap: 2rem; margin: 1.5rem 0; flex-wrap: wrap; }}
  .card {{ background: #16213e; border-radius: 8px; padding: 1rem 1.5rem; min-width: 120px; }}
  .card .label {{ font-size: 0.85rem; color: #999; }}
  .card .value {{ font-size: 1.8rem; font-weight: bold; }}
  .status {{ color: {statusColor}; }}
  table {{ width: 100%; border-collapse: collapse; margin-top: 1.5rem; }}
  th {{ text-align: left; padding: 0.6rem; background: #16213e; color: #999; font-size: 0.85rem; text-transform: uppercase; }}
  td {{ padding: 0.6rem; border-bottom: 1px solid #2a2a4a; }}
  tr:hover {{ background: #1f2b4d; }}
  .footer {{ margin-top: 2rem; color: #666; font-size: 0.8rem; }}
</style>
</head>
<body>
<h1>RevitCli Check Report</h1>
<div class=""summary"">
  <div class=""card""><div class=""label"">Status</div><div class=""value status"">{statusText}</div></div>
  <div class=""card""><div class=""label"">Check Set</div><div class=""value"">{Escape(checkName)}</div></div>
  <div class=""card""><div class=""label"">Passed</div><div class=""value"" style=""color:#2ecc71"">{passed}</div></div>
  <div class=""card""><div class=""label"">Failed</div><div class=""value"" style=""color:#e74c3c"">{failed}</div></div>
  {suppressedCard}
</div>
<div class=""summary"">
  <div class=""card""><div class=""label"">Errors</div><div class=""value"" style=""color:#e74c3c"">{errorCount}</div></div>
  <div class=""card""><div class=""label"">Warnings</div><div class=""value"" style=""color:#f39c12"">{warnCount}</div></div>
  <div class=""card""><div class=""label"">Info</div><div class=""value"" style=""color:#3498db"">{infoCount}</div></div>
</div>

{(issues.Count > 0 ? $@"<table>
<thead><tr><th>Severity</th><th>Rule</th><th>Message</th><th>Element ID</th></tr></thead>
<tbody>
{issueRows}
</tbody>
</table>" : "<p style=\"color:#2ecc71;font-size:1.2rem\">All checks passed.</p>")}

<div class=""footer"">Generated by RevitCli on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>
</body>
</html>";
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
