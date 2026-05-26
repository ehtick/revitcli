# RevitCli v5.0 RC Readiness

> Created: 2026-05-22.
> Current status: GO for a Revit 2026-only v5.0 RC.
> Claimed live Revit years: 2026.

This document is the v5.0 RC boundary contract. It prevents the existing CLI
contract surface from being mistaken for production readiness.

## Stable P0 Commands

These are the only command groups that can be considered for the v5.0 Issue
Closure Workbench RC gate:

| Area | Commands | RC expectation |
| --- | --- | --- |
| Connectivity | `doctor`, `status` | Must identify Revit year, add-in version, CLI/add-in mismatch, and connection state before any live claim. |
| Workbench contract | `workbench verify --contract workbench-contract.v2` | Must pass locally and expose `v5RealSmokeDisclosure`, `issuePackageTraceability`, and `v5FaultInjectionCoverage`. |
| Issue closure | `issue preflight`, `issue diff`, `issue package` | Must stay dry-run first, show hidden-mutation and shell-command safety, and write packages only after explicit approval. |
| Sheet issue metadata | `sheets verify`, `sheets issue-meta`, `plan apply`, `rollback` | Must dry-run, create deterministic plans, write receipts, and rollback on controlled RVT copies before an apply claim. |
| Schedules | `schedules batch-export`, `schedules compare` | Must produce traceable export/diff evidence with path, byte count, and SHA256 hashes. |
| Deliverables | `deliverables verify`, `deliverables bundle`, `issue package` | Must preserve manifest, child receipt, per-file SHA256, bundle hash, and package receipt evidence. |
| Journal/receipt review | `journal verify`, `workbench receipts`, `rollback` | Must reject missing, tampered, mismatched, or stale evidence before writes. |

## Experimental / Deferred Commands

These commands can remain visible in the CLI, but they are not v5.0 RC
production claims unless a separate feature gate and real Revit evidence says
otherwise:

| Area | Boundary |
| --- | --- |
| `views template-apply` / `views clone-set` | Experimental write surface until placed-view, template, browser, and rollback behavior is field-tested. |
| `links repair` / `model map-fix` | Experimental coordination repair surface until worksharing locks, linked model paths, coordinates, worksets, and phases are validated on controlled project copies. |
| `rooms renumber` / `marks assign` | Controlled plan surface for v5.0; broad production apply belongs to v5.3 after office-specific rule pilots. |
| `schedule create` / `schedules ensure` writes | Keep behind dry-run/receipt gates; v5.0 RC prioritizes export/compare traceability. |
| Dashboard | Optional local viewer only. It is not the source of truth for RC status. |
| MCP, SaaS, or built-in LLM parser | Not a v5.0 mainline. External agents may call visible shell commands but cannot bypass dry-run, approval, receipt, rollback, or audit trail. |

v5.5 narrows the view/coordination boundary to audit-first portable hardening:
view plans freeze ids and placed-view rollback guards, link repair remains
path/load only with no coordinate moves, and model map-fix keeps writable-probe
evidence while worksharing-lock and `journal verify` evidence remain controlled
Revit gates.

## Evidence Gates

The v5.0 RC is GO only when all of these are true:

1. `workbench verify --contract workbench-contract.v2` passes and includes
   `v5RealSmokeDisclosure`, `issuePackageTraceability`, and
   `v5FaultInjectionCoverage`.
2. `release verify --strict` passes.
3. `docs/smoke/v5.0/revit-<year>-issue-closure.md` exists for every claimed
   live Revit year.
4. Every unverified known Revit year is explicitly marked `not live verified` in
   `docs/smoke/v5.0/gap-report.md`.
5. The controlled smoke records install/doctor/status, issue preflight, sheet
   issue dry-run, approved sheet apply, receipt, rollback dry-run, approved
   rollback, package dry-run, approved package where safe, and `journal verify`.
6. Fault injection evidence covers missing profiles, missing receipts, tampered
   receipts, path/write failures, stale rollback conflicts, and any available
   worksharing-lock behavior.
7. Non-developer BIM users can run the issue-day recipe from the docs without
   source-code knowledge.

## Current Boundaries

- Revit 2024 is not live verified for v5.0 issue closure.
- Revit 2025 is not live verified for v5.0 issue closure.
- Revit 2026 has controlled issue-closure write/rollback/package evidence in
  `docs/smoke/v5.0/revit-2026-issue-closure.md`.
- Revit 2026 evidence is still one small controlled model; larger models,
  worksharing, linked models, and office-specific titleblock maps need pilot
  evidence before a broad production claim.
- Existing portable tests and workbench checks prove contract behavior, not
  live Revit production writes for unverified years or untested project types.
- v5.1-v5.3 feature plans may exist as planning artifacts, but production
  hardening must not claim broader live support until this RC gate has matching
  smoke evidence or explicit pilot findings that change the order.

## Blocked Follow-Up Plans

These plans remain intentionally blocked by the v5.0 product boundary and pilot
evidence requirements, even though the Revit 2026-only RC gate can pass:

- `.codex/features/v5.1-sheet-release-control.md`
- `.codex/features/v5.2-schedule-deliverable-closure.md`
- `.codex/features/v5.3-numbering-controlled-apply.md`

They are valid as execution queues only after `release verify --strict` passes
for the currently claimed Revit live-smoke target and the remaining
non-claimed-year gaps stay disclosed.

## RC Command Sequence

```powershell
dotnet test tests/RevitCli.Tests/
revitcli workbench verify --contract workbench-contract.v2 --dir . --output markdown
revitcli release verify --output markdown
revitcli release verify --strict --output markdown
```

`release verify --output markdown` is the normal maintainer handoff. The strict
variant is the RC blocker: if this document says `NO-GO` or any claimed Revit
year lacks live evidence, strict verification must fail. Revit 2024/2025 remain
non-claimed live targets until their evidence files exist.
