# RevitCli Q4 to v4 Terminal-First Blueprint

> Period: 2026 Q4 onward
> Status: strategic roadmap, not a locked implementation plan
> North star: an architect can open a terminal and use `revitcli` to
> finish repetitive Revit work safely, repeatably, and with clear
> rollback/audit trails. MCP is out of scope.

## 1. Reset: What RevitCli Is For

RevitCli exists for architects and BIM operators who already know what
they want to do, but do not want to spend another hour clicking through
Revit dialogs, schedules, sheets, parameter grids, and family lists.

The product is not an AI-to-Revit bridge. It is a terminal workbench for
boring but high-consequence Revit operations:

- inspect model state;
- batch-edit parameters safely;
- import/export schedule-like data;
- run office standards;
- publish deliverables;
- clean family assets;
- snapshot, diff, and audit what changed.

Every future feature should make one of those jobs faster, safer, or
more repeatable for a human architect at a command line.

## 2. Product Principles

- **Terminal-first**: the primary interface is `revitcli <verb> ...`.
  Dashboard and docs are supporting surfaces, not the center.
- **Architect-readable output**: table output must be useful without
  post-processing; JSON/CSV exist for scripts.
- **Preview before write**: every mutating workflow needs dry-run,
  action count, affected element identifiers, and clear confirmation.
- **Rollback by design**: writes that touch many elements must leave
  enough baseline/journal data to reverse safely.
- **Local-first**: no SaaS dependency, no remote model upload, no
  account system.
- **No MCP roadmap**: MCP PRs are closed. Existing MCP code, if kept for
  compatibility, is frozen and not a product direction.
- **No built-in LLM**: natural language can be a future wrapper outside
  the CLI, but it must not drive core design.

## 3. Current Pain Points

| Pain | Why it hurts | Current capability | Gap |
| --- | --- | --- | --- |
| Install/version drift | Add-in, CLI, and Revit year can silently mismatch. | `doctor`, smoke scripts | Installer still needs cleaner multi-year support. |
| Finding the right data | Architects do not remember exact category/parameter names. | `query`, aliases | Need inspect/discovery commands and better examples. |
| Safe bulk edits | `set` and `import` are powerful but scary on production models. | dry-run, fix/rollback | Need reusable plan files and stronger receipts. |
| Repeated pre-issue work | Checks, exports, snapshots, and schedules repeat every deadline. | profile/publish/history | Need human-readable terminal workflows. |
| Deliverable packaging | DWG/PDF/IFC output needs predictable naming and review. | `export`, `publish` | Need preflight, manifest, and bundle receipts. |
| Model review | Diffs and health trends exist but need quicker interpretation. | `snapshot`, `diff`, `history`, `score` | Need report presets for architects. |
| Family cleanup | Family bloat is common and tedious to inspect manually. | `family ls/purge/validate/export` | Need office-standard packs and safer purge reports. |
| Audit confidence | Journal exists and can be signed, but not yet easy to browse. | `journal sign/verify` | Need `journal show/stats` as pure CLI, no MCP resource. |

## 4. Milestone Overview

| Milestone | Theme | Outcome |
| --- | --- | --- |
| v2.2 | Terminal Trust Release | Installer, docs, roadmap, and smoke gates match the terminal-first promise. |
| v2.3 | Inspect & Discover | Architects can discover categories, parameters, schedules, sheets, and examples from the terminal. |
| v2.4 | Safe Batch Plans | Bulk edits become plan -> review -> apply -> rollback workflows. |
| v2.5 | Delivery Workflows | Pre-issue checks, exports, snapshots, and receipts become reusable terminal workflows. |
| v3.0 | Project Standards Workbench | Profiles, workflows, family rules, and reports become office-standard packs. |
| v3.x | Review & Knowledge Capture | Repeated terminal work becomes suggested scripts/workflows after human approval. |
| v4.0 | Architect Terminal BIM Workbench | RevitCli is a stable local command platform for recurring architectural BIM operations. |

## 5. v2.2 - Terminal Trust Release

Goal: make the current product coherent, releasable, and aligned with
the original terminal-first intent.

Scope:

- Generalize `scripts/install.ps1` install-directory overrides for
  Revit 2024 / 2025 / 2026.
