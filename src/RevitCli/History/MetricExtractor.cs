using System;
using System.Collections.Generic;
using System.Linq;
using RevitCli.Shared;

namespace RevitCli.History;

/// <summary>
/// Pure-C# seam between <see cref="ModelSnapshot"/> and trend rendering.
/// Given a metric name, returns a numeric value suitable for time series
/// plotting, or <c>null</c> when the snapshot does not carry that metric.
/// <para>
/// Supported metric forms:
/// </para>
/// <list type="bullet">
///   <item><c>score</c> — when a precomputed score is supplied via the
///         <c>scoreLookup</c> argument (the renderer feeds historical scores in).</item>
///   <item><c>elements.&lt;category&gt;</c> — element count for a single
///         snapshot category, looked up case-insensitively. Falls back to the
///         <see cref="SnapshotSummary.ElementCounts"/> dictionary if categories
///         are absent.</item>
///   <item><c>sheets</c> — number of sheets in the snapshot.</item>
///   <item><c>schedules</c> — number of schedules in the snapshot.</item>
///   <item><c>count.&lt;key&gt;</c> — generic alias for any
///         <see cref="SnapshotSummary.ElementCounts"/> key.</item>
/// </list>
/// All comparisons use <see cref="StringComparison.OrdinalIgnoreCase"/> for
/// metric prefixes and <see cref="StringComparer.OrdinalIgnoreCase"/> for
/// category names so a profile-driven "Walls" metric matches a snapshot that
/// stores "walls".
/// </summary>
public static class MetricExtractor
{
    /// <summary>
    /// The metric name used to mean "model health score". Score is not part of
    /// the snapshot payload, so this constant lets callers detect that they
    /// must supply an external lookup.
    /// </summary>
    public const string ScoreMetric = "score";

    /// <summary>
    /// Try to extract <paramref name="metric"/> from <paramref name="snapshot"/>.
    /// Returns <c>null</c> when the metric is unknown or the requested
    /// category/key is missing — callers render a blank cell for those points
    /// and skip them in sparkline scaling.
    /// </summary>
    /// <param name="snapshot">Source snapshot. Must not be null.</param>
    /// <param name="metric">Metric name (see class doc for forms).</param>
    /// <param name="scoreLookup">Optional resolver invoked when the metric is
    /// <c>score</c>. Returning <c>null</c> from the lookup signals "no score
    /// available for this snapshot".</param>
    public static double? Extract(
        ModelSnapshot snapshot,
        string metric,
        Func<ModelSnapshot, double?>? scoreLookup = null)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(metric))
        {
            return null;
        }

        var trimmed = metric.Trim();

        if (string.Equals(trimmed, ScoreMetric, StringComparison.OrdinalIgnoreCase))
        {
            return scoreLookup?.Invoke(snapshot);
        }

        if (string.Equals(trimmed, "sheets", StringComparison.OrdinalIgnoreCase))
        {
            // Prefer the explicit summary count — falls back to the list size
            // when summary was not populated by the addin.
            if (snapshot.Summary != null && snapshot.Summary.SheetCount > 0)
            {
                return snapshot.Summary.SheetCount;
            }
            return snapshot.Sheets?.Count ?? 0;
        }

        if (string.Equals(trimmed, "schedules", StringComparison.OrdinalIgnoreCase))
        {
            if (snapshot.Summary != null && snapshot.Summary.ScheduleCount > 0)
            {
                return snapshot.Summary.ScheduleCount;
            }
            return snapshot.Schedules?.Count ?? 0;
        }

        if (TryGetSuffix(trimmed, "elements.", out var category))
        {
            return ExtractCategoryCount(snapshot, category);
        }

        if (TryGetSuffix(trimmed, "count.", out var countKey))
        {
            return ExtractCountKey(snapshot, countKey);
        }

        return null;
    }

    private static bool TryGetSuffix(string value, string prefix, out string suffix)
    {
        if (value.Length > prefix.Length &&
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            suffix = value.Substring(prefix.Length);
            return suffix.Length > 0;
        }

        suffix = string.Empty;
        return false;
    }

    private static double? ExtractCategoryCount(ModelSnapshot snapshot, string category)
    {
        // Categories take precedence so the user gets the live element list
        // count even when the summary dictionary has not been refreshed.
        if (snapshot.Categories != null)
        {
            foreach (var pair in snapshot.Categories)
            {
                if (string.Equals(pair.Key, category, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value?.Count ?? 0;
                }
            }
        }

        if (snapshot.Summary?.ElementCounts != null)
        {
            foreach (var pair in snapshot.Summary.ElementCounts)
            {
                if (string.Equals(pair.Key, category, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    private static double? ExtractCountKey(ModelSnapshot snapshot, string key)
    {
        if (snapshot.Summary?.ElementCounts != null)
        {
            foreach (var pair in snapshot.Summary.ElementCounts)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        // Fall back to category list size so "count.walls" matches the
        // category-list count when summary is not populated.
        if (snapshot.Categories != null)
        {
            foreach (var pair in snapshot.Categories)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value?.Count ?? 0;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Convenience: enumerate the metric's value across a sequence of snapshots
    /// in the order supplied. Returns one entry per snapshot, with <c>null</c>
    /// for missing data points.
    /// </summary>
    public static IReadOnlyList<double?> Series(
        IEnumerable<ModelSnapshot> snapshots,
        string metric,
        Func<ModelSnapshot, double?>? scoreLookup = null)
    {
        if (snapshots == null)
        {
            throw new ArgumentNullException(nameof(snapshots));
        }

        return snapshots.Select(s => Extract(s, metric, scoreLookup)).ToList();
    }
}
