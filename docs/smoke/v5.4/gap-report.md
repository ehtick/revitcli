# RevitCli v5.4 Standards Runtime Pack Gap Report

v5.4 Standards Runtime Pack is proceeding as a local, offline hardening slice.
The canonical `office-standard` pack lives at `profiles/office-standard` and is
intended to prove standards-as-code bootstrap behavior before any broader team
deployment claim.

## Current Status

| Scope | Status | Evidence |
| --- | --- | --- |
| Canonical office-standard pack | portable verified | `profiles/office-standard/.revitcli/standards.yml` declares profile, workflow, output path, schedule template, sheet map, numbering rules, and family rules. |
| Standards install dry-run | portable verified | `standards install profiles/office-standard --dry-run` previews local files without writing. |
| Standards install apply | portable verified | `standards install profiles/office-standard` copies governed files into a project copy and runs validation. |
| Standards validate | portable verified | `standards validate --manifest .revitcli/standards.yml --dir profiles/office-standard` is offline and deterministic. |
| Release/workbench gates | portable verified | `release verify --strict` and `workbench verify --contract workbench-contract.v2` require the v5.4 standards runtime disclosures and run a temp-project `standards install` dry-run/apply smoke. |
| Bootstrap time SLA | not benchmarked | The `<10 minute` new-project bootstrap target is operational and requires a timed pilot. |
| Office pilot | not live verified | Requires a BIM manager or standards owner to install the pack into a disposable project copy and record friction. |

## Gate

Before v5.4 can be called production-pilot ready, record:

- A fresh project bootstrap using `profiles/office-standard`.
- `standards install --dry-run`, approved `standards install`, and
  `standards validate --output markdown` evidence.
- Workflow validation for the installed `pre-issue` workflow.
- Confirmation that sheet maps and numbering rules remain local project files,
  not hidden SaaS, MCP, dashboard, or embedded LLM runtime state.
- A timed bootstrap note for the `<10 minute` target.

Example portable smoke:

```powershell
mkdir .revitcli-v54-smoke
revitcli standards install profiles/office-standard --dir .revitcli-v54-smoke --dry-run --output markdown
revitcli standards install profiles/office-standard --dir .revitcli-v54-smoke --output markdown
revitcli standards validate --dir .revitcli-v54-smoke --output markdown
revitcli workflow validate --dir .revitcli-v54-smoke --output markdown
```

v5.4 does not introduce cloud sync, SaaS dashboards, MCP orchestration, or a
built-in language model. The pack is a local file contract reviewed from the
terminal.
