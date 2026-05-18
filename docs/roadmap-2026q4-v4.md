# RevitCli Q4 to v4 Terminal-First Blueprint

> Period: 2026 Q4 onward
> Status: strategic roadmap, not a locked implementation plan
> North star: an architect can open a terminal, ask Codex CLI for help,
> and have it call `revitcli` to finish repetitive Revit work safely,
> repeatably, and with clear rollback/audit trails. MCP is out of scope.

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
more repeatable for a human architect at a command line, whether the
architect types `revitcli` directly or asks Codex CLI to operate it.

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
- **Codex CLI-friendly**: commands, examples, outputs, and exit codes
  should be easy for Codex CLI to call and explain.
- **No built-in LLM**: natural language belongs in Codex CLI or another
  external terminal operator; RevitCli stays deterministic.

## 3. Codex CLI Operating Model

Codex CLI is the conversational layer architects can use. RevitCli is
the local BIM command layer it calls.

This keeps the product practical:

- architects can ask for outcomes in normal language;
- Codex CLI translates the task into visible shell commands;
- RevitCli handles Revit API work, dry-runs, writes, exports, receipts,
  rollback, and journal verification;
- risky commands still require preview and explicit approval;
- no MCP server or built-in prompt runtime is required.

Reference guide:
[docs/codex-cli-architect-workflows.md](codex-cli-architect-workflows.md).

## 4. Current Pain Points

| Pain | Why it hurts | Current capability | Gap |
| --- | --- | --- | --- |
| Install/version drift | Add-in, CLI, and Revit year can silently mismatch. | `doctor`, smoke scripts | Installer still needs cleaner multi-year support. |
| Command recall | Architects know the task, not the exact syntax. | README examples | Need Codex CLI task recipes and richer command discovery. |
| Finding the right data | Architects do not remember exact category/parameter names. | `query`, aliases | Need inspect/discovery commands and better examples. |
| Safe bulk edits | `set` and `import` are powerful but scary on production models. | dry-run, fix/rollback | Need reusable plan files and stronger receipts. |
| Repeated pre-issue work | Checks, exports, snapshots, and schedules repeat every deadline. | profile/publish/history | Need human-readable terminal workflows. |
| Deliverable packaging | DWG/PDF/IFC output needs predictable naming and review. | `export`, `publish` | Need preflight, manifest, and bundle receipts. |
| Model review | Diffs and health trends exist but need quicker interpretation. | `snapshot`, `diff`, `history`, `score` | Need report presets for architects. |
| Family cleanup | Family bloat is common and tedious to inspect manually. | `family ls/purge/validate/export`, standards-driven validation, purge reports | Need more real-project standards packs and cleanup presets after field use. |
| Audit confidence | Journal exists, can be browsed, reviewed, and signed/verified. | `journal show/stats/review/sign/verify` | Next need more real-project review presets after user feedback. |

## 5. Milestone Overview

| Milestone | Theme | Outcome |
| --- | --- | --- |
| v2.2 | Terminal Trust Release | Installer, docs, roadmap, and smoke gates match the terminal-first promise. |
| v2.3 | Inspect & Discover | Architects and Codex CLI can discover categories, parameters, schedules, sheets, and examples from the terminal. |
| v2.4 | Safe Batch Plans | Complete: bulk edits become plan -> review -> apply -> receipt -> rollback workflows. |
| v2.5 | Delivery Workflows | Complete: pre-issue checks, exports, snapshots, and receipts become reusable terminal workflows. |
| v3.0 | Project Standards Workbench | Complete: profiles, workflows, family rules, and reports become office-standard packs. |
| v3.x | Review & Knowledge Capture | Complete: repeated terminal work becomes suggested scripts/workflows after human approval. |
| v4.0 | Architect Terminal BIM Workbench | RevitCli is a stable local command platform that Codex CLI can safely operate for recurring architectural BIM tasks. |

## 6. v2.2 - Terminal Trust Release

Goal: make the current product coherent, releasable, and aligned with
the original terminal-first intent.

