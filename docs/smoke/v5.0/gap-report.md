# RevitCli v5.0 Live Smoke Gap Report

> Created: 2026-05-22.
> Scope: v5.0 Issue Closure Workbench Quality Release.

This file exists to prevent accidental live-support claims before controlled
Windows/Revit evidence is captured.

## Current Status

| Revit year | v5.0 issue-closure live smoke status | Notes |
| --- | --- | --- |
| Revit 2024 | not live verified | Source-build target exists, but no v5.0 issue preflight/package/sheet apply/rollback evidence is recorded. |
| Revit 2025 | not live verified | Source-build target exists, but no v5.0 issue preflight/package/sheet apply/rollback evidence is recorded. |
| Revit 2026 | live verified for controlled v5.0 issue closure | Revit 2026 is installed at `D:\revit2026\Revit 2026`; installed live doctor/status, baseline query/filter/set dry-run, v4 workbench, v5 workbench, sheet issue dry-run, `plan apply --yes`, receipt, rollback dry-run, approved rollback, `journal sign`, `journal verify`, and approved `issue package` evidence are recorded in `docs/smoke/v5.0/revit-2026-issue-closure.md`. |

## 2026 Evidence Captured On 2026-05-22

This evidence is useful for release engineering. Revit 2026 now has controlled
issue-closure write/rollback evidence; Revit 2024 and 2025 still do not.

- Found Revit 2026 API files under `D:\revit2026\Revit 2026`.
- WSL add-in build passed with
  `dotnet build src/RevitCli.Addin -p:RevitYear=2026 -p:RevitInstallDir="/mnt/d/revit2026/Revit 2026" --no-restore`.
- WSL add-in test project build passed with
  `dotnet build tests/RevitCli.Addin.Tests/RevitCli.Addin.Tests.csproj -p:RevitYear=2026 -p:RevitInstallDir="/mnt/d/revit2026/Revit 2026"`.
- Windows add-in tests passed from a `D:\temp` mirror with
  `dotnet.exe test ...\tests\RevitCli.Addin.Tests\RevitCli.Addin.Tests.csproj -p:RevitYear=2026 -p:RevitInstallDir="D:\revit2026\Revit 2026"` (54 tests).
- Installed CLI/add-in live smoke passed on Revit 2026 with no approved writes:
  `doctor --check-version 2026`, `status`, `query --id 337596`, `query walls
  --filter "标记 = TEST"`, `set walls ... --dry-run`, v4 workbench checks,
  `workbench verify --contract workbench-contract.v2`, `issue preflight`, and
  `issue package --dry-run`.
- The passing read-only report was written locally to
  `D:\temp\revitcli-install-20260522191247\.revitcli\smoke\revit-2026-v5-readonly-dryrun-pass.json`.
- A second dry-run with `sheets issue-meta` stopped safely because the live
  model sheet `jianzhu2` does not expose the default issue code/date
  parameters. That failure is a fixture/profile gap, not a successful write
  evidence substitute.
- After adding a smoke-only sheet parameter map, Revit 2026 live issue-closure
  smoke passed with 28 steps and 0 failed steps:
  `D:\temp\revitcli-install-20260522191247\.revitcli\smoke\revit-2026-v5-apply-rollback-pass.json`.

Still missing for a v5.0 GO claim:

- Revit 2024 controlled issue-closure smoke;
- Revit 2025 controlled issue-closure smoke;
- sheet issue metadata apply, receipt, rollback dry-run, approved rollback,
  issue package, and `journal verify` evidence on any additional Revit year
  claimed in README or release notes.

## Required Replacement Evidence

Replace the relevant row above with a dedicated evidence file only after a
controlled model run records:

- install or staged-install status;
- `doctor --check-version <year>`;
- `status --output json`;
- `workbench verify --contract workbench-contract.v2`;
- `issue preflight`;
- sheet issue metadata dry-run;
- sheet issue metadata apply with receipt;
- rollback dry-run and approved rollback;
- `issue package --dry-run`;
- approved package when safe;
- `journal verify`;
- failures, retries, and stop conditions.

Expected evidence filenames:

- `docs/smoke/v5.0/revit-2024-issue-closure.md`
- `docs/smoke/v5.0/revit-2025-issue-closure.md`
- `docs/smoke/v5.0/revit-2026-issue-closure.md`

Until one of those files exists, release notes and README text must describe
that year as not live verified for v5.0 issue closure.
