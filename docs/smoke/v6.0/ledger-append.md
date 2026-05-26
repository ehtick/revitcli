# RevitCli v6.0 Ledger Append Portable Smoke

`ledger append` is the append-only ledger runtime slice for v6.0. It emits
`ledger-append.v1` for dry-run and approved append previews, and approved
records are stored as `ledger-operation.v1` JSON lines under
`.revitcli/ledger/operations.jsonl`.

The portable smoke evidence covers:

- dry-run default behavior: `ledger append --output json` does not create or
  modify `.revitcli/ledger/operations.jsonl`.
- explicit approval: `--yes` is required before a record is appended.
- bounded local write evidence: the approved smoke writes only the ledger
  directory and `.revitcli/ledger/operations.jsonl`.
- `source ledger` readback: `ledger query --source ledger` and
  `ledger validate --source ledger` consume the appended record.
- deterministic evidence links: receipt, artifact, and ledger evidence links
  are deduplicated and sorted.
- JSON/table/Markdown output semantic parity for append previews.

The append record may reference action, category, operator, status, model
identity, plan hash, receipt path, receipt hash, artifact path, rollback
pointer, and evidence links. Command-specific receipts remain the detailed
source of truth for the operation; the append record is the local operations
index.

Successful approved `set` commands now use the same local ledger record shape
after the Revit write succeeds. Those records use `command=set`, `action=set`,
the target category or element scope, affected element count, and available
affected element ids. `set --dry-run` and `set --plan-output` remain preview
paths and do not append ledger records.

Successful `export` commands now append `ledger-operation.v1` records after the
export receipt and delivery manifest are persisted. Those records use
`command=export`, `action=export`, the export format as category, receipt
path/hash evidence, output directory artifact evidence, command args, and
best-effort live `modelIdentity`/`modelPath`/`revitVersion` from `/api/status`.
Export status or ledger write failures remain non-blocking audit gaps rather
than changing a completed export result.

The Revit 2026 live smoke evidence in
`docs/smoke/v6.0/revit2026-live-addin.md` covers one PDF export against the
controlled `revit_cli.rvt` model and reads the resulting export ledger row back
through `ledger query --source ledger`.

`schedules batch-export` now appends `ledger-operation.v1` records after the
schedule export manifest is written. Successful batches use
`status=succeeded`; batches with export error issues use `status=failed` while
still linking the manifest artifact. The ledger row uses
`action=schedules.batch-export`, the output format as category, the manifest
path as artifact evidence, successful CSV paths as evidence links, and
best-effort live `modelIdentity`/`modelPath`/`revitVersion` from the manifest.

The emitted `ledger-operation.v1` record also carries the v6.0 contract fields
needed for audit review: `operationId`, `command`, `args`, `workingDirectory`,
`profile`, `revitVersion`, `machine`, `startedAtUtc`, `endedAtUtc`,
`riskLevel`, `dryRunRequired`, `approvalRequired`, `planPath`, `journalPath`,
`checks`, `artifacts`, `affectedElementCount`, and `affectedElementIds`.
Nullable fields remain present with `null` when the append command has no
corresponding source value.

Boundary summary: the portable append smoke does not start Revit, does not
write Revit model data, does not replay or apply ledger records, does not call
a network service, and introduces no database runtime. Live export ledger
recording is limited to receipt-backed audit indexing after export completion.
No SaaS, no MCP, no dashboard-central workflow, and no built-in LLM parser are
introduced.