Scope:

- Generalize `scripts/install.ps1` install-directory overrides for
  Revit 2024 / 2025 / 2026.
- Run and record Revit 2026 live smoke; document 2024/2025 gaps until
  those machines are available.
- Keep MCP out of README and future roadmap. Decision on 2026-04-30:
  keep `revitcli mcp serve` only as a hidden, deprecated compatibility
  command; do not expose it in help, completions, or new docs, and do not add
  new MCP features.
- Add release checklist: CLI tests, dashboard checks, smoke script,
  journal verify, changelog, version bump, tag.
- Add `revitcli release verify`: local release preflight for
  `RevitCliVersion`, changelog/README/checklist presence, Ubuntu
  CLI/Shared-only CI guardrails, installer markers, tag consistency, and
  release packaging workflow markers. Table/JSON/Markdown output supports
  terminal review, CI guardrails, and maintainer handoff notes. Ubuntu CI now
  runs the same guardrail after the portable build. It does not execute live
  Revit smoke.
- Add `docs/architect-terminal-vision.md`: one-page project intent and
  non-goals for contributors.
- Add Codex CLI architect workflow guide with safe command paths for
  checking, publishing, schedule export, and publish failure diagnosis.

Exit gate:

- A new contributor can read README and understand this is a terminal
  tool for architects that Codex CLI can operate, not an MCP project.
- `revitcli doctor`, `query`, `set --dry-run`, `publish --dry-run`,
  `history capture`, and `journal show/stats/verify` all have copy-paste
  examples.

## 7. v2.3 - Inspect & Discover

Goal: reduce the friction between "I know what I want" and "I or Codex
CLI know the exact RevitCli command."

Candidate commands:

- `revitcli inspect categories`: list category aliases, counts, and
  next discovery commands. First CLI-only slice shipped via the existing
  query endpoint.
- `revitcli inspect params <category>`: show common parameters, sample
  values, coverage, and dry-run probe command examples. First CLI-only
  slice shipped via the existing query endpoint. Second slice adds
  query-side `parameterMetadata` so the command can surface writable
  status, read-only fields, storage types, and safer `set --dry-run`
  probes. Delivery-day filters now include `--name`, `--writable-only`,
  and `--missing-only` for finding candidate parameter fixes before
  generating a plan. Writable parameter rows now include sample element IDs
  and element-scoped dry-run probes so the first check can target one
  representative element before widening to a plan.
- `revitcli inspect schedules`: list schedules, fields, categories, and
  export readiness. First slice shipped as a CLI-side wrapper around
  existing schedule discovery. Delivery-day filters now include category/name
  matching, `--ready-only`, `--empty-only`, `--issues-only`, readiness
  issues, and CSV/JSON export commands. `schedule list/export --output
  markdown` adds handoff tables for schedule review while preserving CSV/JSON
  for downstream scripts.
- `revitcli inspect sheets`: CLI-only sheet discovery for Codex CLI and
  terminal users; summarize sheets by number, name, key title-block
  parameters, review issues, filters, and export-candidate state before
  publish/export planning. Inspect discovery commands now support Markdown
  handoff output for categories, parameters, schedules, and sheets while
  preserving JSON for scripts.
- `revitcli sheets verify`: read-only sheet-frame verification against an
  optional `.revitcli/sheets/index.yml`, covering duplicate numbers,
  numbering gaps/ranges, required sheets, and minimum placed-view counts.
  First CLI-only slice shipped through the existing snapshot sheet data;
  deeper view-type/orphan-view checks remain deferred until the snapshot
  surface carries view inventory.
- `revitcli examples <topic>`: local examples for common tasks. First slice
  shipped for inspect, sheets, schedule, set, import, publish, doctor, and
  journal workflows.

Why this matters:

- Architects think in doors, rooms, sheets, schedules, types, marks,
  levels, and issue sets. They should not have to guess API names.
- Codex CLI needs deterministic discovery commands before it can turn
  "导门表" or "查出图问题" into safe command sequences.

