# v5.0 Demo and Pilot Playbook

> Status: planning baseline for the v5.0 Issue Closure Workbench Quality
> Release.

This playbook keeps demos and pilots aligned with the core product boundary:
terminal-first, local-first, deterministic, dry-run first, receipt-backed,
rollbackable, and auditable.

## Setup Artifacts

The demo pack has two concrete local artifacts:

- `profiles/v5-issue.yml`: sample `issue-profile.v1` profile.
- `scripts/v5-issue-day-demo.ps1`: dry-run-first issue-day walkthrough.

Bootstrap a project copy before running the demo:

```powershell
mkdir .revitcli -Force
copy profiles/v5-issue.yml .revitcli/issue.yml
copy profiles/architectural-issue.yml .revitcli.yml
mkdir .revitcli/history -Force
revitcli snapshot --output .revitcli/history/baseline.json
```

Then run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/v5-issue-day-demo.ps1 `
  -ProjectDir . `
  -RevitYear 2026 `
  -IssueProfile .revitcli/issue.yml `
  -Baseline .revitcli/history/baseline.json `
  -BundlePath deliverables/issue-package.zip
```

## Demo 1: Issue-Day Dry Run

Goal: show a BIM manager whether a model/package is ready to issue without
writing model data or delivery files.

```powershell
revitcli doctor --check-version 2026
revitcli status --output json
revitcli workbench verify --contract workbench-contract.v2 --dir . --output markdown
revitcli issue preflight --profile .revitcli/issue.yml --output markdown --fail-on warning
revitcli issue diff --from .revitcli/history/baseline.json --to current --review --output markdown
revitcli issue package --profile .revitcli/issue.yml `
  --bundle-path deliverables/issue-package.zip `
  --dry-run `
  --include-receipts true `
  --sign-journal `
  --output markdown
```

Acceptance evidence:

- `workbench verify` returns the v2 schema and passes issue-closure checks.
- `issue preflight` lists artifacts, commands, mutation-plan references, and
  hidden-mutation status.
- `issue package --dry-run` writes no ZIP and lists planned child files and
  receipt expectations.

## Demo 2: Sheet Issue Metadata Apply and Rollback

Goal: prove a controlled sheet metadata write can be reviewed, applied,
recorded, and rolled back.

Run only on a disposable controlled model or project copy.

```powershell
revitcli sheets issue-meta `
  --selector all `
  --issue-code R03 `
  --issue-date 2026-05-22 `
  --plan-output .revitcli/plans/sheet-issue-r03.json `
  --dry-run `
  --output markdown

revitcli plan show .revitcli/plans/sheet-issue-r03.json --output markdown
revitcli plan apply .revitcli/plans/sheet-issue-r03.json --dry-run --max-changes 500
revitcli plan apply .revitcli/plans/sheet-issue-r03.json --yes --max-changes 500
revitcli rollback .revitcli/plans/sheet-issue-r03.json.receipt.json --dry-run
revitcli rollback .revitcli/plans/sheet-issue-r03.json.receipt.json --yes
revitcli journal verify
```

Acceptance evidence:

- The plan lists old/new values and skipped parameters.
- The receipt includes model identity, affected sheet ids, old/new values,
  plan hash, source operation, and rollback actions.
- Rollback restores target values and fails on current-value conflict.
- `journal verify` passes after apply and rollback evidence is recorded.

For room/mark numbering pilots, the receipt must also include the rule path,
plan action count, skipped count, and deterministic sort evidence when the
numbering plan declares sort fields.

## Demo 3: Package Traceability

Goal: prove a delivery package can be traced back to manifests, receipts,
per-file SHA256 evidence, bundle hash, and journal evidence.

```powershell
revitcli deliverables plan --profile .revitcli.yml --output markdown
revitcli deliverables bundle --bundle-path deliverables/issue-package.zip --dry-run --output markdown
revitcli issue package --profile .revitcli/issue.yml `
  --bundle-path deliverables/issue-package.zip `
  --include-receipts true `
  --sign-journal `
  --output json
