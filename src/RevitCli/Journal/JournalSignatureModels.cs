using System.Collections.Generic;

namespace RevitCli.Journal;

internal sealed class JournalSignatureEnvelope
{
    public int SchemaVersion { get; set; } = 1;

    public string Algorithm { get; set; } = JournalSignatureService.Algorithm;

    public string HashAlgorithm { get; set; } = JournalSignatureService.HashAlgorithm;

    public string CreatedAt { get; set; } = "";

    public string? SignedUntil { get; set; }

    public string JournalPath { get; set; } = "";

    public int EntryCount { get; set; }

    public string RootHash { get; set; } = "";

    public string Signature { get; set; } = "";

    public List<JournalSignatureEntry> Entries { get; set; } = new();
}

internal sealed class JournalSignatureEntry
{
    public int LineNumber { get; set; }

    public string? Timestamp { get; set; }

    public string LineHash { get; set; } = "";

    public string ChainHash { get; set; } = "";
}

internal sealed class JournalSignResult
{
    public string JournalPath { get; set; } = "";

    public string SignaturePath { get; set; } = "";

    public string KeyPath { get; set; } = "";

    public bool KeyCreated { get; set; }

    public int EntryCount { get; set; }

    public string RootHash { get; set; } = "";

    public string? SignedUntil { get; set; }
}

internal sealed class JournalVerifyResult
{
    public bool IsValid => Errors.Count == 0;

    public string JournalPath { get; set; } = "";

    public string SignaturePath { get; set; } = "";

    public int EntryCount { get; set; }

    public string RootHash { get; set; } = "";

    public List<string> Errors { get; } = new();
}
