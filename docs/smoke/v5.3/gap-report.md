# RevitCli v5.3 Numbering Controlled Apply Gap Report

v5.3 numbering controlled apply is proceeding as a portable hardening slice
while v5.1 large-sheet fixtures and v5.2 schedule/package live smoke remain
pending. This is an explicit go-forward decision: room and Mark numbering rules
can be strengthened for deterministic plans, reserved numbers, hold numbers,
duplicate-target failure, receipt shape, and rollback conflict checks without
claiming new live Revit production readiness.

## Current Status

| Scope | Status | Evidence |
| --- | --- | --- |
| Room numbering reserved/hold rules | portable verified | `rooms renumber` skips held room numbers and advances through reserved or held generated targets. |
| Mark numbering reserved/hold rules | portable verified | `marks assign` skips held Marks and advances through reserved or held generated targets. |
| Multi-building fixture rules | portable verified | Residential, office, and healthcare-style room/Mark fixtures validate building, department, level, unit/acuity tokens, deterministic sorting, reserved values, and hold values without live Revit writes. |
| Duplicate-target guards | portable verified | Existing room/mark planner checks reject target values already used by other elements and duplicate generated targets. |
| Controlled apply safety | portable verified | `plan apply` keeps room/mark numbering behind `--yes`, `--max-changes`, high-impact confirmation, receipt model identity, and current-value validation. |
| Receipt and rollback | portable verified | Room/mark receipts record rule path, sort/provenance where applicable, plan/skipped counts, rollback actions, affected ids, model/document identity, and plan hash. |
| Revit 2026 numbering controlled apply smoke | not live verified | Requires disposable model copy with representative room, door, and window numbering rules. |
| Journal verify | not live verified | Required for the live v5.3 smoke packet before production-pilot readiness. |

## Gate

Before v5.3 can be called production-pilot ready, record a Windows/Revit 2026
smoke packet under `docs/smoke/v5.3/` with:

- `rooms renumber` dry-run plan using floor/zone prefixes, reserved numbers,
  hold numbers, and a known duplicate-target failure.
- `marks assign` dry-run plan for doors and/or windows using reserved Marks,
  hold Marks, deterministic sorting, and a known duplicate-target failure.
- `plan show`, `plan apply --dry-run`, approved `plan apply`, receipt review,
  `rollback --dry-run`, approved `rollback`, and post-rollback value evidence.
- Current-value conflict injection between plan generation and apply.
- Package of plan files, receipts, rollback output, and `journal verify`
  evidence.

Example command shape for a disposable project copy:

```powershell
revitcli rooms renumber `
  --rule .revitcli/numbering/rooms.yml `
  --scope all `
  --plan-output .revitcli/plans/rooms-numbering.json `
  --dry-run `
  --output markdown

revitcli marks assign `
  --category doors `
  --rule .revitcli/numbering/doors.yml `
  --plan-output .revitcli/plans/door-marks.json `
  --dry-run `
  --output markdown

revitcli plan show .revitcli/plans/rooms-numbering.json --output markdown
revitcli plan apply .revitcli/plans/rooms-numbering.json --dry-run --max-changes 500
revitcli plan apply .revitcli/plans/rooms-numbering.json --yes --max-changes 500 --high-impact-threshold 100 --confirm-high-impact
revitcli rollback .revitcli/plans/rooms-numbering.json.receipt.json --dry-run
revitcli rollback .revitcli/plans/rooms-numbering.json.receipt.json --yes --max-changes 500
revitcli journal verify --dir <project-copy>
```

v5.3 does not make numbering a default automatic fix. Numbering remains
dry-run first, plan reviewed, approval gated, receipt backed, rollbackable, and
local-first.
