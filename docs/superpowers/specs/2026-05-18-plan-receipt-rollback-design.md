# Plan Receipt Rollback Design

> Spec date: 2026-05-18
> Track: v2.4 Safe Batch Plans
> Status: approved for implementation by active v2.4 continuation

## Summary

The v2.4 exit gate says a large door parameter update must be dry-run,
reviewed, applied, and rolled back from terminal artifacts alone. The current
safe-plan implementation already provides `set/import/fix --plan-output`,
`plan show`, `plan apply`, safety thresholds, and `plan-receipt.v1` sidecars.
The missing slice is rollback for non-fix plan receipts.

This slice extends `revitcli rollback` so the same command can accept either:

- a fix baseline snapshot path, preserving the existing behavior; or
- a `plan-receipt.v1` sidecar created by `plan apply`.

## Decisions

| Decision | Outcome |
| --- | --- |
| Command surface | Reuse `revitcli rollback <artifact>` instead of adding `plan rollback`. |
| Receipt contract | Add explicit `rollbackActions` to `plan-receipt.v1`. |
| Set rollback source | Use set receipt preview rows plus the applied parameter name. |
| Import rollback source | Store per-element/per-parameter rollback actions from apply previews. |
| Fix rollback | Keep baseline/journal rollback as the authoritative fix path. |
| Conflict handling | Before writing, dry-run the reverse set and skip if current value no longer matches the receipt-applied value. |
| Model guard | For real receipt rollback, validate current document path/name when the receipt contains model context. |
| Back compatibility | Existing set receipts without `rollbackActions` can fall back to set preview; import receipts need `rollbackActions` because groups do not carry old values. |

## Receipt Shape

`plan-receipt.v1` keeps its existing fields and adds:

```json
{
  "rollbackActions": [
    {
      "elementId": 100,
      "param": "Fire Rating",
      "oldValue": "30min",
      "newValue": "60min",
      "source": "set"
    }
  ]
}
```

`oldValue` is the value to restore. `newValue` is the value expected to be
present before rollback. `source` is informational and matches the plan
operation (`set` or `import`).

## Rollback Flow

1. `rollback <artifact>` checks the input JSON.
2. If it is a `ModelSnapshot`, it runs the existing fix journal rollback.
3. If it is `plan-receipt.v1`, it builds rollback actions from
   `rollbackActions`, or from set `preview` for legacy set receipts.
4. It enforces `--max-changes`.
5. Dry-run prints reverse actions and exits without touching Revit.
6. Real apply requires `--yes`, validates the current document when possible,
   dry-runs each reverse write, skips conflicts, applies safe reverse writes,
   and returns non-zero only on API/validation errors.

## Testing

Focused tests cover:

- set receipt dry-run prints reverse actions without API calls;
- set receipt apply restores old values after document validation;
- import receipt apply uses per-parameter rollback actions;
- receipt rollback requires `--yes` for writes;
- receipt rollback honors `--max-changes`;
- receipt rollback skips current-value conflicts without applying;
- legacy malformed or unsupported receipt files fail before API calls.
