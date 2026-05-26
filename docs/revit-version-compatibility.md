# Revit Version Compatibility

RevitCli supports Revit 2024, 2025, and 2026, but live confidence comes
from running the smoke matrix on real Windows hosts. Use
`scripts/smoke-revit.ps1 -Version <year>` after installing the matching
add-in and opening the test model in that Revit version.

The v2.3.0 GitHub release ZIP packages the Revit 2026 add-in only. Revit
2024 and 2025 remain source-build targets for operators with the matching
Revit API DLLs available.

## Proof States

Use these terms consistently in release notes and README updates:

| State | Meaning |
| ----- | ------- |
| Source-build target | The add-in project has a target framework and Revit API reference path for that year. |
| Smoke required | The surface is expected to run, but no current controlled live smoke evidence is recorded. |
| Live verified | A controlled Windows/Revit smoke evidence packet exists for that exact surface and year. |
| Not live verified | Do not claim production readiness for that surface/year; link the gap report instead. |

The v5.0 issue-closure chain is tracked separately from older set/fix/v4
workbench evidence because it must prove `issue preflight`, sheet issue
dry-run/apply, receipt rollback, `issue package`, and `journal verify`
together.

## Runtime Targets

| Revit year | Add-in target | Default install path |
| ---------- | ------------- | -------------------- |
| 2024 | `net48` | `%ProgramFiles%\Autodesk\Revit 2024` |
| 2025 | `net8.0-windows` | `%ProgramFiles%\Autodesk\Revit 2025` |
| 2026 | `net8.0-windows` | `%ProgramFiles%\Autodesk\Revit 2026` |

Override non-default installs with either
`REVITCLI_REVIT<year>_INSTALL_DIR` or `Revit<year>InstallDir`, for
example:

```powershell
$env:REVITCLI_REVIT2025_INSTALL_DIR = "D:\Autodesk\Revit 2025"
revitcli doctor --check-version 2025
```

When installing from a source tree, pass the same paths explicitly so the
add-in project can find the matching Revit API DLLs:

```powershell
.\scripts\install.ps1 -RevitYears 2024,2025,2026 `
  -Revit2024InstallDir "D:\Autodesk\Revit 2024" `
  -Revit2025InstallDir "D:\Autodesk\Revit 2025" `
  -Revit2026InstallDir "D:\Autodesk\Revit 2026" `
  -Force
```

`-RevitInstallDir` remains a legacy alias for the Revit 2026 install path.

## Capability Matrix

| Surface | 2024 | 2025 | 2026 | Notes |
| ------- | ---- | ---- | ---- | ----- |
| `doctor --check-version` | Supported | Supported | Supported | Checks API DLLs, add-in manifest, server info, and live Revit year. |
| Query / status / audit / check | Smoke required | Smoke required | Verified 2026-04-30 | Requires the add-in server to be loaded in the active Revit session. |
| `set`, `fix`, `rollback` | Smoke required | Smoke required | `set` apply/restore verified 2026-04-30 | Always use a controlled model and a restorable parameter for write smoke. |
| v5.0 issue closure | Not live verified | Not live verified | Live verified 2026-05-22 | See `docs/smoke/v5.0/revit-2026-issue-closure.md`; older v4 workbench or `set` evidence does not prove this chain for other years. |
| `schedule create` | Compatibility risk | Smoke required | Smoke required | The 2024 schedule API path needs explicit confirmation before release claims. |
| Family `ls`, `validate`, `purge`, `export` | Smoke required | Smoke required | Smoke required | Validate with representative loadable families per office standard. |
| Dashboard and profile commands | Supported | Supported | Supported | These run outside Revit; no Revit API DLLs are required. |

## Smoke Matrix

Copy [`docs/ci/smoke-matrix-template.yml`](ci/smoke-matrix-template.yml)
to `.github/workflows/smoke-matrix.yml` in the operator repo. Label the
self-hosted Windows runners `revit-2024`, `revit-2025`, and
`revit-2026`. Each runner should expose the same smoke variables:

- `REVITCLI_SMOKE_ELEMENT_ID`: element id in the open model.
- `REVITCLI_SMOKE_FILTER`: filter that resolves to exactly that element.
- Optional `REVITCLI_SMOKE_CATEGORY`, `REVITCLI_SMOKE_PARAM`, and
  `REVITCLI_SMOKE_VALUE`.
- Optional v5 issue-closure variables are gated by
  `REVITCLI_V5_ISSUE_CLOSURE=true`. Set `REVITCLI_V5_ISSUE_PROFILE` first,
  then optionally set `REVITCLI_V5_SHEET_SELECTOR`, `REVITCLI_V5_ISSUE_CODE`,
  `REVITCLI_V5_ISSUE_DATE`, `REVITCLI_V5_SHEET_PLAN_PATH`, and
  `REVITCLI_V5_SHEET_PARAM_MAP` for localized/custom titleblock parameters.
  Set `REVITCLI_V5_APPLY_SHEET_ISSUE=true` only on a disposable controlled model.
  Set `REVITCLI_V5_WRITE_ISSUE_PACKAGE=true` only with a disposable
  non-existing bundle path.

The smoke report writes `.revitcli/smoke-<year>.json` and includes the
resolved Revit install path, manifest path, CLI version, installed add-in
version, live add-in version, and every command output.

## Latest Local Legacy Smoke Evidence

Last local verification: **2026-04-30**, Windows + Revit 2026.

Evidence summary:

- Installer layered update ran while Revit was open. CLI files updated
  immediately; the 2026 add-in was staged under
  `%LOCALAPPDATA%\RevitCli\staged\addin\2026\...` for the next restart.
- `revitcli doctor --check-version 2026` passed against
  `D:\revit2026\Revit 2026`.
- `revitcli status` connected to the open `revit_cli` model.
- `scripts/smoke-revit.ps1 -Version 2026` passed dry-run and apply/restore
  using wall element `337596`, filter `id = 337596`, parameter `标记`,
  old value `TEST`, and test value `revitcli-smoke-20260430`.
- Post-smoke query confirmed `标记` was restored to `TEST`.
- `revitcli journal sign` and `revitcli journal verify` passed for the local
  smoke journal.

Known gaps:

- Revit 2024 and 2025 live smoke are not yet verified on this machine because
  matching local Revit installs/runners were not available.
- Revit 2026 v5.0 issue closure is live verified in
  `docs/smoke/v5.0/revit-2026-issue-closure.md`.
- Revit 2024 and 2025 are not live verified for the v5.0 issue-closure chain
  until dedicated `docs/smoke/v5.0/revit-<year>-issue-closure.md` evidence
  files replace the corresponding rows in `docs/smoke/v5.0/gap-report.md`.
- The current Revit session may continue running the already-loaded add-in
  until Revit restarts. This is expected after a layered installer update; the
  manifest and installed add-in point to the staged build for the next launch.
