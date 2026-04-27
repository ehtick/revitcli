using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RevitCli.History;

/// <summary>
/// Renders a single-line ASCII sparkline plus a per-point value listing for
/// time-series data. The renderer is pure: input is a list of
/// <c>(label, value?)</c> pairs and a target width, output is a multi-line
/// string. No console I/O is performed here so callers can capture the result
/// in tests, write it to files, or pipe it through a TUI.
/// </summary>
public static class TrendRenderer
{
    /// <summary>
    /// Unicode block characters used for sparkline cells, lowest to highest.
    /// All eight glyphs render correctly in modern Windows / macOS / Linux
    /// terminals using UTF-8 output (PowerShell 7 + Windows Terminal default
    /// to UTF-8). For older Windows consoles the caller can set
    /// <c>Console.OutputEncoding = Encoding.UTF8</c> at startup.
    /// </summary>
    public static readonly char[] BlockChars =
        new[] { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    /// <summary>
    /// Glyph rendered for a missing data point. A blank cell would collapse
    /// adjacent bars, so we use an em-dash-like character that survives both
    /// UTF-8 and CP-437 codepages.
    /// </summary>
    public const char MissingChar = ' ';

    /// <summary>
    /// One data point in the trend. <see cref="Value"/> is nullable because
    /// the metric may be missing on some snapshots (e.g. a category did not
    /// exist yet, or the score lookup could not run).
    /// </summary>
    /// <param name="Label">Human-readable label, typically a date or
    /// timestamp. Used by <see cref="Render"/> in the per-point listing.</param>
    /// <param name="Value">Numeric value; <c>null</c> when missing.</param>
    public sealed record Point(string Label, double? Value);

    /// <summary>
    /// Rendered output bundle. Sparkline-and-rows split out so tests can
    /// assert on each independently without parsing the combined text.
    /// </summary>
    /// <param name="Sparkline">Single line of <see cref="BlockChars"/>.</param>
    /// <param name="Rows">Per-point lines: "yyyy-MM-dd  123" or "yyyy-MM-dd  -".</param>
    /// <param name="Combined">Sparkline followed by a blank line and the rows;
    /// suitable to write directly to <c>Console.Out</c>.</param>
    public sealed record RenderResult(string Sparkline, IReadOnlyList<string> Rows, string Combined);

    /// <summary>
    /// Render <paramref name="points"/> as a sparkline plus per-point rows.
    /// Behavioural rules:
    /// <list type="bullet">
    ///   <item>Empty input → sparkline = empty, rows contain a single
    ///         "No snapshots in window" line.</item>
    ///   <item>Single point with a value → one full bar.</item>
    ///   <item>All values identical → flat mid-height bar so the user sees
    ///         the row exists without it implying a downward trend.</item>
    ///   <item>Missing values → cell rendered with <see cref="MissingChar"/>;
    ///         excluded from the min/max scale.</item>
    ///   <item>Fewer points than <paramref name="width"/> → bars are stretched
    ///         (each point covers <c>width / count</c> cells, with leftover
    ///         cells distributed left-to-right so the width matches exactly).</item>
    /// </list>
    /// </summary>
    /// <param name="points">Input series in chronological order.</param>
    /// <param name="width">Target sparkline width in characters. Must be &gt; 0.</param>
    /// <param name="culture">Culture for formatting numeric values; defaults
    /// to <see cref="CultureInfo.InvariantCulture"/>.</param>
    public static RenderResult Render(
        IReadOnlyList<Point> points,
        int width = 60,
        CultureInfo? culture = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "width must be positive.");
        }

        culture ??= CultureInfo.InvariantCulture;

        if (points == null || points.Count == 0)
        {
            return new RenderResult(string.Empty, new List<string> { "No snapshots in window" }, "No snapshots in window");
        }

        var sparkline = BuildSparkline(points, width);
        var rows = BuildRows(points, culture);
        var sb = new StringBuilder();
        sb.AppendLine(sparkline);
        sb.AppendLine();
        foreach (var row in rows)
        {
            sb.AppendLine(row);
        }

        return new RenderResult(sparkline, rows, sb.ToString().TrimEnd());
    }

    private static string BuildSparkline(IReadOnlyList<Point> points, int width)
    {
        var present = points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToList();
        if (present.Count == 0)
        {
            // No usable data — render a row of missing chars so the layout
            // still has a sparkline line.
            return new string(MissingChar, width);
        }

        var min = present.Min();
        var max = present.Max();
        var range = max - min;

        // Compute how many cells each input point should occupy. This avoids
        // truncating when the user has fewer snapshots than width.
        var allocations = AllocateCells(points.Count, width);

        var sb = new StringBuilder(width);
        for (var i = 0; i < points.Count; i++)
        {
            var cells = allocations[i];
            if (cells == 0)
            {
                continue;
            }

            char glyph;
            if (!points[i].Value.HasValue)
            {
                glyph = MissingChar;
            }
            else if (range == 0)
            {
                // Flat: render mid-height bar so the user sees a clear "we have
                // data, it just isn't moving" line.
                glyph = BlockChars[BlockChars.Length / 2];
            }
            else
            {
                var normalised = (points[i].Value!.Value - min) / range;
                var index = (int)Math.Round(normalised * (BlockChars.Length - 1));
                if (index < 0) index = 0;
                if (index >= BlockChars.Length) index = BlockChars.Length - 1;
                glyph = BlockChars[index];
            }

            sb.Append(glyph, cells);
        }

        return sb.ToString();
    }

    private static int[] AllocateCells(int pointCount, int width)
    {
        var allocations = new int[pointCount];
        if (pointCount >= width)
        {
            // Each cell maps to ~one input point; pick evenly-spaced indices.
            for (var i = 0; i < width; i++)
            {
                var sourceIdx = (int)Math.Round((double)i * (pointCount - 1) / Math.Max(1, width - 1));
                if (sourceIdx < 0) sourceIdx = 0;
                if (sourceIdx >= pointCount) sourceIdx = pointCount - 1;
                allocations[sourceIdx] += 1;
            }
            return allocations;
        }

        var baseCells = width / pointCount;
        var remainder = width % pointCount;
        for (var i = 0; i < pointCount; i++)
        {
            allocations[i] = baseCells + (i < remainder ? 1 : 0);
        }

        return allocations;
    }

    private static IReadOnlyList<string> BuildRows(IReadOnlyList<Point> points, CultureInfo culture)
    {
        var labelWidth = points.Max(p => (p.Label ?? string.Empty).Length);
        var rows = new List<string>(points.Count);
        foreach (var point in points)
        {
            var label = (point.Label ?? string.Empty).PadRight(labelWidth);
            var value = point.Value.HasValue
                ? FormatNumber(point.Value.Value, culture)
                : "-";
            rows.Add($"{label}  {value}");
        }

        return rows;
    }

    private static string FormatNumber(double value, CultureInfo culture)
    {
        // Render integers without the trailing .0 so element counts read clean,
        // but allow up to two decimals for fractional metrics like score ratios.
        if (Math.Abs(value - Math.Round(value)) < 1e-9)
        {
            return ((long)Math.Round(value)).ToString(culture);
        }

        return value.ToString("0.##", culture);
    }
}
