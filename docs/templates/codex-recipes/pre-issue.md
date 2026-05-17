# Pre-Issue Dry Run

## Prompt

Check whether this model is ready for issue. Dry-run only; do not write files or
modify the model.

## Command Path

```powershell
revitcli doctor
revitcli status
revitcli standards validate
revitcli profile simulate issue
revitcli workflow init pre-issue
revitcli workflow validate .revitcli/workflows/pre-issue.yml --output markdown
revitcli workflow simulate .revitcli/workflows/pre-issue.yml --output markdown
revitcli inspect schedules --issues-only --output markdown
revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run --output markdown
revitcli check issue --output table
revitcli publish issue --dry-run
revitcli deliverables verify --output markdown
revitcli deliverables bundle --dry-run --output markdown
revitcli workflow receipts --output markdown
```

## Handoff

Summarize blockers, skipped checks, failed rules, export candidates, workflow
receipt status, and delivery manifest issues. Ask for approval before any
non-dry-run publish, bundle write, or model write.
