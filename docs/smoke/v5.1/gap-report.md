# RevitCli v5.1 Sheet Release Control Gap Report

v5.1 sheet release control is production pilot gated. The current work is CLI/shared hardening for sheet issue metadata and sheet renumber plans; it does not claim new production readiness until live Revit smoke and pilot evidence are recorded.

## Current Status

| Scope | Status | Evidence |
| --- | --- | --- |
| Revit 2026 sheet issue dry-run/plan/receipt/rollback path | inherited baseline verified | v5.0 controlled issue-closure smoke covers one localized sheet issue apply, receipt, rollback, journal verify, and package write. |
| 100 sheet live fixture | not live verified | Portable determinism tests may cover generated plans, but live Revit apply/rollback performance and transaction behavior are not claimed. |
| 300 sheet live fixture | not live verified | Requires disposable Revit 2026 model copy or synthetic fixture with real titleblock parameters. |
| 1000 sheet live fixture | not live verified | Requires explicit performance, timeout, rollback, and journal verify evidence before production pilot use. |
| Mixed titleblock families | not live verified | Portable param-map tests cover missing and ambiguous mappings; real titleblock family behavior remains a Revit smoke gate. |

## Gate

Before v5.1 can be called production-pilot ready, record a Windows/Revit 2026 smoke packet under `docs/smoke/v5.1/` with:

- 100 sheet, 300 sheet, and 1000 sheet fixture outcomes, or a specific reason each fixture is unavailable.
- `sheets issue-meta --dry-run --plan-output` evidence with the titleblock parameter map used.
- `plan apply --dry-run`, approved `plan apply`, receipt creation, `rollback --dry-run`, approved rollback, and `journal verify`.
- Post-rollback evidence that sheet issue fields and sheet numbers returned to their original values.
