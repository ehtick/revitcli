using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCli.Workflows;

public static class WorkflowCommandLine
{
    private static readonly HashSet<string> ShellOperators = new(StringComparer.Ordinal)
    {
        "&",
        "&&",
        "|",
        "||",
        ";",
        ">",
        ">>",
        "<",
    };

    public static IReadOnlyList<string> Tokenize(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote == '\0')
            {
                if (char.IsWhiteSpace(ch))
                {
                    FlushToken(tokens, current);
                    continue;
                }

                if (ch is '"' or '\'')
                {
                    quote = ch;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == quote)
            {
                quote = '\0';
                continue;
            }

            current.Append(ch);
        }

        if (quote != '\0')
        {
            throw new FormatException("workflow command contains an unterminated quote.");
        }

        FlushToken(tokens, current);
        return tokens;
    }

    public static bool ContainsShellOperator(IReadOnlyList<string> tokens) =>
        tokens.Any(token => ShellOperators.Contains(token) ||
                            token.Contains("&&", StringComparison.Ordinal) ||
                            token.Contains("||", StringComparison.Ordinal) ||
                            token.Contains(';') ||
                            token.Contains('|') ||
                            token.Contains('>') ||
                            token.Contains('<') ||
                            token.Contains('&') ||
                            token.Contains("$(", StringComparison.Ordinal) ||
                            token.Contains('`'));

    public static bool ContainsShellOperator(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var quote = '\0';
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote == '\0')
            {
                if (ch is '"' or '\'')
                {
                    quote = ch;
                    continue;
                }

                if (ch is ';' or '|' or '>' or '<' or '&' or '`')
                    return true;

                if (ch == '$' && i + 1 < command.Length && command[i + 1] == '(')
                    return true;

                continue;
            }

            if (ch == quote)
                quote = '\0';
        }

        return false;
    }

    private static void FlushToken(List<string> tokens, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }
}
