using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevitCli.Journal;

internal static class JournalSignatureService
{
    public const string Algorithm = "HMACSHA256";
    public const string HashAlgorithm = "SHA256-CHAIN-V1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static JournalSignResult Sign(
        string journalPath,
        string signaturePath,
        string keyPath,
        DateTimeOffset? signedUntil)
    {
        if (!File.Exists(journalPath))
            throw new FileNotFoundException($"Journal file not found: {journalPath}", journalPath);

        var key = LoadOrCreateKey(keyPath, out var keyCreated);
        var entries = ReadSelectedLines(journalPath, signedUntil);
        var computed = Compute(entries);
        var envelope = new JournalSignatureEnvelope
        {
            CreatedAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            SignedUntil = signedUntil?.ToString("o", CultureInfo.InvariantCulture),
            JournalPath = Path.GetFullPath(journalPath),
            EntryCount = computed.Entries.Count,
            RootHash = computed.RootHash,
            Entries = computed.Entries,
        };
        envelope.Signature = ComputeSignature(envelope, key);

        var signatureDir = Path.GetDirectoryName(Path.GetFullPath(signaturePath));
        if (!string.IsNullOrWhiteSpace(signatureDir))
            Directory.CreateDirectory(signatureDir);
        File.WriteAllText(signaturePath, JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8);

        return new JournalSignResult
        {
            JournalPath = envelope.JournalPath,
            SignaturePath = Path.GetFullPath(signaturePath),
            KeyPath = Path.GetFullPath(keyPath),
            KeyCreated = keyCreated,
            EntryCount = envelope.EntryCount,
            RootHash = envelope.RootHash,
            SignedUntil = envelope.SignedUntil,
        };
    }

    public static JournalVerifyResult Verify(string journalPath, string signaturePath, string keyPath)
    {
        var result = new JournalVerifyResult
        {
            JournalPath = Path.GetFullPath(journalPath),
            SignaturePath = Path.GetFullPath(signaturePath),
        };

        if (!File.Exists(journalPath))
        {
            result.Errors.Add($"Journal file not found: {journalPath}");
            return result;
        }

        if (!File.Exists(signaturePath))
        {
            result.Errors.Add($"Signature file not found: {signaturePath}");
            return result;
        }

        if (!File.Exists(keyPath))
        {
            result.Errors.Add($"Key file not found: {keyPath}");
            return result;
        }

        JournalSignatureEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<JournalSignatureEnvelope>(
                File.ReadAllText(signaturePath, Encoding.UTF8),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Signature file is not valid JSON: {ex.Message}");
            return result;
        }

        if (envelope == null)
        {
            result.Errors.Add("Signature file is empty.");
            return result;
        }

        ValidateEnvelope(envelope, result.Errors);
        if (result.Errors.Count > 0)
            return result;

        var signedUntil = ParseSignedUntil(envelope.SignedUntil, result.Errors);
        if (result.Errors.Count > 0)
            return result;

        var key = LoadExistingKey(keyPath);
        var computed = Compute(ReadSelectedLines(journalPath, signedUntil));
        result.EntryCount = computed.Entries.Count;
        result.RootHash = computed.RootHash;

        CompareEntries(envelope, computed, result.Errors);

        if (!string.Equals(envelope.RootHash, computed.RootHash, StringComparison.OrdinalIgnoreCase))
            result.Errors.Add($"Root hash mismatch: expected {envelope.RootHash}, actual {computed.RootHash}.");

        var expectedSignature = ComputeSignature(envelope, key);
        if (!FixedTimeHexEquals(envelope.Signature, expectedSignature))
            result.Errors.Add("HMAC signature mismatch.");

        return result;
    }

    private static DateTimeOffset? ParseSignedUntil(string? signedUntil, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(signedUntil))
            return null;

        if (DateTimeOffset.TryParse(
                signedUntil,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return parsed;

        errors.Add($"Signature has invalid signedUntil value: {signedUntil}");
        return null;
    }

    private static void ValidateEnvelope(JournalSignatureEnvelope envelope, List<string> errors)
    {
        if (envelope.SchemaVersion != 1)
            errors.Add($"Unsupported signature schema version: {envelope.SchemaVersion}.");
        if (!string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal))
            errors.Add($"Unsupported signature algorithm: {envelope.Algorithm}.");
        if (!string.Equals(envelope.HashAlgorithm, HashAlgorithm, StringComparison.Ordinal))
            errors.Add($"Unsupported hash algorithm: {envelope.HashAlgorithm}.");
        if (string.IsNullOrWhiteSpace(envelope.RootHash))
            errors.Add("Signature is missing rootHash.");
        if (string.IsNullOrWhiteSpace(envelope.Signature))
            errors.Add("Signature is missing HMAC value.");
    }

    private static void CompareEntries(
        JournalSignatureEnvelope envelope,
        ComputedSignature computed,
        List<string> errors)
    {
        if (envelope.EntryCount != computed.Entries.Count)
            errors.Add($"Entry count mismatch: expected {envelope.EntryCount}, actual {computed.Entries.Count}.");

        if (envelope.Entries.Count != envelope.EntryCount)
            errors.Add($"Signature entry table count {envelope.Entries.Count} does not match entryCount {envelope.EntryCount}.");

        var count = Math.Min(envelope.Entries.Count, computed.Entries.Count);
        for (var i = 0; i < count; i++)
        {
            var expected = envelope.Entries[i];
            var actual = computed.Entries[i];
            if (expected.LineNumber != actual.LineNumber)
            {
                errors.Add($"Journal line mismatch at signed entry {i + 1}: expected line {expected.LineNumber}, actual line {actual.LineNumber}.");
                return;
            }

            if (!string.Equals(expected.LineHash, actual.LineHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Journal line {actual.LineNumber} hash mismatch.");
                return;
            }

            if (!string.Equals(expected.ChainHash, actual.ChainHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Journal line {actual.LineNumber} chain hash mismatch.");
                return;
            }
        }
    }

    private static List<JournalLine> ReadSelectedLines(string journalPath, DateTimeOffset? signedUntil)
    {
        var selected = new List<JournalLine>();
        var lines = File.ReadAllLines(journalPath, Encoding.UTF8);
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            var timestamp = TryReadTimestamp(line);
            if (signedUntil.HasValue && timestamp.HasValue && timestamp.Value > signedUntil.Value)
                continue;

            selected.Add(new JournalLine(lineNumber, line, timestamp));
        }

        return selected;
    }

    private static DateTimeOffset? TryReadTimestamp(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("timestamp", out var timestampProperty))
                return null;
            if (timestampProperty.ValueKind != JsonValueKind.String)
                return null;
            var timestamp = timestampProperty.GetString();
            if (string.IsNullOrWhiteSpace(timestamp))
                return null;
            if (DateTimeOffset.TryParse(
                    timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
                return parsed;
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static ComputedSignature Compute(IReadOnlyList<JournalLine> lines)
    {
        var previous = new byte[32];
        var entries = new List<JournalSignatureEntry>();

        foreach (var line in lines)
        {
            var lineHash = SHA256.HashData(Encoding.UTF8.GetBytes(line.Text));
            var lineNumber = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(lineNumber, line.LineNumber);

            var chainInput = new byte[previous.Length + lineNumber.Length + lineHash.Length];
            Buffer.BlockCopy(previous, 0, chainInput, 0, previous.Length);
            Buffer.BlockCopy(lineNumber, 0, chainInput, previous.Length, lineNumber.Length);
            Buffer.BlockCopy(lineHash, 0, chainInput, previous.Length + lineNumber.Length, lineHash.Length);
            previous = SHA256.HashData(chainInput);

            entries.Add(new JournalSignatureEntry
            {
                LineNumber = line.LineNumber,
                Timestamp = line.Timestamp?.ToString("o", CultureInfo.InvariantCulture),
                LineHash = Convert.ToHexString(lineHash).ToLowerInvariant(),
                ChainHash = Convert.ToHexString(previous).ToLowerInvariant(),
            });
        }

        return new ComputedSignature(entries, Convert.ToHexString(previous).ToLowerInvariant());
    }

    private static byte[] LoadOrCreateKey(string keyPath, out bool created)
    {
        if (File.Exists(keyPath))
        {
            created = false;
            return LoadExistingKey(keyPath);
        }

        var keyDir = Path.GetDirectoryName(Path.GetFullPath(keyPath));
        if (!string.IsNullOrWhiteSpace(keyDir))
            Directory.CreateDirectory(keyDir);

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyPath, Convert.ToBase64String(key) + Environment.NewLine, Encoding.UTF8);
        created = true;
        return key;
    }

    private static byte[] LoadExistingKey(string keyPath)
    {
        var text = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Key file is empty: {keyPath}");

        try
        {
            var decoded = Convert.FromBase64String(text);
            if (decoded.Length >= 16)
                return decoded;
        }
        catch (FormatException)
        {
        }

        return Encoding.UTF8.GetBytes(text);
    }

    private static string ComputeSignature(JournalSignatureEnvelope envelope, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(BuildSignaturePayload(envelope))))
            .ToLowerInvariant();
    }

    private static string BuildSignaturePayload(JournalSignatureEnvelope envelope)
    {
        return string.Join(
            "\n",
            envelope.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            envelope.Algorithm,
            envelope.HashAlgorithm,
            envelope.JournalPath,
            envelope.EntryCount.ToString(CultureInfo.InvariantCulture),
            envelope.RootHash,
            envelope.SignedUntil ?? "");
    }

    private static bool FixedTimeHexEquals(string expectedHex, string actualHex)
    {
        try
        {
            var expected = Convert.FromHexString(expectedHex);
            var actual = Convert.FromHexString(actualHex);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record JournalLine(int LineNumber, string Text, DateTimeOffset? Timestamp);

    private sealed record ComputedSignature(List<JournalSignatureEntry> Entries, string RootHash);
}
