# Revit 2026 Real Smoke Acceptance

This is the internal acceptance gate for the Revit 2026 vertical slice:

```text
doctor -> status -> query --id -> query <category> --filter -> set --dry-run -> set -> query confirm -> restore
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
| Old value | Non-null value so `set` can restore it exactly |
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
  -OutputPath ".\revitcli-smoke-2026-dry-run.json"
```

Expected result:

- Exit code `0`.
- Report JSON includes `cliVersion`, `installedAddinVersion`, `liveAddinVersion`, `manifestAssemblyPath`, `serverInfoPath`, `oldValue`, `testValue`, and every command step with its exit code and output.
- `applied` is `false`.

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
- The script writes the test value, confirms it with `query --id`, restores the old value with `set --id`, then confirms the restore with `query --id`.

If the apply step fails after the write attempt, the script still tries to restore the original value. Treat any restore failure as blocking.

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
