# v6.0 Local BIMOps Workbench Contract

Status: contract baseline plus staged local ledger runtime.

v6.0 positions RevitCli as a terminal-first, local-first Local BIMOps
Workbench for Revit. The external product phrase can be BIM Release OS, but the
technical kernel is the Revit Model Operations Ledger: a local, deterministic
record of reviewed Revit model operations, release checks, receipts, rollback
pointers, and audit trail evidence.

This contract does not make RevitCli an AI designer, ACC replacement, SaaS
platform, MCP server, built-in LLM parser, or dashboard-central workflow. Those
are non-goals for this baseline.

No SaaS, no MCP, no built-in LLM, no dashboard-central workflow state, and no database runtime are part of this v6.0 contract baseline.

## Command Spine

The v6.0 contract builds on visible shell commands only:

- `doctor --output json`
- `workbench verify --contract workbench-contract.v2 --dir . --output json`
- `release verify --strict --output json`
- `standards validate --output json`
- `issue preflight --output json`
- `issue package --dry-run --output json`
- `deliverables verify --output json`
- `journal verify --dir . --output json`
- `history list --output json`
- `ledger append --action <action> --yes --output json`
- `ledger replay --source ledger --output json`
- `ledger query --source all --output json`
- `ledger validate --source all --output json`
- `ledger stats --source all --output json`
- `ledger timeline --source all --bucket day --output json`
- `ledger analytics --source all --bucket day --output-dir .revitcli/analytics --output json`
- `workflow registry --output json`
- `rollback <receipt> --dry-run`

External agents may call these commands, but they must not bypass dry-run,
plan, explicit approval, receipt, rollback, or journal verification.

Dedicated v6.0 smoke docs gate the portable standards runtime, issue command
spine, deliverables verification spine, ledger views, and workflow registry.
The command-spine runtime gate executes those local parser paths plus signed
`journal verify`, `history-list.v1` output from `history list --output json`,
and safe `rollback --dry-run` preview fixtures. The rollback probe enforces
that preview requests are marked dry-run, and the no-write proof combines a
final file-tree snapshot with event-level no-write evidence. The command-spine
smoke scope remains local-only and does not claim live Revit mutation, cloud
control, dashboard-central state, or built-in LLM behavior.

`ledger append` is the first scoped write-capable v6.0 runtime path. It writes
only local JSONL records under `.revitcli/ledger/operations.jsonl`, defaults to
dry-run unless `--yes` is supplied, and is consumed by `ledger query`,
`ledger replay`, `ledger validate`, `ledger stats`, `ledger timeline`, and
`ledger analytics` through the `ledger` source. Default `ledger replay` is preview-only: it emits
`ledger-replay.v1` with `dryRun=true`, per-step `canApply`/`blockReason`
evidence, no file writes, no Revit startup, no database, and no replacement for
command-specific receipts.

The bounded live replay path is explicit and narrow:
`ledger replay --source ledger --action set --apply --yes --output json`
replays only local ledger records produced by successful approved `set --yes`
operations with frozen affected ids. `ledger replay --source ledger --action
export --apply --yes --output json` replays successful receipt-backed `export`
records by reusing recorded `--format`, selector, and `--output-dir` args;
`ledger replay --source ledger --action schedules.batch-export --apply --yes
--output json` replays successful manifest-backed schedule batch-export records
by reusing the recorded manifest entries and writing CSV outputs. Dry-run or
incomplete export records, missing schedule manifests, other non-set records,
non-approved records, set records without affected ids, and non-ledger sources
remain blocked. Default replay remains preview-only and does not write files or
start Revit.
Successful bounded `--apply --yes` appends a local `ledger-operation.v1` audit
row for the replay operation itself with `command=ledger` and
`action=ledger.replay.apply`, while avoiding a second replayable source action
record.
That replay-apply audit row also captures best-effort live
`modelIdentity`/`modelPath`/`revitVersion` from `/api/status` when available;
status unavailability does not block the replay audit append.

Successful approved `set` operations append a local `ledger-operation.v1`
record after the Revit API write succeeds. The record uses `command=set`,
`action=set`, the resolved target scope, affected element count, and available
affected element ids. This makes live Revit `set` writes visible to
`ledger query --source ledger` while preserving `set` command output and keeping
ledger write failures as non-blocking audit warnings. Dry-run set previews and
`--plan-output` plan creation do not append ledger records.

Successful `export` operations now append the same local ledger record shape
after the export receipt and delivery manifest are written. The record uses
`command=export`, `action=export`, the export format as category, the receipt
path/hash, the output directory as artifact evidence, and best-effort live
`modelIdentity`/`modelPath`/`revitVersion` from `/api/status`; status or ledger
write failures do not change a successful export result.

