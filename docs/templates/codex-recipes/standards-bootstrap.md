# Standards Bootstrap

## Prompt

Check whether this project satisfies the office standards. Preview installation
first if the standards pack is not installed.

## Command Path

```powershell
revitcli standards install profiles/office-standard --dry-run --output markdown
revitcli standards install profiles/office-standard
revitcli standards validate --output markdown
revitcli standards validate --output json
revitcli workflow validate --output markdown
revitcli family validate --rules-from .revitcli/standards.yml
```

## Handoff

Report missing profiles, workflows, output paths, schedule templates, sheet
maps, numbering rules, and family rules. For existing projects, ask before
running `standards install ... --force`.