- Run and record Revit 2026 live smoke; document 2024/2025 gaps until
  those machines are available.
- Keep MCP out of README and future roadmap. Keep or remove existing MCP
  code only after a compatibility decision; do not add new MCP features.
- Add release checklist: CLI tests, dashboard checks, smoke script,
  journal verify, changelog, version bump, tag.
- Add `docs/architect-terminal-vision.md`: one-page project intent and
  non-goals for contributors.

Exit gate:

- A new contributor can read README and understand this is a terminal
  tool for architects, not an AI/MCP project.
- `revitcli doctor`, `query`, `set --dry-run`, `publish --dry-run`,
  `history capture`, and `journal verify` all have copy-paste examples.

## 6. v2.3 - Inspect & Discover

Goal: reduce the friction between "I know what I want" and "I know the
exact RevitCli command."

Candidate commands:

- `revitcli inspect categories`: list category aliases, counts, and
  whether they are writable.
- `revitcli inspect params <category>`: show common parameters, sample
  values, read-only/writeable status, and duplicate names.
- `revitcli inspect sheets`: summarize sheets by number, name, title
  block, revision, and printable/exportable state.
- `revitcli examples <command>`: local examples for common tasks.

Why this matters:

- Architects think in doors, rooms, sheets, schedules, types, marks,
  levels, and issue sets. They should not have to guess API names.

Exit gate:

- Given an unfamiliar model, a user can find a writable door parameter
  and build a safe dry-run `set` command without opening docs.

## 7. v2.4 - Safe Batch Plans

Goal: make every large write feel reviewable before it touches the model.

Scope:

- Add explicit plan files for mutating commands:
  `set --plan-output`, `import --plan-output`, `fix --plan-output`.
- Add `revitcli plan show <file>` and `revitcli plan apply <file> --yes`.
- Standardize receipts: command, model path, document version, affected
  ids, old/new values, timestamp, operator, baseline path, journal path.
- Add thresholds: `--max-changes`, profile defaults, and second
  confirmation for high-impact writes.

Innovation:

- Treat batch Revit writes like database migrations: generated plan,
  explicit review, apply, receipt, rollback.

Exit gate:

- A 200-door parameter update can be dry-run, reviewed, applied, and
  rolled back from terminal artifacts alone.

## 8. v2.5 - Delivery Workflows

Goal: make deadline-day repetition boring.

Scope:

- Add `.revitcli/workflows/*.yml` for human terminal workflows.
- Add `workflow validate`, `workflow simulate`, and `workflow run`.
- First workflow pack: `pre-issue`, `weekly-health`, `export-package`,
  `family-cleanup`.
- Workflows may call only existing CLI commands; no hidden runtime.
- Every workflow step must declare whether it is read-only, dry-run, or
  mutating.

Example:

```yaml
name: pre-issue
steps:
  - run: revitcli profile simulate issue
  - run: revitcli check issue --output table
  - run: revitcli publish issue --dry-run
  - run: revitcli history capture --source pre-issue
```

Exit gate:

- An architect can run one terminal command before issue day and get a
  readable checklist result plus artifacts for review.

## 9. v3.0 - Project Standards Workbench

Goal: turn one-off profiles and workflows into office standards.

Scope:

- Profile/workflow packs with version metadata and compatibility notes.
- `revitcli standards validate`: verify a project has required profiles,
  workflows, output paths, family rules, and schedule templates.
- `revitcli standards install <path-or-git-url>`: install approved local
  office standards.
- Family validation rules promoted into reusable rule packs.
- Dashboard remains optional for managers; terminal remains primary.

Exit gate:

- A new project can be bootstrapped with office standards and checked
  from terminal in under 10 minutes.

## 10. v3.x - Review & Knowledge Capture

Goal: help architects understand and reuse their own repeated work
without introducing AI or MCP as a dependency.

Candidate tracks:

- `journal show/stats`: browse recent writes, top edited categories,
  operator, and affected elements.
- `diff --review`: terminal summary of suspicious changes, grouped by
  category/sheet/schedule.
- `workflow suggest`: detect repeated command sequences from journal and
  propose a workflow YAML, never write it without approval.
