# RevitCli v6.0 Ledger Replay Portable Smoke

`ledger replay --source ledger --output json` reads appended local
`ledger-operation.v1` records and emits `ledger-replay.v1` as a deterministic
preview-only plan by default.

The default preview contract is explicit: `dryRun` is `true`, the report shows
`applySupported`, and each step reports `canApply` with either a replayable
set-operation marker or a block reason. Default preview does not write files,
does not start Revit, does not call a network service, and uses no database.

`ledger replay --source ledger --action set --apply --yes --output json` is the
first bounded live replay path. It is limited to local source-ledger records
produced by successful approved `set --yes` commands that include frozen
`affectedElementIds`. `ledger replay --source ledger --action export --apply
--yes --output json` is a second bounded path for successful receipt-backed
`export` records with recorded `--format` and `--output-dir` args; dry-run or
incomplete export records stay blocked. `ledger replay --source ledger
--action schedules.batch-export --apply --yes --output json` is a third bounded
path for successful manifest-backed schedule batch-export records; missing or
incomplete manifests stay blocked. Apply mode reuses the recorded
parameter/frozen ids for set records, recorded export args for export records,
and recorded schedule manifest entries for schedule CSV outputs, then sends
those requests to the configured Revit server. A successful apply adds one local
`ledger-operation.v1` audit row with `command=ledger` and
`action=ledger.replay.apply`; it does not create another replayable source
action record. The replay audit row captures best-effort
`modelIdentity`/`modelPath`/`revitVersion` from `/api/status` when available,
while status unavailability leaves those fields null and does not block the
audit append. It does not replay journal/history/delivery/workflow sources and
does not create a database.

The live Revit 2026 smoke gate parses the isolated
`.revitcli/ledger/operations.jsonl` after replay apply and fails if the
`ledger.replay.apply` audit row is missing the affected element id, `--apply`
and `--yes` args, or non-empty live `modelIdentity`/`modelPath`/`revitVersion`
evidence.

Portable smoke coverage verifies source ledger readback,
JSON/table/Markdown output semantic parity, event-level no-write evidence, final
file-tree snapshot evidence for preview mode, `--apply --yes` approval
enforcement, source-ledger-only apply guards, set frozen-id replay request
construction, export replay arg reconstruction, and schedule batch-export
manifest replay reconstruction.

Boundary summary: no SaaS, no MCP, no built-in LLM parser, no database, and no
dashboard-central workflow state.