Exit gate:

- Given an unfamiliar model, a user can find a writable door parameter
  and build a safe dry-run `set` command without opening docs.
- Codex CLI can answer "what can I export or check in this model?" using
  only read-only RevitCli commands, including sheet/export-candidate
  discovery through `revitcli inspect sheets --ready-only`, schedule
  handoff triage through `revitcli inspect schedules --issues-only --output
  markdown`, and sheet issue triage through
  `revitcli inspect sheets --issues-only --output markdown` and
  `revitcli sheets verify --issues-only`.

## 8. v2.4 - Safe Batch Plans

Goal: make every large write feel reviewable before it touches the model.

Scope:

- Add explicit plan files for mutating commands:
  `set --plan-output`, `import --plan-output`, `fix --plan-output`.
  First slices shipped for `set --plan-output`, `import --plan-output`,
  and `fix --plan-output` with frozen element IDs or generated fix actions.
- Add `revitcli plan show <file>` and `revitcli plan apply <file> --yes`.
  First slices shipped for set/import/fix plans, including `--dry-run`,
  `--max-changes`, sidecar receipt files, and fix baseline/journal capture
  for rollback.
- Add JSON summaries for Codex CLI approval prompts. First slice shipped
  through `plan show FILE --output json` with a stable `plan-summary.v1`
  envelope for set/import/fix plans. Markdown approval reviews are also
  available through `plan show FILE --output markdown` for architect
  handoff notes.
- Standardize receipts: command, model path, document version, affected
  ids, old/new values, timestamp, operator, baseline path, journal path.
  First slice shipped through `plan-receipt.v1` sidecars for
  `plan apply` set/import/fix, including command metadata, model context
  when available, affected IDs, rollback actions for set/import receipts,
  and fix rollback pointers.
- Add thresholds: `--max-changes`, profile defaults, and second
  confirmation for high-impact writes. First slice shipped through
  `defaults.planMaxChanges`, `defaults.highImpactChanges`,
  `--high-impact-threshold`, and `--confirm-high-impact` on `plan apply`.

Innovation:

- Treat batch Revit writes like database migrations: generated plan,
  explicit review, apply, receipt, rollback.

Exit gate:

- A 200-door parameter update can be dry-run, reviewed, applied, and
  rolled back from terminal artifacts alone.
- Codex CLI can summarize the plan in Chinese before the architect
  approves the apply step.

Completion status: complete as of 2026-05-18. The terminal path now covers
`set/import/fix --plan-output`, `plan show` table/JSON/Markdown review,
`plan apply` dry-run/apply with safety thresholds and receipts, set/import
receipt rollback actions, fix baseline rollback, and local examples/recipes
that show Chinese summary handoff plus receipt rollback.

## 9. v2.5 - Delivery Workflows

Goal: make deadline-day repetition boring.

Scope:

- Add `.revitcli/workflows/*.yml` for human and Codex CLI-driven
  terminal workflows.
- Add `workflow validate`, `workflow simulate`, and `workflow run`. First
  slices shipped for validation/simulation and gated execution with
  `--dry-run`, `--yes`, no shell operators, per-step risk modes, and
  table/JSON/Markdown review output.
- First workflow pack: `pre-issue`, `weekly-health`, `export-package`,
  `family-cleanup`. First pack shipped as built-in templates installable with
  `workflow init <template>` or `workflow init all`.
- Architect prompt acceptance examples shipped through
  `workflow examples [template]`, covering pre-issue checks, export package
  handoff, weekly health review, and family cleanup.
- Workflows may call only existing CLI commands; no hidden runtime.
- Workflow simulation should return clear JSON/table/Markdown summaries for
  Codex CLI to explain.
- Every workflow step must declare whether it is read-only, dry-run, or
  mutating.
- Real workflow runs write `workflow-run-receipt.v1` receipts with command
  metadata, operator/machine, step statuses, exit codes, and success/failure
  status under `.revitcli/workflows/receipts/`. Local receipt review is
  available through `workflow receipts`, including JSON/Markdown output and
  `--failed-only` triage.