- `report weekly`: generate a terminal/Markdown summary from history,
  score, diff, and journal.

Constraint:

- Suggestions are drafts. The architect approves before new standards,
  workflows, or fix recipes are written.

## 11. v4.0 - Architect Terminal BIM Workbench

v4.0 is not an AI platform. It is the point where RevitCli feels like a
complete local workbench for recurring architectural BIM operations.

Target outcome:

- A stable command vocabulary for inspect, query, plan, apply, publish,
  report, standards, family, history, and journal.
- Office-standard packs are portable between projects.
- Bulk writes are plan-based and reversible.
- Deliverables have manifests and receipts.
- Model health is trackable from terminal and optionally visible in the
  dashboard.
- Extension points exist for custom rules/workflows, but they serve the
  terminal product, not an MCP ecosystem.

Non-goals:

- No MCP expansion.
- No built-in LLM, prompt layer, or natural-language command system.
- No SaaS / cloud sync / ACC clone.
- No fully autonomous model mutation.
- No Family Editor geometry editing until a real user need justifies the
  complexity.

## 12. Forward-Looking, Innovative, Feasible

Forward-looking:

- RevitCli should become local project memory: what was checked, changed,
  exported, reverted, and approved.
- Office standards should move from tribal knowledge into versioned
  terminal packs that can be installed, validated, and audited.
- Repetitive deadline work should become named workflows that architects
  can run, inspect, and improve.

Innovative:

- Treat bulk Revit writes like database migrations: plan, review, apply,
  receipt, rollback.
- Use signed journals and history to make model operations explainable
  after the fact.
- Let command history suggest workflow drafts, while keeping human
  approval as the control point.

Feasible:

- The plan builds on existing `query`, `set`, `import`, `fix`,
  `rollback`, `publish`, `history`, `family`, and `journal` commands.
- No milestone depends on cloud services, live collaboration, MCP, or a
  built-in LLM.
- Every risky feature has a small acceptance gate: dry-run output, CLI
  tests, receipts, rollback, and real Revit smoke where needed.

## 13. Development Method

Every new feature should start from a concrete architect task:

1. Write the manual Revit workflow in plain language.
2. Translate it into the desired terminal command.
3. Define the dry-run output before implementation.
4. Define rollback/audit artifacts for any write.
5. Add CLI tests first; add add-in tests when Revit API behavior changes.
6. Add one copy-paste README or docs example.
7. Run real Revit smoke for any feature that mutates or exports.

Avoid features that cannot be explained as:

> "An architect can run this command to avoid doing this repetitive Revit
> task by hand."

## 14. Self-Audit: Are We Violating the Original Intent?

| Area | Status | Judgment |
| --- | --- | --- |
| Core CLI commands | Strong | `query`, `set`, `import`, `publish`, `history`, `family`, and `journal` match the terminal-first intent. |
| Safety model | Strong | Dry-run, rollback, snapshot, and signed journal support the right level of trust. |
| README positioning | Corrected | Command list and roadmap now focus on terminal workflows; keep future docs aligned. |
| MCP code | Drift | MCP was overbuilt relative to the original product. Keep PRs closed and decide whether to hide, deprecate, or remove existing code in v2.2. |
| Agent-native docs | Drift | Useful ideas exist, but the framing pulls the project away from architects using a terminal. Treat as archived brainstorming. |
| Dashboard | Acceptable if optional | Good for history visualization, but should not define the product. |
| v4 language | Corrected | The roadmap now describes a terminal BIM workbench for architects, not an integration protocol product. |

Conclusion: the implemented core still serves the original intent, but
recent roadmap language drifted toward agent/MCP. The correction is to
freeze MCP, rewrite future planning around architect pain points, and use
terminal-first acceptance gates for every feature.

## 15. Immediate Next Actions

1. Keep MCP PRs closed; do not open new MCP work.
2. Decide whether existing `revitcli mcp serve` should be hidden,
   deprecated, or removed before the next release.
3. Continue v2.2 with installer hardening, live smoke evidence, and release checklist cleanup.
4. Write `inspect categories` / `inspect params` spec for v2.3.
5. Collect three real repetitive architect workflows and turn them into
   v2.5 workflow acceptance examples.
