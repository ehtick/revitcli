# Release Preflight

Use when the maintainer asks for release readiness checks without running live
Revit smoke. This recipe covers local release files and CI guardrails only.

## Prompt Shape

```text
帮我做发版前预检,不要跑真实 Revit smoke。
```

## Command Path

```powershell
revitcli release verify --tag v2.2.0
revitcli release verify --tag v2.2.0 --output json
revitcli release verify --tag v2.2.0 --output markdown
```

## Review

- Confirm `schemaVersion` is `release-verify.v1`.
- `errorCount` must be `0`.
- Check `version` and `tag` match.
- Confirm the Ubuntu CI checks say it only runs CLI/Shared portable tests.
- Use the Markdown output for a maintainer-facing preflight handoff.
- Treat real Revit smoke as a separate checklist item.

## Follow-Up Gate

```powershell
revitcli doctor --check-version 2026
.\scripts\smoke-revit.ps1 -Version 2026 -ElementId 12345 -Filter "id = 12345"
revitcli journal verify
```

Do not claim live Revit release readiness from `release verify` alone.
