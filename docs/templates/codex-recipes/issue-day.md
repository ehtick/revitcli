# Issue-Day Closure

## Prompt

Help me prepare this project for issue. Stay local and deterministic. Run
read-only and dry-run commands first, summarize blockers, and do not apply,
rollback, or write a package until I explicitly approve.

## Command Path

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

If the sheet issue metadata plan is already reviewed and the model is a
controlled disposable copy, ask for approval before continuing:

```powershell
revitcli plan show .revitcli/plans/sheet-issue-r03.json --output markdown
revitcli plan apply .revitcli/plans/sheet-issue-r03.json --dry-run --max-changes 500
revitcli plan apply .revitcli/plans/sheet-issue-r03.json --yes --max-changes 500
revitcli rollback .revitcli/plans/sheet-issue-r03.json.receipt.json --dry-run --max-changes 500
revitcli rollback .revitcli/plans/sheet-issue-r03.json.receipt.json --yes --max-changes 500
revitcli journal verify
```

Only after package contents and paths are reviewed, ask for approval before
writing the issue package:

```powershell
revitcli issue package --profile .revitcli/issue.yml `
  --bundle-path deliverables/issue-package.zip `
  --include-receipts true `
  --sign-journal `
  --output json
revitcli deliverables verify --output markdown
revitcli journal verify
```

## Handoff

Report the issue preflight result, diff review highlights, dry-run package
contents, receipt paths, rollback result, package path, journal verify result,
and any stop condition. Keep private model names and paths out of public
handoff notes unless the user explicitly asks to include them.