`workbench verify --contract workbench-contract.v2 --output json` also emits
structured `runtimeEvidence` for the v6.0 gate. `release verify --strict`
requires the scoped `v60LocalBimOpsContractGate` status to pass and requires
all runtime evidence flags to be true; a passing status without bound runtime
evidence is treated as a release-gate failure. The bound evidence includes
structured `historyListEvidence` for JSON count consistency and table row-order
matching, plus `rollbackDryRunEvidence` for action/conflict/error counts, safe
apply command emission, dry-run preview-only behavior, and the absence of a
mutating set request.

## Operations Ledger Record

Every ledger-worthy operation must be representable as an append-only local
record with stable field names and deterministic ordering.

Required fields:

- `schemaVersion`
- `operationId`
- `command`
- `args`
- `workingDirectory`
- `profile`
- `modelIdentity`
- `modelPath`
- `revitVersion`
- `operator`
- `machine`
- `startedAtUtc`
- `endedAtUtc`
- `riskLevel`
- `dryRunRequired`
- `approvalRequired`
- `planPath`
- `planHash`
- `receiptPath`
- `receiptHash`
- `journalPath`
- `rollbackPointer`
- `status`
- `checks`
- `artifacts`

Deterministic receipt rules:

- Element, file, schedule, sheet, and package entries are sorted by stable key.
- Hashes are SHA256 over canonical JSON or file bytes.
- Delivery manifests that declare `receiptHash` must match the referenced
  receipt file bytes, and `ledger validate` must fail mismatches as a local
  receipt-integrity error.
- Optional fields must be omitted or set to `null` consistently by schema.
- Ledger timestamps use ISO 8601 with an explicit UTC offset (`Z` or
  `+/-HH:mm`); timestamps without an offset are treated as ambiguous warnings.
- Validation and timeline time filters preserve invalid timestamp warnings so
  malformed or offset-less records do not disappear during a filtered review.
- Timestamps are recorded but never used as the sole ordering or identity key.
- Re-running the same dry-run against the same model state should produce the
  same plan hash.

## Dry-Run And Approval

Dry-run first is mandatory for write, export, bundle, and local cleanup
operations. A write-capable command must expose a reviewable plan or package
preview before any approved apply step.

Approval requirements:

- High-risk operations require explicit approval.
- `--yes` or equivalent approval flags are only valid after a saved plan or
  dry-run preview exists.
- Hidden mutation is forbidden for `verify`, `audit`, `preflight`, `diff`,
  `review`, `list`, `stats`, and `history` read paths.

## Rollback Preconditions

Rollback is a reviewed operation, not a best-effort undo.

Required rollback preconditions:

- The receipt schema is supported.
- The referenced plan exists when the receipt carries a plan hash.
- The plan hash matches.
- The current value matches the receipt's expected new value unless the command
  explicitly supports a documented conflict override.
- Element identity can be resolved by a stable identifier.
- Missing elements, stale values, and unsupported action sources fail before
  writing.
- Any current-value conflict fails before writing unless a command documents a
  narrow conflict override.

Idempotency:

- A dry-run rollback can be repeated without writing.
- An approved rollback must not perform a second destructive write when the
  model is already restored.
- Partial rollback behavior must be recorded as success, skipped, or failed per
  action.

## Audit Invariants

The audit trail remains local-first and file-first.

Required audit invariants:

- Receipts are locally readable without a server.
- Journals can be signed and verified with `journal verify`.
- Delivery packages include manifests and per-file hashes.
- Workbench and release gates are terminal commands, not dashboard state.
- A local dashboard or TUI may view receipts and trends, but it cannot be the
  source of truth.
- Team policy and standards profiles are versionable files.

## Standards Runtime

The standards runtime is a governed file contract, not a cloud policy service.
It can reference sheet naming, issue metadata, schedule specs, view templates,
link policy, family policy, export policy, receipt retention, and rollback
requirements.

Required behavior:

- `standards validate` checks local files.
- `standards install --dry-run` previews local file writes before copying.
- Profile changes are reviewable through receipts, history, or local VCS.

## Project Memory

Project memory is a queryable local history, not LLM memory.

Allowed sources:

- receipts
- journals
- histories
- issue packages
- delivery manifests
- standards validation reports
- workbench/release verification output

Project memory can summarize recurring failures, repeated commands, sheet and
schedule drift, package history, and release evidence. It must not silently
write Revit models.

`history list --output json` emits `history-list.v1` so the local history
source is machine-readable through the same terminal contract used by ledger
and release gates.

The first runtime surfaces for this memory are read-only local artifact
commands plus an append-only local ledger writer:

