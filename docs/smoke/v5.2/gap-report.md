# RevitCli v5.2 Schedule Deliverable Closure Gap Report

v5.2 schedule deliverable closure is proceeding as a schedule/package-only hardening slice while v5.1 large-sheet live fixtures remain pending. This is an explicit go-forward decision: schedule export manifests and package receipts can be strengthened locally without claiming new live Revit production readiness.

## Current Status

| Scope | Status | Evidence |
| --- | --- | --- |
| Schedule spec validation | portable verified | Missing fields and duplicate CSV export names are rejected before export. |
| Schedule export manifest | portable verified | Manifest records profile, spec path, manifest path, output directory, format, command, schedule identity, output path, byte count, SHA256, and model/document identity when Revit status is available. |
| Revit 2026 schedule export live smoke | not live verified | Requires disposable project copy with known schedules. |
| Schedule compare live smoke | not live verified | CSV diff semantics are portable; live schedule export inputs still need Revit 2026 evidence. |
| Deliverables bundle and issue package write smoke | not live verified | v5.0 package write exists for issue closure; v5.2 schedule/package closure still needs a dedicated schedule export + bundle packet. |
| Journal verify | not live verified | Required for the live v5.2 smoke packet before production-pilot readiness. |

## Gate

Before v5.2 can be called production-pilot ready, record a Windows/Revit 2026 smoke packet under `docs/smoke/v5.2/` with:

- `schedules batch-export` from a real Revit model, including the manifest JSON and CSV outputs.
- `schedules compare` between baseline and current export directories.
- `deliverables verify`, `deliverables bundle --dry-run`, and approved bundle write where safe.
- `issue package --dry-run` and approved package write where safe.
- Package receipt, child receipt, per-file hash, bundle hash, and `journal verify` evidence.

The smoke scripts expose this as an explicit opt-in lane, not a default claim:

```powershell
.\scripts\smoke-revit.ps1 `
  -Version 2026 `
  -ElementId <id> `
  -Filter '<safe filter>' `
  -V4Workbench `
  -V5IssueClosure `
  -V5ProjectDir <project-copy> `
  -V5IssueProfile <issue-profile.yml> `
  -V52SchedulePackage `
  -V52ScheduleSet issue `
  -V52ScheduleCompareBaselineDir <baseline-schedule-export-dir> `
  -V52ScheduleCompareKeys Mark
```

Approved deliverables bundle writes require `-V52WriteDeliverablesBundle` plus
an explicit disposable `-V52DeliverablesBundlePath`. Approved issue-package
writes remain controlled by `-V5WriteIssuePackage` and an explicit
`-V5IssueBundlePath`.

`-V52ScheduleCompareBaselineDir` is required on purpose: the smoke lane must
compare a known baseline export directory against a fresh current export rather
than silently comparing the new export directory to itself.
