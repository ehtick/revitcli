# Parameter Change Plan

## Prompt

Plan a parameter update and show exactly which elements would change. Do not
apply the change until approved.

## Command Path

```powershell
revitcli inspect params doors
revitcli inspect params doors --name "Fire*" --writable-only --missing-only --output json
revitcli query doors --filter "name contains Fire" --output table
revitcli set --id 12345 --param "Fire Rating" --value "60min" --dry-run
revitcli set doors --filter "name contains Fire" --param "Fire Rating" --value "60min" --dry-run
revitcli set doors --filter "name contains Fire" --param "Fire Rating" --value "60min" --plan-output .revitcli/plans/fire-rating.json
revitcli plan show .revitcli/plans/fire-rating.json --output markdown
revitcli plan apply .revitcli/plans/fire-rating.json --dry-run
# After explicit approval:
revitcli plan apply .revitcli/plans/fire-rating.json --yes --max-changes 250 --high-impact-threshold 50 --confirm-high-impact
revitcli rollback .revitcli/plans/fire-rating.json.receipt.json --dry-run
revitcli rollback .revitcli/plans/fire-rating.json.receipt.json --yes --max-changes 250
```

## Handoff

Summarize element count, parameters touched, unmatched filters, and rollback
coverage in Chinese. Ask before `plan apply ... --yes`, and ask again before
real `rollback ... --yes`.
