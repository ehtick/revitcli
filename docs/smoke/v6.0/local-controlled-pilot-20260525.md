# RevitCli v6.0 Local Controlled Pilot Evidence

This is a local controlled pilot packet for the `revit_cli.rvt` smoke model on
May 25, 2026. It is not office rollout evidence, not a production support
claim, and not permission to mutate a central production model.

## Scope

- Pilot identifier: `v6-local-controlled-pilot-20260525`
- Evidence bundle:
  `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/`
- Machine-readable release-gate summary:
  `docs/smoke/v6.0/local-controlled-pilot-20260525.evidence.json`
- Public model identifier: `revit_cli`
- Model type: sample / synthetic controlled smoke model
- Revit year/build: Revit 2026
- CLI version:
  `2.3.0+05c6d927bcff23777995fbfff7226ecfc55aac3f`
- Installed add-in version:
  `2.3.0+05c6d927bcff23777995fbfff7226ecfc55aac3f`
- Live add-in version:
  `2.3.0+05c6d927bcff23777995fbfff7226ecfc55aac3f`

## Evidence Captured

| Evidence | Path | Result |
|---|---|---|
| Doctor/version proof | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/doctor.json` | `success=true`; connected to Revit 2026; CLI, installed add-in, and live add-in versions match. |
| Live status proof | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/status.json` | Revit 2026 document path is `D:\桌面\revit\revit_cli.rvt`; live add-in version matches the CLI build. |
| Workbench gate | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/workbench.json` | `success=true`; issue count is `0`. |
| Release gate | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/release.json` | `success=true`; `errorCount=0`; `warningCount=0`. |
| Ledger query | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-query.json` | `ledger-query.v1`; 4 source-ledger operations; 0 issues. |
| Ledger validation | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-validate.json` | `ledger-validate.v1`; `valid=true`; 0 issues. |
| Ledger stats snapshot | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-stats.json` | `ledger-stats.v1`; 4 operations; 0 issues. |
| Ledger timeline snapshot | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-timeline.json` | `ledger-timeline.v1`; 4 operations; 1 bucket; 0 issues. |
| Journal signature | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/journal-sign.json` | Signed 2 local pilot evidence journal rows. |
| Journal verification | `.artifacts/live-smoke/revit2026-v6-local-controlled-pilot-20260525/outputs/journal-verify.json` | `isValid=true`; 2 entries; root hash `b915f6cf6ffea40425cb16bf51bba858339e8e00059f07455b919475968d24fe`. |

The local evidence project under the bundle combines successful export replay
and schedule batch-export replay ledger rows copied from prior Revit 2026 live
smoke artifacts. It does not re-run a mutating Revit command.

## Template Coverage

This packet satisfies the v6.0 pilot evidence template fields for:

- `doctor --check-version 2026 --output json`
- `status --output json`
- `workbench verify --contract workbench-contract.v2 --dir . --output json`
- `release verify --strict --output json`
- `ledger query --source ledger --output json`
- `ledger validate --source ledger --output json`
- `ledger stats --source ledger --analytics-snapshot ... --output json`
- `ledger timeline --source ledger --analytics-snapshot ... --output json`
- `journal verify --output json`

It is not office rollout completion because it has no BIM manager
interview, no real office project-copy owner signoff, no support ticket review,
and no multi-user rollout postmortem.

Boundary summary: no SaaS, no MCP, no dashboard-central workflow, no built-in
LLM parser, no database runtime, no central production model mutation, and no
production support claim.