Example:

```yaml
version: 1
name: pre-issue
steps:
  - run: revitcli profile simulate issue
    mode: read-only
  - run: revitcli check issue --output table
    mode: read-only
  - run: revitcli publish issue --dry-run
    mode: dry-run
  - run: revitcli history capture --source pre-issue
    mode: mutating
    requiresApproval: true
```

Exit gate:

- An architect can run one terminal command before issue day and get a
  readable checklist result plus artifacts for review.
- An architect can ask Codex CLI "帮我做出图前检查" and see the exact
  workflow steps before anything mutates or exports.

Completion status: complete as of 2026-05-18. The workflow surface now covers
template install, validation, simulation, gated run, receipt writing/review,
acceptance examples, and journal-based suggestions. Validation rejects shell
operators, missing risk modes, unapproved mutating steps during real runs, and
unknown RevitCli commands/subcommands so workflows call only visible CLI
surfaces.

## 10. v3.0 - Project Standards Workbench

Goal: turn one-off profiles and workflows into office standards.

Scope:

- Profile/workflow packs with version metadata and compatibility notes.
  First slice shipped through `.revitcli/standards.yml` `packVersion` and
  `compatibility` metadata, including minimum RevitCli version checks,
  supported Revit years, and notes in table/JSON validation output.
- `revitcli standards validate`: verify a project has required profiles,
  workflows, output paths, family rules, and schedule templates. First
  CLI-only local manifest slice shipped for `.revitcli/standards.yml` with
  table/JSON/Markdown output.
- `revitcli standards install <path-or-git-url>`: install approved local
  office standards. First local-pack slice shipped with dry-run, force,
  table/JSON/Markdown output, governed-file copy, workflow copy, and
  output-directory bootstrap. Installer now copies every relative
  `required.profiles` entry from the pack, so office standards can bootstrap
  more than the root `.revitcli.yml` profile.
- Family validation rules promoted into reusable rule packs. First CLI
  slice shipped through `family validate --rules-from .revitcli/standards.yml`
  so standards manifests can drive the enabled built-in family rule set.
- Family purge review reports shipped through `family purge --report FILE`
  with a stable `family-purge-report.v1` JSON envelope for dry-run and
  approved cleanup evidence.
- Dashboard remains optional for managers; terminal remains primary.
- Codex CLI task recipes can reference standards packs instead of
  inventing project rules during a session.

Exit gate:

- A new project can be bootstrapped with office standards and checked
  from terminal in under 10 minutes:
  `standards install ../office-standards --dry-run --output markdown`;
  `standards install ../office-standards`;
  `standards validate --output markdown`;
  `workflow validate --output markdown`; and
  `family validate --rules-from .revitcli/standards.yml` when Revit is open.

Completion status: complete as of 2026-05-18. Standards packs now cover
manifest metadata/compatibility, install/validate table/JSON/Markdown output,
all manifest-declared profile files, workflows, output directories, family
rule packs, purge reports, and terminal-first bootstrap recipes.

## 11. v3.x - Review & Knowledge Capture

Goal: help architects and Codex CLI understand and reuse repeated work
without putting an LLM inside RevitCli or introducing MCP as a dependency.

Candidate tracks:

- `journal show/stats`: browse recent writes, top edited categories,
  operator, and affected elements. First richer stats slice shipped with
  action/category/user/operator counts, affected totals per group, and
  distinct affected element IDs. Filtered journal review shipped for
  `journal show --action/--category/--operator/--user`. Review presets now
  include `journal review` with table/JSON/Markdown output, risk buckets,
  top actions/categories/operators/users, affected ID samples, and highlighted
  mutating or high-impact entries.
- `diff --review`: terminal summary of suspicious changes, grouped by
  category/sheet/schedule. First deterministic slice shipped for
  anomaly/notable/routine triage over snapshot diffs and history diffs.
