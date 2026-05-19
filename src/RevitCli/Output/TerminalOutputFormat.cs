using System;

namespace RevitCli.Output;

internal static class TerminalOutputFormat
{
    public static bool TryNormalize(string? value, out string normalized, params string[] allowed)
    {
        if (allowed.Length == 0)
            throw new ArgumentException("At least one allowed format must be specified.", nameof(allowed));

        normalized = string.IsNullOrWhiteSpace(value)
            ? allowed[0].ToLowerInvariant()
            : value.Trim().ToLowerInvariant();

        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
