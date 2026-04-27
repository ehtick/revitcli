using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitCli.History;

/// <summary>
/// Parses user-provided time references that point at a snapshot inside the
/// history store. Three forms are supported:
/// <list type="bullet">
///   <item><c>@-N</c> - the Nth most recent snapshot. <c>@-1</c> is "the latest",
///         <c>@-2</c> is the one before that, etc.</item>
///   <item><c>YYYY-MM-DDTHH:MM:SSZ</c> ISO-8601 timestamp - resolves to the
///         snapshot whose <see cref="SnapshotMetadata.CapturedAt"/> is closest to
///         (and not after) the given instant.</item>
///   <item>Duration like <c>7d</c>, <c>24h</c>, <c>30m</c> - resolved against "now"
///         then matched as the ISO timestamp form. <c>7d</c> picks the snapshot
///         whose <c>CapturedAt</c> is closest to <c>now - 7d</c> without going
///         past it.</item>
/// </list>
/// The parser produces a selector closure so callers can apply it later against
/// any concrete history listing without re-parsing.
/// </summary>
public static class HistoryReference
{
    /// <summary>
    /// Parse <paramref name="reference"/> into a selector that, given an ordered
    /// snapshot list (newest last), returns the matching entry or <c>null</c>.
    /// </summary>
    /// <param name="reference">User input. Whitespace is trimmed; case is preserved
    /// for ISO timestamps but ignored for the duration suffix.</param>
    /// <param name="now">Override for the current instant. Tests pass a fixed value;
    /// production callers should pass <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <returns>Selector that picks one entry from a chronologically sorted list.</returns>
    /// <exception cref="FormatException">Thrown when the input does not match any
    /// supported form. The message lists the accepted shapes so the CLI can
    /// surface the same hint to users.</exception>
    public static Func<IReadOnlyList<SnapshotMetadata>, SnapshotMetadata?> Parse(
        string reference,
        DateTimeOffset? now = null)
    {
        if (reference == null)
        {
            throw new FormatException("History reference is required.");
        }

        var trimmed = reference.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("History reference is required.");
        }

        // @-N form
        if (trimmed.StartsWith("@-", StringComparison.Ordinal))
        {
            var rest = trimmed.Substring(2);
            if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
            {
                throw new FormatException(
                    $"Invalid history reference '{reference}': @-N must use a positive integer (e.g. @-1).");
            }

            return list => SelectNthMostRecent(list, n);
        }

        // ISO 8601 timestamp
        if (TryParseIsoTimestamp(trimmed, out var iso))
        {
            return list => SelectClosestNotAfter(list, iso);
        }

        // Duration suffix d/h/m
        if (TryParseDuration(trimmed, out var span))
        {
            var resolveNow = now ?? DateTimeOffset.UtcNow;
            var target = resolveNow - span;
            return list => SelectClosestNotAfter(list, target);
        }

        throw new FormatException(
            $"Invalid history reference '{reference}'. Use @-N, ISO 8601 timestamp, or duration like 7d/24h/30m.");
    }

    private static SnapshotMetadata? SelectNthMostRecent(IReadOnlyList<SnapshotMetadata> list, int n)
    {
        if (list == null || list.Count == 0 || n <= 0 || n > list.Count)
        {
            return null;
        }

        // Sort newest first (descending CapturedAt) then pick index n-1.
        var ordered = list
            .OrderByDescending(meta => ParseCapturedAt(meta.CapturedAt))
            .ThenByDescending(meta => meta.Id, StringComparer.Ordinal)
            .ToList();
        return ordered[n - 1];
    }

    private static SnapshotMetadata? SelectClosestNotAfter(
        IReadOnlyList<SnapshotMetadata> list,
        DateTimeOffset target)
    {
        if (list == null || list.Count == 0)
        {
            return null;
        }

        SnapshotMetadata? best = null;
        DateTimeOffset bestAt = DateTimeOffset.MinValue;
        foreach (var meta in list)
        {
            var captured = ParseCapturedAt(meta.CapturedAt);
            if (captured > target)
            {
                continue;
            }

            if (best == null || captured > bestAt)
            {
                best = meta;
                bestAt = captured;
            }
            else if (captured == bestAt &&
                     string.Compare(meta.Id, best.Id, StringComparison.Ordinal) > 0)
            {
                // Deterministic tie-break: prefer the lexicographically larger id
                // so identical timestamps still yield a stable result.
                best = meta;
            }
        }

        return best;
    }

    private static DateTimeOffset ParseCapturedAt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTimeOffset.MinValue;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.MinValue;
    }

    private static bool TryParseIsoTimestamp(string value, out DateTimeOffset utc)
    {
        // Accept "Z", numeric offset, or naive UTC. Reject pure date-only ("2026-04-27")
        // because that would be ambiguous with the duration form.
        if (DateTimeOffset.TryParseExact(
                value,
                new[]
                {
                    "yyyy-MM-ddTHH:mm:ssK",
                    "yyyy-MM-ddTHH:mm:ss.fffK",
                    "yyyy-MM-ddTHH:mm:ss.fffffffK",
                    "o",
                    "yyyy-MM-ddTHH:mm:ssZ",
                    "yyyy-MM-ddTHH:mm:ss",
                },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utc = parsed;
            return true;
        }

        utc = default;
        return false;
    }

    private static bool TryParseDuration(string value, out TimeSpan span)
    {
        span = default;
        if (value.Length < 2)
        {
            return false;
        }

        var suffix = char.ToLowerInvariant(value[value.Length - 1]);
        if (suffix != 'd' && suffix != 'h' && suffix != 'm' && suffix != 's')
        {
            return false;
        }

        var head = value.Substring(0, value.Length - 1);
        if (!long.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        if (amount < 0)
        {
            // Negative durations are not meaningful — disallow rather than silently flip.
            throw new FormatException(
                $"Invalid history duration '{value}': negative offsets are not allowed.");
        }

        try
        {
            span = suffix switch
            {
                'd' => TimeSpan.FromDays(amount),
                'h' => TimeSpan.FromHours(amount),
                'm' => TimeSpan.FromMinutes(amount),
                's' => TimeSpan.FromSeconds(amount),
                _ => TimeSpan.Zero,
            };
        }
        catch (OverflowException)
        {
            throw new FormatException(
                $"Invalid history duration '{value}': value is too large to represent.");
        }

        return true;
    }
}
