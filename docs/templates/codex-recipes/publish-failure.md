# Publish Failure Diagnosis

## Prompt

Find out why publish failed. Start with local audit data and journal entries.

## Command Path

```powershell
revitcli journal stats
revitcli journal show --limit 10
revitcli journal verify
revitcli check --output table
revitcli profile simulate issue
revitcli publish issue --dry-run
revitcli report weekly --window 14d --output markdown
```

## Handoff

Separate setup failures, profile validation errors, model check blockers,
export blockers, and journal integrity failures. Ask before retrying a real
publish.
