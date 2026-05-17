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
```

## Handoff

Summarize element count, parameters touched, unmatched filters, and rollback
coverage. Ask before `plan apply ... --yes`.