revitcli deliverables verify --output markdown
revitcli journal verify
```

Acceptance evidence:

- Every packaged file has a manifest or child-receipt reference.
- Schedule export manifests and schedule diff reports show CSV paths, byte
  counts, and SHA256 evidence for baseline/current review.
- The issue-package receipt includes bundle hash and optional journal
  signature path, and each child file entry includes its own SHA256.
- `deliverables verify` can find referenced receipts.

## Pilot Model Ladder

| Model | Purpose | Mutating commands allowed |
| --- | --- | --- |
| Small clean sample | Contract and docs reproduction | Yes, with rollback. |
| Synthetic sheet-heavy model | 100-300 sheet issue metadata and package scale | Yes, with rollback. |
| Real project copy | User workflow realism | Yes only after dry-run acceptance; never on central production model. |
| Linked/workshared copy | Coordination and permission risk discovery | Read-only first; writes only with explicit pilot approval. |

## Pilot Evidence Packet

Record this for every pilot:

```text
Date/time:
Commit:
CLI version:
Installed add-in version:
Live add-in version:
Revit year/build:
Machine class:
Public model identifier:
Project class:
Model type: sample / synthetic / project copy / linked-workshared copy
Profile identifier:
Commands run:
Dry-run artifact identifiers:
Apply artifact identifiers:
Receipt identifiers:
Rollback result:
Journal verify result:
Package identifier:
Failures:
User trust notes:
Follow-up:
```

Keep raw machine names, model paths, receipt paths, package paths, and client
or project names in the private pilot notes only. Public PR/release handoff
should use stable identifiers such as `pilot-2026-05-office-copy-01` and
`issue-package-r03`.

## User Interview Guide

Do not ask "Do you want a CLI?" Ask about the release work.

Questions:

- What changes most often in the last 48 hours before issue?
- Which Revit batch operations are scary enough that you avoid automating them?
- Which issue-day mistakes have actually reached a client or contractor?
- How do you prove a sheet date, revision, number, or package is correct?
- Which standards exist but are applied inconsistently across teams?
- What would make you trust a tool that writes to a Revit model?
- Which is more important for trust: dry-run, approval, receipt, rollback, or
  journal signature?
- If Codex or another agent calls RevitCli for you, what must it never do?
- Which commands would you allow on a real project copy this week?
- Which commands should stay experimental until more proof exists?

Role-specific follow-ups:

### BIM Manager

- Which office standard is hardest to keep consistent at issue time?
- Would a versioned issue profile be easier to govern than a checklist PDF?
- What receipt fields would you need before accepting a model write?

### BIM coordinator

- Which repeated sheet, room, mark, schedule, or link checks interrupt you most?
- Which dry-run output would make a batch operation reviewable in under five minutes?
- Where do current Excel/Dynamo/pyRevit handoffs lose traceability?

### Sheet Team

- Which titleblock fields and sheet index fields change most often before issue?
- What is the largest sheet set you would trust after reviewing a plan?
- What rollback evidence would make a sheet metadata update acceptable?

### Project Manager

- What does "ready to issue" mean for your team in the final 24 hours?
- Which package traceability evidence would reduce client/contractor disputes?
- How should blockers be summarized without exposing private model paths?

### IT / Standards Owner

- Where should profiles, receipts, and journals live for confidential projects?
- How should CLI/add-in version mismatch be reported to support staff?
- Which commands must remain unavailable until Windows/Revit smoke evidence exists?

## Pilot Stop Conditions

Stop immediately when:

- RevitCli reports a hidden mutation in a read-only path.
- The live add-in version does not match the installed build.
- Dry-run does not show the expected old/new values.
- A write succeeds but no receipt is written.
- A receipt is missing schema, model/document identity, source operation,
  old/new values, plan hash, or numbering rule provenance for room/mark work.
- A missing receipt, tampered receipt, malformed manifest, path permission
  failure, or package write failure does not produce a stable error code.
- Receipt rollback reports a missing plan file or plan hash mismatch.
- Rollback cannot verify current values before restore.
- Rollback reports a current-value conflict; investigate the intervening edit
  instead of forcing restore.
- The model is a central production model rather than a controlled copy.
- A user cannot explain what changed from the receipt and report.

Portable fault injection checklist:

- Missing profile: run `issue preflight/package` against a missing profile path.
- Missing receipt: run `deliverables verify` against a manifest with a missing
  child receipt.
- Tampered receipt: replace a receipt with malformed JSON and verify
  `receipt-json-invalid`.
- Path permission or write failure: point package/bundle output at an invalid
  or blocked path and confirm no partial ZIP or receipt remains.
- Stale value: edit a target value between apply and rollback and confirm
  rollback reports a current-value conflict.
