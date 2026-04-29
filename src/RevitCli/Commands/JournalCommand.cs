using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Journal;

namespace RevitCli.Commands;

public static class JournalCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static Command Create()
    {
        var command = new Command("journal", "Sign and verify the RevitCli operation journal");
        command.AddCommand(CreateSignCommand());
        command.AddCommand(CreateVerifyCommand());
        return command;
    }

    private static Command CreateSignCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var signatureOpt = new Option<string?>("--signature", "Override signature file path (default: <journal>.sig)");
        var keyOpt = new Option<string?>("--key", "HMAC key path (default: <dir>/.revitcli/journal.key; created if missing)");
        var untilOpt = new Option<string?>("--until", "Sign entries at or before this timestamp (ISO 8601)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json");

        var cmd = new Command("sign", "Create .revitcli/journal.jsonl.sig")
        {
            dirOpt,
            journalOpt,
            signatureOpt,
            keyOpt,
            untilOpt,
            outputOpt,
        };

        cmd.SetHandler(async (string? dir, string? journal, string? signature, string? key, string? until, string output) =>
        {
            Environment.ExitCode = await ExecuteSignAsync(dir, journal, signature, key, until, output, Console.Out);
        }, dirOpt, journalOpt, signatureOpt, keyOpt, untilOpt, outputOpt);

        return cmd;
    }

    private static Command CreateVerifyCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var signatureOpt = new Option<string?>("--signature", "Override signature file path (default: <journal>.sig)");
        var keyOpt = new Option<string?>("--key", "HMAC key path (default: <dir>/.revitcli/journal.key)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json");

        var cmd = new Command("verify", "Verify .revitcli/journal.jsonl.sig against the journal")
        {
            dirOpt,
            journalOpt,
            signatureOpt,
            keyOpt,
            outputOpt,
        };

        cmd.SetHandler(async (string? dir, string? journal, string? signature, string? key, string output) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(dir, journal, signature, key, output, Console.Out);
        }, dirOpt, journalOpt, signatureOpt, keyOpt, outputOpt);

        return cmd;
    }

    internal static Task<int> ExecuteSignAsync(
        string? dir,
        string? journal,
        string? signature,
        string? key,
        string? until,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        if (!TryParseUntil(until, out var signedUntil, out var untilError))
            return WriteError(output, untilError);

        var paths = ResolvePaths(dir, journal, signature, key);
        try
        {
            var result = JournalSignatureService.Sign(
                paths.JournalPath,
                paths.SignaturePath,
                paths.KeyPath,
                signedUntil);

            if (normalizedOutput == "json")
                return WriteJson(output, result, 0);

            return WriteLines(
                output,
                0,
                $"OK: Journal signature written: {result.SignaturePath}",
                $"OK: Entries signed: {result.EntryCount}",
                $"OK: Root hash: {result.RootHash}",
                $"OK: HMAC key: {result.KeyPath}{(result.KeyCreated ? " (created)" : "")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, $"failed to sign journal: {ex.Message}");
        }
    }

    internal static Task<int> ExecuteVerifyAsync(
        string? dir,
        string? journal,
        string? signature,
        string? key,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        var paths = ResolvePaths(dir, journal, signature, key);
        try
        {
            var result = JournalSignatureService.Verify(paths.JournalPath, paths.SignaturePath, paths.KeyPath);
            if (normalizedOutput == "json")
                return WriteJson(output, result, result.IsValid ? 0 : 1);

            if (result.IsValid)
            {
                return WriteLines(
                    output,
                    0,
                    $"OK: Journal signature valid: {result.SignaturePath}",
                    $"OK: Entries verified: {result.EntryCount}",
                    $"OK: Root hash: {result.RootHash}");
            }

            return WriteLines(output, 1, result.Errors.Select(error => $"FAIL: {error}").ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, $"failed to verify journal: {ex.Message}");
        }
    }

    private static bool TryParseOutput(string? outputFormat, out string normalized, out string error)
    {
        normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();
        if (normalized is "table" or "json")
        {
            error = "";
            return true;
        }

        error = $"unknown output format '{outputFormat}'. Use table or json.";
        return false;
    }

    private static bool TryParseUntil(string? until, out DateTimeOffset? signedUntil, out string error)
    {
        signedUntil = null;
        error = "";
        if (string.IsNullOrWhiteSpace(until))
            return true;

        if (DateTimeOffset.TryParse(
                until,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            signedUntil = parsed;
            return true;
        }

        error = $"invalid --until timestamp '{until}'. Use ISO 8601, for example 2026-04-29T12:34:56Z.";
        return false;
    }

    private static JournalPaths ResolvePaths(string? dir, string? journal, string? signature, string? key)
    {
        var baseDir = string.IsNullOrWhiteSpace(dir)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(dir);
        var journalPath = string.IsNullOrWhiteSpace(journal)
            ? Path.Combine(baseDir, ".revitcli", "journal.jsonl")
            : Path.GetFullPath(journal);
        var signaturePath = string.IsNullOrWhiteSpace(signature)
            ? journalPath + ".sig"
            : Path.GetFullPath(signature);
        var keyPath = string.IsNullOrWhiteSpace(key)
            ? Path.Combine(baseDir, ".revitcli", "journal.key")
            : Path.GetFullPath(key);
        return new JournalPaths(journalPath, signaturePath, keyPath);
    }

    private static Task<int> WriteError(TextWriter output, string error)
    {
        return WriteLines(output, 1, $"Error: {error}");
    }

    private static async Task<int> WriteLines(TextWriter output, int exitCode, params string[] lines)
    {
        foreach (var line in lines)
            await output.WriteLineAsync(line);
        return exitCode;
    }

    private static async Task<int> WriteJson(TextWriter output, object value, int exitCode)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions));
        return exitCode;
    }

    private sealed record JournalPaths(string JournalPath, string SignaturePath, string KeyPath);
}
