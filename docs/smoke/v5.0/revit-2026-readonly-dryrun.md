# Revit 2026 v5.0 Read-Only Dry-Run Smoke

> Captured: 2026-05-22.
> Status: PASS for read-only/dry-run live connectivity. Superseded for Revit
> 2026 write/rollback readiness by
> `docs/smoke/v5.0/revit-2026-issue-closure.md`.

This file records live Revit 2026 evidence that is intentionally narrower than
`revit-2026-issue-closure.md`. It proves the installed CLI/add-in can execute
the v5.0 read-only and dry-run issue-closure lane against a running Revit
process. It does not prove approved sheet metadata writes, receipts, rollback,
or journal verification.

## Environment

| Field | Value |
| --- | --- |
| Revit install | `D:\revit2026\Revit 2026` |
| Live document | `D:\桌面\revit\revit_cli.rvt` |
| CLI path | `C:\Users\Lenovo\AppData\Local\RevitCli\bin\revitcli.exe` |
| CLI version | `2.3.0` |
| Installed add-in version | `2.3.0` |
| Live add-in version | `2.3.0` |
| Smoke report | `D:\temp\revitcli-install-20260522191247\.revitcli\smoke\revit-2026-v5-readonly-dryrun-pass.json` |

## Passing Commands

All commands in the smoke report exited with code `0`:

```text
doctor --check-version 2026
status
query --id 337596 --output json
query walls --filter "标记 = TEST" --output json
set walls --filter "标记 = TEST" --param "标记" --value "revitcli-v5-smoke" --dry-run
workbench verify --dir D:\temp\revitcli-install-20260522191247 --output json
workbench handoff --dir D:\temp\revitcli-install-20260522191247 --output json
status --output json
inspect categories --output json
inspect params walls --output json
inspect schedules --output json
inspect sheets --output json
schedule list --output json
schedule export --category walls --fields Name,Category,Type Name --output json
schedule create --category walls --fields Name --name "RevitCli Smoke Preview" --dry-run --output json
query --id 337596 --output json
workbench verify --contract workbench-contract.v2 --dir D:\temp\revitcli-install-20260522191247 --output json
issue preflight --profile D:\temp\revitcli-install-20260522191247\profiles\v5-issue.yml --output json
issue package --profile D:\temp\revitcli-install-20260522191247\profiles\v5-issue.yml --bundle-path D:\temp\revitcli-install-20260522191247\.revitcli\smoke\v5-issue-2026-readonly-dryrun-pass.zip --dry-run --sign-journal --include-receipts --output json
```

The baseline dry-run target was element `337596` in category `walls`, selected
with filter `标记 = TEST`. The dry-run preview reported:

```text
[337596] 常规 - 200mm: "TEST" -> "revitcli-v5-smoke"
```

No `-Apply`, `-V5ApplySheetIssue`, or `-V5WriteIssuePackage` flag was used.

## Controlled Stop

A stricter dry-run that included `sheets issue-meta` stopped safely before any
write because sheet `jianzhu2` does not expose the default issue metadata
parameters:

```text
issueCode: parameter-missing
issueDate: parameter-missing
```

That result is evidence that the smoke gate detects an unsuitable fixture. It
is not evidence for v5.0 write readiness.

## Remaining NO-GO Items

- Run the same lane on a disposable controlled RVT with mapped sheet issue
  code/date parameters.
- Execute `sheets issue-meta --dry-run`, `plan apply --yes`, receipt capture,
  `rollback --dry-run`, approved rollback, and `journal verify`.
- Repeat or explicitly disclose gaps for Revit 2024 and Revit 2025.
