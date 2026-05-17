# Weekly Review

## Prompt

Create a weekly model health summary from local history and journal data. Do not
open Revit or write to the model.

## Command Path

```powershell
revitcli history list --limit 10
revitcli history trend --window 14d
revitcli history diff @-2 @-1 --review
revitcli journal stats
revitcli journal show --limit 10
revitcli report weekly --window 14d --output markdown
revitcli workflow receipts --failed-only --output markdown
revitcli workflow suggest --output yaml
```

## Handoff

Summarize score movement, suspicious diffs, frequent journal actions, and any
failed workflow receipts or workflow draft worth saving after review.