- `ledger append` emits `ledger-append.v1` and can append
  `ledger-operation.v1` records to `.revitcli/ledger/operations.jsonl` only
  when `--yes` is supplied; dry-run output does not mutate files.
- approved `set` writes append `ledger-operation.v1` records with
  command/action, target, affected count, affected id evidence, and
  best-effort live `modelIdentity`/`modelPath`/`revitVersion` from `/api/status` after a
  successful Revit write. If status is unavailable, the write remains
  successful and only the status-derived fields are omitted.
- successful `export` writes append `ledger-operation.v1` records with
  receipt path/hash, output artifact evidence, command args, and best-effort
  live model/version evidence after the export receipt is persisted.
- `schedules batch-export` writes append `ledger-operation.v1` records after
  the schedule export manifest is persisted. The records use
  `action=schedules.batch-export`, the output format as category, the manifest
  as artifact evidence, successful CSV paths as evidence links, and
  best-effort live model/version evidence. Manifests with export error issues
  append `status=failed` so partial schedule-export failures remain auditable.
- `ledger query` emits `ledger-query.v1` from journal, history, delivery
  manifest, workflow receipt files, and local appended ledger records.
- `ledger replay` emits `ledger-replay.v1` from local appended ledger records
  as a deterministic default preview. `--source ledger --action set --apply
  --yes` sends eligible approved set records with frozen ids to Revit, and
  `--source ledger --action export --apply --yes` replays successful
  receipt-backed export records with recorded args. `--source ledger --action
  schedules.batch-export --apply --yes` replays successful manifest-backed
  schedule batch-export records with recorded manifest output entries.
- `ledger validate` emits `ledger-validate.v1` and checks source readability,
  artifact links, receipt status, declared receipt hash values, timestamp
  format, and invalid timestamp warning preservation under time filters.
- `ledger stats` emits `ledger-stats.v1` and summarizes operation counts by
  source, action, category, operator, receipt status, issue severity, and
  project directory when repeated `--project` roots are supplied, including
  issue source counts and malformed local artifact evidence without writing
  files by default. `--analytics-snapshot` persists the same JSON as a local
  file, and `--from-analytics-snapshot` reads the snapshot back without a
  database.
- `ledger timeline` emits `ledger-timeline.v1` and buckets project memory by
  day or hour with source, action, category counts per bucket, operator counts
  per bucket, receipt status, issue severity, unbucketed timestamp evidence, and
  unbucketed warning preservation under time filters. Repeated `--project`
  roots add explicit local cross-project timeline aggregation with
  `projectDirectories` and `byProject` counts, without creating a database or
  dashboard state source. `--analytics-snapshot` persists the same JSON as a
  local file, and `--from-analytics-snapshot` reads the snapshot back.
- `ledger analytics` emits `ledger-analytics-bundle.v1`, writes a local
  stats/timeline snapshot pair under the requested `--output-dir`, and reports
  explicit local-only boundary flags. It packages deterministic evidence only;
  it does not start a network service or create a database runtime.

They do not create a database, call a network service, infer Revit writes, or
repair artifacts. Ledger replay applies only the explicit bounded
`--source ledger --action set --apply --yes` path for eligible approved set
records with frozen affected ids.

## Workflow Registry

Governed workflows declare:

- inputs
- outputs
- read/write scope
- risk level
- dry-run command
- approval command
- rollback support
- receipt schema
- acceptance evidence

The first runtime surface is a read-only local command:

```bash
revitcli workflow registry --output json
```

It emits `workflow-registry.v1` with indexed workflow files, inputs, outputs,
read/write scope, risk level, dry-run commands, approval commands, rollback
support, receipt schemas, acceptance evidence, JSON/table/Markdown output
semantic parity, final file-tree snapshot evidence, and event-level no-write
evidence. It does not run workflow steps, write files, create receipts, call
Revit, call a network service, or create a database.

Workflows are local files. The registry indexes them, but the registry is not a
SaaS control plane and not a dashboard-central state source.

## Non-Goals

The v6.0 contract baseline does not implement:

- operations ledger database runtime
- live operations ledger replay/apply runtime beyond the bounded approved
  `set --yes` and receipt-backed `export` source-ledger paths
- cloud sync
- SaaS workspace
- MCP adapter
- built-in LLM parser
- dashboard-central workflow state
- ACC replacement
- automatic design generation
- hidden model mutation

## Acceptance Gate

The contract baseline is acceptable when:

- `workbench verify --contract workbench-contract.v2 --dir . --output json`
  exposes a passing v6.0 contract gate.
- `release verify --strict --output json` checks the v6.0 contract and gap
  report.
- `ledger append --action smoke.append --output json` emits
  `ledger-append.v1` without writing files.
