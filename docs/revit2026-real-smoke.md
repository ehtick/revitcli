# Revit 2026 Real Smoke Acceptance

This is the internal acceptance gate for the Revit 2026 vertical slice:

```text
doctor -> status -> query --id -> query <category> --filter -> set --dry-run -> set --yes -> query confirm -> restore
```

For releases that touch the v1.5 auto-fix path (`fix` / `rollback` commands or
recipe matching), also run the **fix loop addendum** at the end of this doc.

The goal is to prove that the CLI, installed add-in, live add-in HTTP server, Revit API bridge, dry-run preview, transaction write, and restore path all work against a real Revit 2026 document.

## Prerequisites

- Windows machine with Revit 2026 installed.
- Revit API DLLs exist under the selected install directory:
  - `RevitAPI.dll`
  - `RevitAPIUI.dll`
- RevitCli CLI and add-in installed from the same build.
- If Revit is running during `scripts/install.ps1`, CLI files update
  immediately and add-in files are staged for the next Revit restart.
- Revit 2026 restarted after installing the add-in.
- A project document is open before running `status`, `query`, or `set`.
- The test model contains at least one wall or door that can be queried by a stable filter.

If Revit 2026 is installed outside `%ProgramFiles%`, pass the path explicitly:

```powershell
$revit2026 = "D:\revit2026\Revit 2026"
```

## Test Model Contract

Record these before running the smoke:

| Field | Requirement |
|---|---|
| Model path | Full path to the `.rvt` used for the run |
| Revit build | Revit 2026 build shown by `status` |
| Category | Prefer `walls` or `doors` |
| Element ID | Stable element ID returned by `query --id` |
| Filter | Must match exactly one element and must not depend on the parameter being written |
| Safe parameter | Writable text parameter, preferably `Comments` or another project-safe text parameter |
| Old value | Existing parameter property; empty strings are restorable with `--clear-value`, but null values are rejected |
| Test value | Non-empty unique value, for example `revitcli-smoke-20260426` |

Do not run the apply step if the dry-run preview does not show the target element ID and the exact old-value to new-value transition.

## Install From Source Tree

Use this when validating a local branch before review:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 `
  -RevitYears 2026 `
  -Revit2026InstallDir "D:\revit2026\Revit 2026" `
  -Force
```

`-RevitInstallDir` is still accepted as a legacy alias for 2026, but new
scripts should use `-Revit2026InstallDir`.

Expected result:

- CLI is copied to `%LOCALAPPDATA%\RevitCli\bin`.
- Add-in files are copied to `%LOCALAPPDATA%\RevitCli\addin\2026`.
- Manifest exists at `%APPDATA%\Autodesk\Revit\Addins\2026\RevitCli.addin`.
- `%LOCALAPPDATA%\RevitCli\install.json` records the installed CLI version and `2026`.

Restart Revit 2026 after this step.

## Manual Baseline Commands

Run these first and record command, exit code, and key output:

```powershell
revitcli doctor
$LASTEXITCODE

revitcli status
$LASTEXITCODE

revitcli query --id 12345 --output json
$LASTEXITCODE

revitcli query walls --filter "Mark = W-01" --output json
$LASTEXITCODE

revitcli set walls --filter "Mark = W-01" --param Comments --value "revitcli-smoke-20260426" --dry-run
$LASTEXITCODE
```

Only continue when:

- `doctor` exits `0`.
- `status` shows Revit 2026, an active document, and the expected add-in version.
- `query --id` returns exactly one element.
- `query <category> --filter` returns exactly one element, with the same ID.
- `set --dry-run` shows the target ID plus old and new parameter values.

## Scripted Smoke

Prefer the scripted smoke after the manual commands identify a safe element:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-revit2026.ps1 `
  -RevitInstallDir "D:\revit2026\Revit 2026" `
  -ElementId 12345 `
  -Category walls `
  -Filter "Mark = W-01" `
  -Param Comments `
  -Value "revitcli-smoke-20260426" `
  -V4Workbench `
  -V4ProjectDir . `
  -OutputPath ".\revitcli-smoke-2026-dry-run.json"
```

