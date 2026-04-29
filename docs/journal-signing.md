# Journal Signing

`.revitcli/journal.jsonl` is append-only by convention. `revitcli journal`
adds an explicit tamper-evidence layer for regulated or high-trust
projects.

## Sign

```powershell
revitcli journal sign
```

Defaults:

- Journal: `.revitcli/journal.jsonl`
- Signature: `.revitcli/journal.jsonl.sig`
- HMAC key: `.revitcli/journal.key`

If the key does not exist, `sign` creates a 256-bit local key. Store that
key outside shared history if multiple parties should not be able to
re-sign modified journals.

Use `--until` to sign only entries at or before a timestamp:

```powershell
revitcli journal sign --until 2026-04-29T12:00:00Z
```

Later journal entries remain allowed during verification; inserting,
deleting, or modifying any signed entry fails verification.

## Verify

```powershell
revitcli journal verify
```

Verification recomputes the SHA256 line hash chain, checks the saved root
hash, and validates the HMAC signature. It exits `0` only when the
signature, key, and signed journal entries match.

## Custom Paths

```powershell
revitcli journal sign `
  --journal C:\audit\journal.jsonl `
  --signature C:\audit\journal.jsonl.sig `
  --key C:\secure\revitcli-journal.key

revitcli journal verify `
  --journal C:\audit\journal.jsonl `
  --signature C:\audit\journal.jsonl.sig `
  --key C:\secure\revitcli-journal.key
```

Use `--output json` for CI systems that need machine-readable results.