- `ledger replay --source ledger --output json` emits `ledger-replay.v1`
  without writing files, applying operations, or requiring Revit to be running.
- `ledger replay --source ledger --action set --apply --yes --output json`
  applies only eligible approved set records with frozen affected ids.
- `ledger replay --source ledger --action export --apply --yes --output json`
  applies only eligible successful export records with recorded format,
  selector, and output directory args.
- `ledger replay --source ledger --action schedules.batch-export --apply --yes
  --output json` applies only eligible successful schedule batch-export records
  with recorded manifest entries and CSV output paths.
- `ledger query --output json` emits `ledger-query.v1` without writing files or
  requiring Revit to be running.
- `ledger validate --output json` emits `ledger-validate.v1` without writing
  files or requiring Revit to be running.
- `ledger stats --output json` emits `ledger-stats.v1` without writing files or
  requiring Revit to be running.
- `ledger stats --analytics-snapshot .revitcli/analytics/ledger-stats.json
  --output json` persists a local `ledger-stats.v1` snapshot, and
  `ledger stats --from-analytics-snapshot
  .revitcli/analytics/ledger-stats.json --output json` reads it back without a
  database or dashboard state source.
- `ledger stats --dir project-a --project project-b --output json` emits
  `projectDirectories` and `byProject` from explicitly supplied local roots
  without creating a database or dashboard state source.
- `ledger timeline --output json` emits `ledger-timeline.v1` without writing
  files or requiring Revit to be running.
- `ledger timeline --analytics-snapshot
  .revitcli/analytics/ledger-timeline.json --bucket day --output json` persists
  a local `ledger-timeline.v1` snapshot, and `ledger timeline
  --from-analytics-snapshot .revitcli/analytics/ledger-timeline.json --output
  json` reads it back without a database or dashboard state source.
- `ledger timeline --dir project-a --project project-b --output json` emits
  `projectDirectories` and `byProject` from explicitly supplied local roots
  without creating a database or dashboard state source.
- `ledger analytics --output-dir .revitcli/analytics --output json` emits
  `ledger-analytics-bundle.v1` and writes local `ledger-stats.v1` plus
  `ledger-timeline.v1` snapshots without creating a database, network service,
  or dashboard state source.
- `docs/smoke/v6.0/pilot-evidence-template.md` documents the v6.0 office
  rollout pilot evidence packet for controlled project-copy pilots, including
  doctor, workbench, release, ledger, analytics snapshot, journal verify,
  rollback, user-review evidence, BIM manager signoff, project-copy owner
  signoff, support ticket review, multi-user rollout postmortem, and a packet
  `Pilot identifier` matching the registered pilot id.
  `release pilot scaffold` creates the packet and `release pilot validate`
  checks it before rollout status is updated. `release pilot register` dry-runs
  or writes the completed-pilot entry after validation and reports register nextActions
  for validation failures, dry-run writes, and post-write status checks. `release pilot status`
  reports the machine-readable office pilot rollout progress, registered packet
  validation, per-pilot `missingEvidence`, and aggregate
  `missingEvidenceSummary` flags without mutating status. It also reports
  `evidenceCompleteOfficePilotCount` and
  `remainingEvidenceCompleteOfficePilotCount` so registered-but-incomplete
  pilots are not confused with evidence-complete rollout progress. It reports
  `productionSupportReviewPath` and machine-readable `nextActions` for
  remaining pilot intake steps.
  `release pilot claim` is dry-run by default, reports machine-readable
  `claimBlockers` and `nextActions`, and writes the office rollout completion
  claim only after validated completed pilot evidence reaches the threshold;
  production support still requires an explicit
  `--production-support` request plus
  `--support-review docs/smoke/v6.0/<support-review>.md` after private support
  review. Support review creation is deferred until the completed pilot
  threshold is satisfied, so premature production-support requests keep the
  next actions on pilot packet intake first. The written status records
  `productionSupportReviewPath`; it is not a production support claim by
  default.
- `docs/smoke/v6.0/office-rollout-status.json` records the current
  machine-readable rollout status, minimum pilot count, completed pilot count,
  per-pilot evidence flags, public-safe repo-relative Markdown evidence packet
  paths, optional production support review summary path, and
  no-completion/no-production-support boundary.
- `workflow registry --output json` emits `workflow-registry.v1` without
  running workflow steps, writing files, or requiring Revit to be running.
- Missing contract or gap-report files fail the gates.
- The gap report states that live ledger replay/apply beyond approved `set`
  records, receipt-backed `export` records, and manifest-backed
  `schedules.batch-export` records, database runtime, analytics service
  runtime, broader live Revit ledger integration, and office pilots remain
  future work beyond bounded
  set/export/schedule replay.