Expected result:

- Exit code `0`.
- Report JSON includes `cliVersion`, `installedAddinVersion`, `liveAddinVersion`, `manifestAssemblyPath`, `serverInfoPath`, `oldValue`, `testValue`, and every command step with its exit code and output.
- Each command step includes `attempt` and `retrySafe` values. Explicitly
  transient add-in communication timeouts may be retried once for read-only or
  dry-run commands, and both the failed attempt and the successful retry remain
  in the report. Write and rollback commands are not retried automatically.
- `applied` is `false`.
- `v4Workbench` is `true`, and the step list includes `workbench verify`,
  `workbench handoff`, live `inspect schedules` / `inspect sheets`,
  `schedule list`, and read-only `schedule export` evidence from the active
  Revit document.

Then run the apply/confirm/restore smoke:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-revit2026.ps1 `
  -RevitInstallDir "D:\revit2026\Revit 2026" `
  -ElementId 12345 `
  -Category walls `
  -Filter "Mark = W-01" `
  -Param Comments `
  -Value "revitcli-smoke-20260426" `
  -Apply `
  -OutputPath ".\revitcli-smoke-2026-apply.json"
```

Expected result:

- Exit code `0`.
- `applied` is `true`.
- The script writes the test value with `set --yes`, confirms it with `query --id`, restores the old value with `set --id ... --yes`, then confirms the restore with `query --id`.

If the apply step fails after the write attempt, the script still tries to restore the original value. Treat any restore failure as blocking.

## v5.0 Issue Closure Addendum

Run this only when validating the v5.0 issue-closure workbench. This addendum is
separate from the legacy `set` apply/restore smoke above. Older v4 workbench
or `set` evidence does not prove v5.0 issue closure.

### Controlled Model Contract

Use a disposable copy of a real project or a dedicated smoke RVT. Do not run the
apply leg against a central production model.

Record these extra fields:

| Field | Requirement |
|---|---|
| Issue profile | `issue-profile.v1` YAML with local paths and explicit mutation plans |
| Sheet selector | Must resolve to known disposable sheets before apply |
| Issue code/date | Values chosen only for the smoke run |
| Sheet plan path | Under `.revitcli/plans/`, committed only as sanitized evidence if needed |
| Receipt path | `<sheet-plan>.receipt.json` after approved apply |
| Package path | Under `.revitcli/smoke/` or another disposable output directory |
| Journal state | `journal verify` must pass after rollback |

### Dry-Run Issue Closure

This proves the issue-closure contract without writing the model:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-revit2026.ps1 `
  -RevitInstallDir "D:\revit2026\Revit 2026" `
  -ElementId 12345 `
  -Category walls `
  -Filter "Mark = W-01" `
  -Param Comments `
  -Value "revitcli-smoke-20260522" `
  -V4Workbench `
  -V4ProjectDir . `
  -V5IssueClosure `
  -V5ProjectDir . `
  -V5IssueProfile .\profiles\v5-issue.yml `
  -V5IssueBundlePath .\.revitcli\smoke\v5-issue-closure.zip `
  -V5SheetSelector all `
  -V5IssueCode R03 `
  -V5IssueDate 2026-05-22 `
  -V5SheetParamMap .\.revitcli\smoke\sheet-issue-param-map.yml `
  -OutputPath ".\revitcli-smoke-2026-v5-dry-run.json"
