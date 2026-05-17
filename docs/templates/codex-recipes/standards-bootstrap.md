# Standards Bootstrap

## Prompt

Check whether this project satisfies the office standards. Preview installation
first if the standards pack is not installed.

## Command Path

```powershell
revitcli standards install ../office-standards --dry-run --output markdown
revitcli standards validate --output markdown
revitcli standards validate --output json
revitcli family validate --rules-from .revitcli/standards.yml
revitcli workflow validate --output markdown
```

## Handoff

Report missing profiles, workflows, output paths, schedule templates, and family
rules. Ask before running `standards install ... --force`.
