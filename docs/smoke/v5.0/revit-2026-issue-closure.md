# Revit 2026 v5.0 Issue Closure Smoke

> Captured: 2026-05-22.
> Status: PASS for controlled Revit 2026 issue-closure smoke.

This is controlled live Revit evidence for the v5.0 Issue Closure Workbench.
The run used the installed CLI/add-in against a running Revit 2026 process,
performed a reviewed sheet issue metadata write, created a receipt, rolled the
write back, verified the journal signature, and wrote an issue package.

This file does not make claims for Revit 2024 or Revit 2025.

## Environment

| Field | Value |
| --- | --- |
| Revit install | `D:\revit2026\Revit 2026` |
| Live document | `D:\桌面\revit\revit_cli.rvt` |
| CLI path | `C:\Users\Lenovo\AppData\Local\RevitCli\bin\revitcli.exe` |
| CLI version | `2.3.0` |
| Installed add-in version | `2.3.0` |
| Live add-in version | `2.3.0` |
| Smoke report | `D:\temp\revitcli-install-20260522191247\.revitcli\smoke\revit-2026-v5-apply-rollback-pass.json` |
| Sheet plan | `D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json` |
| Sheet receipt | `D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json.receipt.json` |
| Issue package | `D:\temp\revitcli-install-20260522191247\.revitcli\smoke\v5-issue-2026-apply-rollback.zip` |
| Journal signature | `D:\temp\revitcli-install-20260522191247\.revitcli\journal.jsonl.sig` |

## Scope

The run selected wall element `337596` with filter `标记 = TEST` for the
baseline dry-run write check:

```text
[337596] 常规 - 200mm: "TEST" -> "revitcli-v5-smoke"
```

For sheet issue metadata, the controlled model uses localized sheet parameters,
so the smoke used this parameter map:

```yaml
issueCode:
  - 审核者
issueDate:
  - 设计者
```

The generated sheet issue plan contained exactly two actions:

```text
[338301] jianzhu1 审核者: "审核者" -> "R03"
[338301] jianzhu1 设计者: "设计者" -> "2026-05-22"
```

## Passing Commands

The report recorded 28 steps, all with exit code `0`:

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
sheets issue-meta --selector jianzhu2 --issue-code R03 --issue-date 2026-05-22 --plan-output D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json --dry-run --output json --param-map D:\temp\revitcli-install-20260522191247\.revitcli\smoke\sheet-issue-param-map.yml
plan show D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json --output json
plan apply D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json --dry-run --max-changes 500
issue preflight --profile D:\temp\revitcli-install-20260522191247\profiles\v5-issue.yml --output json
issue package --profile D:\temp\revitcli-install-20260522191247\profiles\v5-issue.yml --bundle-path D:\temp\revitcli-install-20260522191247\.revitcli\smoke\v5-issue-2026-apply-rollback.zip --dry-run --sign-journal --include-receipts --output json
plan apply D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json --yes --max-changes 500
rollback D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json.receipt.json --dry-run --max-changes 500
rollback D:\temp\revitcli-install-20260522191247\.revitcli\plans\v5-sheet-issue-smoke.json.receipt.json --yes --max-changes 500
journal sign --dir D:\temp\revitcli-install-20260522191247
journal verify --dir D:\temp\revitcli-install-20260522191247
issue package --profile D:\temp\revitcli-install-20260522191247\profiles\v5-issue.yml --bundle-path D:\temp\revitcli-install-20260522191247\.revitcli\smoke\v5-issue-2026-apply-rollback.zip --sign-journal --include-receipts --output json
```

## Rollback Verification

After the approved rollback, a fresh live snapshot showed the controlled sheet
metadata restored to the original values:

```json
{
  "number": "jianzhu2",
  "name": "jianzhu1",
  "reviewer": "审核者",
  "designer": "设计者"
}
```

The run also produced:

- `v5-sheet-issue-smoke.json.receipt.json`
- `journal.jsonl.sig`
- `journal.key`
- `v5-issue-2026-apply-rollback.zip`

## Remaining Gaps

- Revit 2024 and Revit 2025 still require controlled issue-closure smoke.
- The tested sheet was a small controlled model with one sheet; larger models,
  worksharing, linked models, and office-specific titleblock parameter maps
  still need pilot evidence.
- This smoke used a localized parameter map because the default issue metadata
  candidates were not present in the model.
