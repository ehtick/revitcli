# Revit Version Compatibility

RevitCli supports Revit 2024, 2025, and 2026, but live confidence comes
from running the smoke matrix on real Windows hosts. Use
`scripts/smoke-revit.ps1 -Version <year>` after installing the matching
add-in and opening the test model in that Revit version.

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

## Capability Matrix

| Surface | 2024 | 2025 | 2026 | Notes |
| ------- | ---- | ---- | ---- | ----- |
| `doctor --check-version` | Supported | Supported | Supported | Checks API DLLs, add-in manifest, server info, and live Revit year. |
| Query / status / audit / check | Smoke required | Smoke required | Verified by existing 2026 flow | Requires the add-in server to be loaded in the active Revit session. |
| `set`, `fix`, `rollback` | Smoke required | Smoke required | Verified by existing 2026 flow | Always use a controlled model and a restorable parameter for write smoke. |
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

The smoke report writes `.revitcli/smoke-<year>.json` and includes the
resolved Revit install path, manifest path, CLI version, installed add-in
version, live add-in version, and every command output.