- `workflow suggest`: detect repeated command sequences from journal and
  propose a workflow YAML, never write it without approval. First local
  slice shipped for explicit journal `command` / `commandLine` / `run`
  fields with table/JSON/YAML output and no file-writing option.
- `report weekly`: generate a terminal/Markdown summary from history,
  score, diff, and journal. First local slice shipped for table/json/markdown
  weekly reports from `.revitcli/history` and `.revitcli/journal.jsonl`.
- `report knowledge`: consolidate reusable local project memory from history,
  journal command repetition, workflow receipts, delivery manifests/receipts,
  standards validation, and saved weekly reports. First terminal-only slice
  shipped with table/JSON/Markdown output, deterministic reuse hints, and
  review-only workflow YAML drafts from repeated journal command sequences; it
  does not write workflows, standards, or any LLM/MCP runtime state.
- `codex recipes`: documented prompt-to-command examples, stored as
  docs/templates rather than executable hidden logic. First template pack
  shipped under `docs/templates/codex-recipes/` with an `examples recipes`
  pointer and no runtime command parser.

Constraint:

- Suggestions are drafts. The architect approves before new standards,
  workflows, or fix recipes are written.

Completion status: complete as of 2026-05-18. The v3.x terminal path now covers
journal browsing/stats/review, deterministic diff review, journal-derived
workflow YAML suggestions, weekly reports, knowledge reports that consolidate
local artifacts and expose review-only workflow drafts, and documented Codex
recipe templates. RevitCli remains a local CLI artifact reader and suggestion
surface; it does not embed an LLM, MCP workflow, dashboard dependency, or hidden
write path for suggested workflows.

## 12. v4.0 - Architect Terminal BIM Workbench

v4.0 is not an AI platform. It is the point where RevitCli feels like a
complete local workbench that Codex CLI can safely operate for recurring
architectural BIM work.

Target outcome:

- A stable command vocabulary for inspect, query, plan, apply, publish,
  report, standards, family, history, and journal.
- A stable "Codex CLI can call this" contract: predictable exit codes,
  readable tables, compact JSON, dry-run everywhere, and receipts for
  writes/exports. First v4 contract slice shipped through
  `status --output json`, `doctor --output json`, `check --output json`,
  `export --dry-run --output json`, `publish --dry-run --output json`,
  `schedule list --output json`, and journal `--output json` for
  machine-readable setup checks, connection status, check gate results,
  export/publish plans, schedule discovery, journal review, and
  connection-failure diagnostics. Schedule list/export Markdown output and
  export-side output validation extend the same predictable CLI contract to
  handoff tables. First delivery receipt slices shipped
  through export receipts under
  `<outputDir>/.revitcli/receipts/export-*.json` and standardized publish
  receipts under `.revitcli/receipts/<pipeline>-*.json`; successful real
  exports and publishes now also append `delivery-manifest.v1` JSONL entries
  under `.revitcli/deliveries/manifest.jsonl`. Local manifest review is
  available through `deliverables list`, `deliverables stats`, and
  `deliverables verify`, including receipt existence, receipt-schema checks,
  and Markdown handoff output. Delivery handoff packaging now has
  `deliverables bundle` dry-runs, Markdown previews, zip output, and
  `delivery-bundle-receipt.v1` sidecars that trace packaged receipts and
  output files. Safe-plan receipts now use `plan-receipt.v1`
  sidecars with
  command metadata, timestamp/operator/machine, model context when available,
  affected element IDs, set/import rollback actions, and fix rollback
  baseline/journal pointers. `rollback` accepts both fix baselines and
  plan receipt sidecars with current-value conflict checks. Safe-plan apply
  also reads profile safety defaults for max changes and high-impact
  confirmation gates.
- Office-standard packs are portable between projects and expose
  pack-version / compatibility metadata during validation.
- Bulk writes are plan-based and reversible.
- Deliverables have manifests, receipts, and bundle receipts.
- Delivery workflows have run receipts and a local `workflow receipts` review
  surface for failed-run triage and handoff notes.