```

Expected v5 dry-run evidence:

- `workbench verify --contract workbench-contract.v2` returns `success=true`.
- `sheets issue-meta --dry-run` writes a reviewed sheet issue plan.
- `-V5SheetParamMap` can map `issueCode` / `issueDate` to local or localized
  sheet/titleblock parameters when the default English candidates are not
  present.
- `plan show` can render that plan as JSON.
- `plan apply --dry-run` previews the plan without writing.
- `issue preflight` returns `issue-preflight-report.v1`.
- `issue package --dry-run --sign-journal --include-receipts` returns
  `issue-package-receipt.v1` and does not write the bundle.
- The report contains `v5IssueClosure=true`, `v5IssueProfile`,
  `v5IssueBundlePath`, and `v5SheetIssuePlanPath`.
- The script refuses to continue if CLI, installed add-in, and live add-in
  versions do not match.
- `-V5WriteIssuePackage` is not part of the dry-run lane and fails unless an
  explicit disposable `-V5IssueBundlePath` is provided.

### Apply, Receipt, Rollback

Add `-V5ApplySheetIssue` only after the dry-run plan has been reviewed and the
model copy is disposable. Add `-V5WriteIssuePackage` only with a disposable
non-existing bundle path when approved package zip/receipt writing is also part
of the smoke:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-revit2026.ps1 `
  -RevitInstallDir "D:\revit2026\Revit 2026" `
  -ElementId 12345 `
  -Category walls `
  -Filter "Mark = W-01" `
  -Param Comments `
  -Value "revitcli-smoke-20260522" `
  -V5IssueClosure `
  -V5ProjectDir . `
  -V5IssueProfile .\profiles\v5-issue.yml `
  -V5IssueBundlePath .\.revitcli\smoke\v5-issue-closure-2026.zip `
  -V5SheetSelector all `
  -V5IssueCode R03 `
  -V5IssueDate 2026-05-22 `
  -V5SheetParamMap .\.revitcli\smoke\sheet-issue-param-map.yml `
  -V5ApplySheetIssue `
  -V5WriteIssuePackage `
  -OutputPath ".\revitcli-smoke-2026-v5-apply-rollback.json"
```

Expected apply evidence:

- `issue preflight` ran before any `plan apply --yes`.
- `plan apply --yes` creates `<sheet-plan>.receipt.json`.
- `rollback <receipt> --dry-run` reports a safe apply command or explains
  conflicts/errors.
- `rollback <receipt> --yes` restores the sheet issue metadata.
- `journal sign --dir <project>` creates the local journal signature.
- `journal verify` passes after rollback.
- `issue package` without `--dry-run` writes the disposable bundle and package
  receipt when `-V5WriteIssuePackage` is set.
- The report includes all apply, rollback, package, and journal command output.

### Fault Injection

Before claiming v5.0 production readiness, capture at least these failure cases
on the controlled model or adjacent disposable files:

- Missing issue profile: script fails before any sheet issue apply.
- Read-only package directory or permission-denied output path: package dry-run
  remains safe, approved package write reports the path failure.
- Stale plan old values: `plan apply --yes` refuses to write when the live
  sheet value no longer matches the plan baseline.
- Tampered receipt or plan hash mismatch: rollback refuses the artifact.
- Live add-in mismatch: the script blocks when CLI, installed add-in, and live
  add-in versions differ.
- Rollback conflict: rollback dry-run withholds the safe apply command until the
  conflict is resolved.

Write live evidence to `docs/smoke/v5.0/revit-2026-issue-closure.md`. Until
that file exists, keep `docs/smoke/v5.0/gap-report.md` as the source of truth
and describe Revit 2026 v5.0 issue closure as not live verified.

## Evidence Packet

Attach or paste these items into the PR or review handoff:

```text
Date/time:
Branch/commit:
Machine:
Revit install dir:
Revit build:
Model path:
Model preconditions:
  category:
  elementId:
  filter:
  safe parameter:
  old value:
  test value:

Commands:
  command:
  exit code:
  key output:

Smoke reports:
  dry-run report path:
  apply report path:

Result:
  PASS / FAIL
  follow-up:
