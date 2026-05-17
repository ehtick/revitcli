# Family Cleanup Review

Use when the architect asks to clean unused families, reduce model bloat, or
review purge candidates. Start with read-only discovery and write a purge
report before any destructive cleanup.

## Prompt Shape

```text
帮我清理未使用的族,先写报告,不要直接删除。
```

## Command Path

```powershell
revitcli workflow init family-cleanup
revitcli workflow simulate .revitcli/workflows/family-cleanup.yml
revitcli family ls --unused
revitcli family validate --rules-from .revitcli/standards.yml
revitcli family purge --dry-run --report .revitcli/reports/family-purge.json
```

## Review Before Approval

- Report path: `.revitcli/reports/family-purge.json`
- Schema: `family-purge-report.v1`
- Check `summary.candidateCount`, `keptByPattern`, `excludedPlaced`, and
  `excludedInPlace`.
- Confirm the candidate list with the architect before any apply command.

## Apply Only After Approval

```powershell
revitcli family purge --apply --yes --report .revitcli/reports/family-purge-applied.json
revitcli history capture --source family-cleanup
revitcli journal review --output markdown
```

Never add `--apply --yes` unless the architect has approved the dry-run report.