- Model health is trackable from terminal and optionally visible in the
  dashboard.
- Extension points exist for custom rules/workflows, but they serve the
  terminal product, not an MCP ecosystem.

Non-goals:

- No MCP expansion.
- No built-in LLM, prompt layer, or natural-language command system
  inside RevitCli.
- No SaaS / cloud sync / ACC clone.
- No fully autonomous model mutation.
- No Family Editor geometry editing until a real user need justifies the
  complexity.

## 13. Forward-Looking, Innovative, Feasible

Forward-looking:

- RevitCli should become local project memory: what was checked, changed,
  exported, reverted, and approved.
- Codex CLI can become the architect-facing conversation layer without
  changing RevitCli into an AI product.
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
- Design command outputs as both human-readable terminal UX and
  machine-readable context for Codex CLI.

Feasible:

- The plan builds on existing `query`, `set`, `import`, `fix`,
  `rollback`, `publish`, `history`, `family`, and `journal` commands.
- No milestone depends on cloud services, live collaboration, MCP, or an
  LLM embedded inside RevitCli.
- Every risky feature has a small acceptance gate: dry-run output, CLI
  tests, receipts, rollback, and real Revit smoke where needed.

## 14. Development Method

Every new feature should start from a concrete architect task:

1. Write the manual Revit workflow in plain language.
2. Translate it into the desired terminal command.
3. Write a Codex CLI prompt example for the same task.
4. Define the dry-run output before implementation.
5. Define rollback/audit artifacts for any write.
6. Add CLI tests first; add add-in tests when Revit API behavior changes.
7. Add one copy-paste README or docs example.
8. Run real Revit smoke for any feature that mutates or exports.

Avoid features that cannot be explained as:

> "An architect can run this command to avoid doing this repetitive Revit
> task by hand."

## 15. Self-Audit: Are We Violating the Original Intent?

| Area | Status | Judgment |
| --- | --- | --- |
| Core CLI commands | Strong | `query`, `set`, `import`, `publish`, `history`, `family`, and `journal` match the terminal-first intent. |
| Codex CLI orchestration | Strong if external | This matches the intent when Codex CLI runs visible RevitCli commands and respects dry-run/approval gates. |
| Safety model | Strong | Dry-run, rollback, snapshot, and signed journal support the right level of trust. |
| README positioning | Corrected | Command list and roadmap now focus on terminal workflows; keep future docs aligned. |
| MCP code | Drift | MCP was overbuilt relative to the original product. Keep PRs closed and decide whether to hide, deprecate, or remove existing code in v2.2. |
| Agent-native docs | Mixed | Codex CLI as a terminal operator is useful; protocol/server/product framing pulls away from the original intent. |
| Dashboard | Acceptable if optional | Good for history visualization, but should not define the product. |
| v4 language | Corrected | The roadmap now describes a terminal BIM workbench for architects, not an integration protocol product. |

Conclusion: the implemented core still serves the original intent.
Codex CLI strengthens the intent when it operates RevitCli transparently:
visible commands, dry-run first, explicit approval for writes, and local
audit artifacts. The risk is not Codex CLI itself; the risk is turning
RevitCli into a hidden AI/MCP platform instead of a dependable CLI.

## 16. Immediate Next Actions

1. Keep MCP PRs closed; do not open new MCP work.
2. Keep existing `revitcli mcp serve` hidden and deprecated; remove it only
   with a future breaking-change notice.
3. Keep v2.3 release packaging focused on the version/tag flow, CI signal,
   and Windows/Revit smoke evidence. Current CLI-only CI runs `release verify`
   and the portable tests before tag publication; live smoke evidence remains
   a Windows/Revit release gate.
4. Extend inspect/discover and workflow review beyond the shipped sheets,
   schedules, writable parameter metadata, delivery receipts, and standards
   checks into deeper workflows that directly support
   delivery-day tasks.
5. Collect more real repetitive architect prompts after field use and add
   them to workflow acceptance examples when they map to visible CLI paths.