```

## Current Local Evidence

Last verified: `2026-05-19 19:55:55 +08:00`.

The Revit 2026 add-in was installed from the local source tree, Revit was
restarted, and the live server reported matching CLI, installed add-in, and
live add-in version `2.3.0`. A dry-run smoke with `-V4Workbench` passed against
a local Revit 2026 test document using category `levels`, element ID `311`,
filter `id = 311`, parameter `IfcGUID`, and test value
`revitcli-smoke-20260519`.

The final report recorded 16 command steps, each tagged with `retrySafe`, with
zero failed steps and zero retries. During validation, an earlier run exposed a
transient `doctor --check-version 2026` HTTP timeout; the smoke script now keeps
retry evidence visible and limits retries to read-only or dry-run commands.
The final status, query, dry-run set, workbench verify, workbench handoff,
inspect, schedule list/export, schedule create dry-run, and final query
evidence completed with no script failure.

The apply/confirm/restore smoke was intentionally not run on this document
because the discovered writable parameter was not a project-safe text field.
Run the apply leg only on a controlled model with a disposable writable text
parameter such as `Comments`.

## Stop Conditions

Stop and fix the lower layer first when any of these happen:

- `doctor` cannot find `RevitInstallDir`, `RevitAPI.dll`, or `RevitAPIUI.dll`.
- The add-in manifest is missing or points to a DLL that does not exist.
- Installed add-in version, live add-in version, and CLI version do not match.
- `server.json` is missing, stale, has a dead PID, or points to a non-Revit process.
- `status` reports no active document.
- `query --id` returns no element or a different element than expected.
- Filter returns zero or multiple elements.
- Dry-run preview does not include the expected old and new values.
- The safe parameter is missing, null, read-only, or cannot be restored.

## Fix Loop Addendum (v1.5)

Run when shipping changes to `revitcli fix`, `revitcli rollback`, recipe
matching, or any code under `src/RevitCli/Fix/` and
`src/RevitCli.Addin/Services/RealRevitOperations.cs#audit*`.

### Prerequisites

- A `.revitcli.yml` profile in cwd that defines a `fixes:` entry whose
  `category`, `parameter`, and `match`/`replace` (or `value`) target the test
  model's safe parameter chosen above. The starter profiles in `profiles/`
  (`architectural-issue.yml`, `interior-room-data.yml`,
  `general-publish.yml`) all have commented `fixes:` blocks that can be
  uncommented and adjusted.
- The check this fix targets must currently report at least one issue
  (otherwise the dry-run plan is empty and the smoke is meaningless).

### Sequence

```powershell
# 1. Plan only — must not write anything
revitcli fix default --dry-run
$LASTEXITCODE

# 2. Apply with explicit confirmation. Writes a baseline snapshot + journal
#    under .revitcli/fix-baseline-<timestamp>.json (path printed by the CLI).
revitcli fix default --apply --yes
$LASTEXITCODE

# 3. Confirm the model now passes the originally-failing check.
revitcli check default
$LASTEXITCODE

# 4. Roll back using the baseline path printed in step 2.
revitcli rollback .\.revitcli\fix-baseline-<timestamp>.json --yes
$LASTEXITCODE

# 5. Confirm the model is back to its pre-fix state.
revitcli check default
$LASTEXITCODE
```

Or run the full sequence via the smoke script's fix flags:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-revit2026.ps1 `
  -RevitInstallDir "D:\revit2026\Revit 2026" `
  -ElementId 12345 -Category walls -Filter "Mark = W-01" `
  -Param Comments -Value "revitcli-smoke-20260426" `
  -FixDryRun -FixApply -FixCheckName default -FixProfile .\.revitcli.yml `
  -Apply `
  -OutputPath ".\revitcli-smoke-2026-fix.json"
```

### Expected outcomes

- Step 1 prints `Fix plan` with at least 1 action and exit code `0`. No file
  changes appear under `.revitcli/`.
- Step 2 prints `Baseline saved: ...` then `Journal saved: ...`, then issues
  one `SetRequest` per `(param, value)` group. Exit code `0`.
- Step 3 reports the originally-failing check as passing.
- Step 4 prints reverse-write count matching step 2 and exit code `0`.
- Step 5 reports the same failure as step 3's *opposite* — the original issue
  is back.

### Fix-loop stop conditions (in addition to the base list)

- Dry-run reports zero actions. Either the recipe doesn't match the issue
  shape, or `AuditIssue.Source` is `inferred` and `--allow-inferred` was not
  passed.
- Apply step skips `Set` calls and exits `1` with `Error: failed to save
  baseline snapshot: ...`. The `--baseline-output` parent directory is not
  writable.
- Rollback reports `journal/baseline mismatch` or `document path mismatch`.
  Don't force it — the model probably moved between apply and rollback.
