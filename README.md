# RevitCli

Terminal-first Autodesk Revit automation for architects. RevitCli lets a
human operator or Codex CLI inspect models, find drawing blockers, export
sheets and schedules, diagnose publish failures, snapshot/diff model state,
and make reviewed parameter changes without clicking through repetitive
Revit UI.

> **Status: Unreleased v4.0 - Architect Terminal BIM Workbench**
> Windows/Revit-first BIMOps runner with source-level support for Revit
> 2024/2025/2026; the latest tagged release ZIP packages the Revit 2026 add-in.
> Current focus:
> reliable inspect/discover commands, standards checking, deliverable
> publishing, schedule export, CSV writeback, safe dry-run plans,
> `fix`/`rollback`, model snapshots, signed journals, and release preflight.

```bash
revitcli status --output json                                # connection check
revitcli inspect sheets --issues-only --output markdown      # find sheet blockers
revitcli sheets verify --output json --issues-only           # verify sheet numbering/index expectations
revitcli inspect params doors                                # find writable parameters
revitcli inspect schedules --issues-only --output markdown   # find schedule export blockers
revitcli inspect plans --output markdown                     # review saved plans before apply
revitcli query walls --filter "height > 3000" --output json  # query
revitcli schedule list --output markdown                     # review available schedules
revitcli schedule export --name "Door Schedule" --output csv # export a schedule
revitcli schedule create --category Doors --fields "Mark,Level" --name "Door Review" --dry-run --output json # preview ViewSchedule write
revitcli export --format dwg --sheets "A1*" --output-dir .   # batch export
revitcli set doors --param "Fire Rating" --value "60min"     # bulk set
revitcli snapshot --output snap.json                         # capture model state
revitcli diff snap-mon.json snap-fri.json --output markdown  # what changed
revitcli diff snap-mon.json snap-fri.json --review           # flag suspicious changes
revitcli workflow init pre-issue                             # create a workflow template
revitcli inspect workflows --output markdown                 # discover local workflow YAML and next commands
revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run # preview workflow steps
revitcli workflow review .revitcli/workflows/pre-issue.yml --output markdown # review approval/evidence handoff
revitcli workflow suggest --output yaml                       # draft workflow from repeated journal commands
revitcli workflow receipts --name pre-issue --output markdown # review one workflow's receipts
revitcli issue preflight --profile .revitcli/issue.yml --output markdown --fail-on warning # issue readiness gate
revitcli issue diff --from .revitcli/history/baseline.json --to current --review --output markdown # issue diff review
revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue.zip --dry-run --include-receipts true --output markdown # dry-run delivery package
revitcli score --history 30d --output json                    # machine-readable model health trend
revitcli workbench contract --output json                     # machine-readable command contract
revitcli workbench verify --dir . --output markdown           # verify local workbench contract readiness
revitcli workbench receipts --output json                     # machine-readable receipt schema index
revitcli workbench paths --output json                        # flat callable command path index
revitcli workbench exits --output json                        # predictable exit-code index
revitcli workbench extensions --output json                   # terminal extension-point index
revitcli workbench outputs --output json                      # output schema index
revitcli workbench safeguards --output json                   # dry-run/approval safety index
revitcli workbench project --output json                      # local project artifact inventory
revitcli workbench handoff --output markdown                  # one-command terminal handoff
revitcli examples workbench                                  # discover v4 workbench command path
revitcli examples workflow --output json                     # machine-readable task recipe
revitcli examples recipes                                     # show Codex CLI recipe templates
revitcli report weekly --report .revitcli/reports/weekly.md  # local weekly report
revitcli report knowledge --output markdown                  # summarize reusable local project knowledge
revitcli standards install ../office-standards --dry-run --output markdown # preview standards bootstrap
revitcli standards install ../office-standards              # bootstrap a new project
revitcli standards validate --output markdown                # check local office standards
revitcli family purge --dry-run --report .revitcli/reports/family-purge.json # review cleanup candidates
revitcli release verify --tag v2.3.0 --output markdown       # local release preflight handoff
revitcli publish --since baseline.json                       # incremental re-export
revitcli import doors.csv --category doors --match-by Mark   # CSV → params
```

## Architecture

```
CLI (revitcli.exe)  ──HTTP REST──>  Revit Add-in (embedded HTTP server)
                                         │
                                    ExternalEvent Bridge
                                         │
                                    Revit API (main thread)
```

- **CLI** — Standalone .NET 8 console app (works headless in CI / scripts)
- **Revit Add-in** — Multi-target (`net48` for 2024, `net8.0-windows` for 2025/2026), EmbedIO HTTP server with ExternalEvent thread bridge for safe Revit API access
- **Shared** — `netstandard2.0` DTOs and the `IRevitOperations` interface

## Commands

| Command | Description |
|---|---|
| `revitcli status` | Show Revit version, addin version, active document; supports table/JSON output |
| `revitcli doctor` | Diagnose setup and connection issues; supports table/JSON output |
| `revitcli query <category>` | Query elements with filters; output table/JSON/CSV |
| `revitcli set <category>` | Modify parameters with `--dry-run` or `--plan-output` preview |
| `revitcli plan show` / `apply` | Review and apply saved mutation plans |
| `revitcli fix [checkName]` | Preview or apply profile-driven parameter fixes |
| `revitcli rollback <artifact>` | Restore parameters from a fix baseline or plan receipt |
| `revitcli export --format <fmt>` | Export DWG/PDF/IFC; supports JSON dry-run plans, receipts, and delivery manifests |
| `revitcli schedule list` / `export` / `create` | Manage Revit schedules; list/export support table/JSON/Markdown, export also supports CSV, and create supports dry-run JSON/Markdown plus write receipts |
| `revitcli schedules ensure` / `batch-export` / `compare` | Manage versioned schedule specs with dry-run ensure plans, traceable CSV export manifests, and baseline/current CSV diff reports |
| `revitcli audit` | Run model quality checks |
| `revitcli check` | Profile-driven check pipeline; supports table/JSON/HTML/SARIF/PR-comment output |
| `revitcli publish [name]` | Profile-driven export pipeline (DWG/PDF/IFC), with JSON dry-run plans, receipts, and delivery manifests |
| `revitcli init <template>` | Bootstrap a `.revitcli.yml` profile |
| `revitcli score` | Model health score from `check` results; supports table/JSON/Markdown output |
| `revitcli coverage` | Profile coverage report (which checks ran) |
| `revitcli inspect categories` | Discover common categories and next commands |
| `revitcli inspect params <category>` | Discover parameter coverage, write status, storage type, and dry-run probes |
| `revitcli inspect schedules` | Discover schedules, readiness issues, filters, and ready-to-run export commands |
| `revitcli inspect sheets` | Discover sheets, issues, filters, and export candidates for CLI/Codex workflows |
| `revitcli inspect workflows` | Discover local workflow YAML files with validate/simulate/review/dry-run/receipt next commands |
| `revitcli inspect plans` | Discover saved mutation plans with show, dry-run apply, approved apply, receipt, and rollback-preview commands |
| `revitcli sheets verify` / `issue-meta` / `renumber` / `index` | Verify sheet numbering, plan dry-run issue metadata and renumber updates, and manage local sheet-frame expectations |
| `revitcli rooms renumber` | Plan deterministic room number updates from local YAML rules with frozen room ids |
| `revitcli marks assign` / `verify` | Plan door/window Mark updates from local YAML rules and verify duplicates, missing Marks, and rule conformance |
| `revitcli examples <topic>` | Show copy-paste examples for common architect workflows; supports table/JSON/Markdown output |
| `revitcli views audit` / `template-apply` / `clone-set` | Audit view standards and create dry-run plans for view template assignment or cloned view sets |
| `revitcli links audit` / `repair` | Audit coordination links and create dry-run path/load repair plans without coordinate moves |
| `revitcli model map-check` / `map-fix` | Audit and plan workset/phase mapping fixes with write precheck evidence |
| `revitcli workbench contract` / `verify` / `receipts` / `paths` / `exits` / `extensions` / `outputs` / `safeguards` / `project` / `handoff` | Show and verify the stable command/output/dry-run/receipt/exit-code contract, receipt schema index, callable path index, exit-code index, extension-point index, output schema index, dry-run/approval safety index, local project artifact inventory, and one-command terminal handoff for Codex CLI |
| `revitcli workflow validate` / `simulate` / `review` / `run` / `suggest` / `receipts` | Check, review, run, draft, and inspect reusable terminal workflow YAML with approval gates |
| `revitcli issue preflight` / `diff` / `package` | Run the v5 issue closure contract with hidden-mutation preflight, issue-scoped diff review, dry-run package planning, child receipt traceability, bundle hash, and optional journal signature evidence |
| `revitcli report weekly` / `knowledge` | Generate local weekly reports and project knowledge summaries from RevitCli artifacts |
| `revitcli deliverables list` / `stats` / `verify` / `plan` / `bundle` | Review delivery plans, manifest entries, receipt traceability, and package handoff zips |
| `revitcli standards install` / `validate` | Install and validate required profiles, workflows, outputs, schedules, and family rules |
| `revitcli release verify` | Check local release files, version/tag consistency, CI guardrails, and smoke documentation; use `--output markdown` for handoff notes |
| `revitcli snapshot` | Capture model semantic state as JSON |
| `revitcli diff <from> <to>` | Diff two snapshots, or add `--review` for anomaly/notable/routine triage |
| `revitcli import <file>` | Batch-write parameters from CSV, with `--plan-output` support |
| `revitcli config show` / `set` | View or modify CLI configuration |
| `revitcli batch <file>` | Execute commands from a JSON file |
| `revitcli completions <shell>` | Generate shell completions (bash/zsh/PowerShell) |
| `revitcli interactive` / `-i` | Interactive REPL mode |
| `revitcli history init` / `capture` / `list` / `prune` / `diff` / `trend` | Local snapshot timeline + ASCII trend (v1.6) |
| `revitcli score --history <duration>` | Per-day score time series with table/JSON/Markdown output |
| `revitcli check --output sarif\|pr-comment` | SARIF 2.1.0 / PR-comment report (v1.7) |
| `revitcli ci doctor` | Detect CI provider + emit workflow snippet (v1.7) |
| `revitcli profile validate` / `show --resolve` / `diff` / `install` | Profile lint, resolve, diff, git install (v1.9) |
| `revitcli family ls` / `validate` / `purge` / `export` | List, check, report, purge, and export Revit families |
| `revitcli dashboard serve` / `build` | Serve / package the static dashboard (v2.0 — phase 1) |
| `revitcli journal show` / `stats` / `review` / `sign` / `verify` | Browse, review, and verify local operation history |

## Features

### Query / Set

- Category-based collection with English + Chinese aliases (`walls`/`墙`, `doors`/`门`, etc.)
- Filter expressions: `name=Foo`, `height > 3000`, `type!=Default`
- Pseudo fields: `id`, `name`, `category`, `type` + parameter fields with numeric unit conversion
- Duplicate parameter disambiguation via `[N]` suffix
- Output formats: table (Spectre.Console), JSON (scriptable), CSV
- `inspect` discovery commands support table, JSON, and Markdown for terminal review, scripts, or handoff notes
- `inspect params <category>` shows value coverage, writable/read-only status, storage types, sample element IDs, and ready-to-copy element-scoped `set --dry-run` probes
- `inspect params doors --writable-only --missing-only` narrows parameter discovery to fix candidates for delivery-day schedule data
- `inspect schedules --category Doors --ready-only` narrows schedule discovery for delivery-day exports; `--empty-only` and `--issues-only` surface empty or incomplete schedules before handoff
- `inspect workflows --output json|markdown` prints `inspect-workflows.v1`, a read-only local workflow YAML inventory with validate, simulate, review, dry-run, approved-run, and receipt review commands
- `inspect plans --dir <path> --output json|markdown` prints `inspect-plans.v1`, a read-only saved-plan inventory with action counts, high-impact/invalid status, receipt detection, `plan show`, dry-run apply, approved apply, and rollback-preview commands
- `schedule list/export --output markdown` adds handoff-ready schedule review tables while preserving JSON/CSV for scripts and spreadsheet workflows
- `sheets verify --against .revitcli/sheets/index.yml --output json` checks numbering gaps, duplicate sheet numbers, required sheet declarations, and required placed-view counts without writing to Revit.
- `sheets index init` bootstraps `.revitcli/sheets/index.yml` from the active model for later review; it only writes the local YAML file and refuses to overwrite without `--force`.
- `set` supports category+filter, `--id`, `--ids-from FILE`, or stdin pipe; all-or-nothing Transaction; `--dry-run` previews
- `set --plan-output .revitcli/plans/fire-rating.json` writes a frozen-ID plan; review with `revitcli plan show`, then apply with `revitcli plan apply <file> --yes`
- `import --plan-output .revitcli/plans/door-hardware.json` validates CSV writes through dry-run groups before writing a saved plan

### Export

- **DWG** — Per-view/sheet export with wildcard matching (`A1*`, `all`)
- **PDF** — Combined PDF output
- **IFC** — Whole-model export (IFC2x3)
- `--sheets` / `--views` / `--output-dir` for selection and routing
- Path traversal guarded; OutputDir restricted to user home

### Audit / Check

- `audit` (built-in rules): `naming`, `room-bounds`, `level-consistency`, `unplaced-rooms`, `duplicate-room-numbers`, `room-metadata`
- `check` (profile-driven): combine multiple rules + suppressions + `failOn: error|warning` exit code policy
- `score` rolls check results into a single 0–100 model-health number; `score --history 30d --output json` prints a compact `model-health-history.v1` envelope from local snapshots
- `coverage` reports which checks actually ran vs which were skipped

### Publish — profile-driven export pipelines

- `.revitcli.yml` defines named pipelines (DWG / PDF / IFC presets) with sheet selectors
- Pre-publish hook can run `check` first; failed checks abort by default
- Webhook + journal logging for CI integration
- Successful real exports and publishes write receipts plus `.revitcli/deliveries/manifest.jsonl` entries; dry-runs never write delivery files
- `deliverables plan --profile .revitcli.yml --since baseline.json --output markdown` prints a read-only `delivery-plan.v1` profile export plan with pipeline/preset output paths, baseline sheet counts when available, review command paths, and risk evidence before publish
- `deliverables list`, `deliverables stats`, and `deliverables verify` review the local delivery manifest in table, JSON, or Markdown and confirm each entry points back to a readable receipt
- `deliverables bundle --dry-run --output markdown` previews the manifest receipts and output files that would be packaged; real bundle runs write a zip plus `delivery-bundle-receipt.v1` sidecar

### Publish `--since` — incremental re-export (v1.2.0)

- Diff a baseline snapshot against the current model and re-export only the **changed sheets** instead of the whole pipeline
- `--since-mode content|meta` — `content` traces sheet → placed views → element hashes (default); `meta` only inspects sheet metadata
- `--update-baseline` rewrites the baseline atomically on successful publish
- Profile alternative: `publish.<pipeline>.incremental: true` + `baselinePath` for hands-off operation
- Backward-compatible: a v1.1.0 baseline (without ContentHash) auto-falls back to MetaHash; no schema bump

### Snapshot / Diff — Model-as-Code (v1.1.0)

- `snapshot` writes a stable JSON capture of the model: per-category elements with hash, sheets (with placed-view ids), schedules (with rows + columns)
- Hashes use SHA256-truncated-16 — stable across runs; idempotent on unchanged models
- `diff` produces table (terminal), JSON (scriptable), or markdown (PR-ready) output
- `diff --review` adds deterministic anomaly/notable/routine triage for removed items, blanked values, critical parameters, sheet changes, and large batches
- `--summary-only` for fast metric-only snapshots
- Use cases: weekly model report, PR description for shared models, baseline for `publish --since`

### Workflows — reusable terminal checklists

- Workflow YAML lives under `.revitcli/workflows/*.yml`
- Built-in templates: `pre-issue`, `weekly-health`, `export-package`, and `family-cleanup`
- `workflow init <template>` creates a workflow YAML in the project; `workflow init all` installs the whole pack
- Each step must declare `run` and `mode: read-only|dry-run|mutating`
- `workflow validate` checks files without running any command; `--output markdown` produces handoff-ready validation notes
- `workflow validate` recognizes v4 read-only handoff paths such as `workbench verify`, `workbench handoff`, `inspect workflows`, `inspect plans`, `report knowledge`, and `workflow review`, while rejecting unknown grouped subcommands before execution
- `workflow validate`, `workflow simulate`, and `workflow run` reject unknown `--output` values before reading or executing workflow steps
- Shell completions only suggest output formats each workflow subcommand actually accepts: `workflow suggest` includes YAML, while validate/simulate/review/run/examples/receipts stay on table/JSON/Markdown
- `workflow simulate <file>` prints the planned steps, risk modes, and validation issues as table, JSON, or Markdown for Codex CLI review
- `workflow review <file> --dir <path> --output json|markdown` prints `workflow-review.v1` with pre-run workbench verify/handoff commands for the same project directory, approval counts, inferred project artifact readiness for workflow step dependencies, saved-plan review through `inspect plans --dir <path>`, recommended dry-run/approval commands, post-run receipt triage commands, acceptance evidence hints, and handoff notes without running commands
- `workflow run <file> --dry-run --output markdown` prints a reviewable execution plan; `workflow run <file> --yes` is required before approved mutating steps run, and `--timeout-ms <n>` fails a long-running step with exit code 124 before writing receipt evidence
- Real `workflow run` executions write `.revitcli/workflows/receipts/*.json` with a stable `workflow-run-receipt.v1` schema, step exit codes, command metadata, run/step duration metadata, timeout metadata, and success/failure status
- `workflow receipts --output markdown` reviews saved workflow-run receipts with duration evidence; add `--failed-only` to triage failed deadline workflows, `--name pre-issue` to focus one repeated workflow, `--min-duration-ms 60000` to isolate slow runs, `--sort duration` to list slowest runs first, or `--window 24h` to review recent automation only
- `workflow suggest` reads explicit `command` / `commandLine` / `run` fields from `.revitcli/journal.jsonl` and prints suggested YAML only; it does not write workflow files
- `workflow examples [template]` shows architect prompts, preview commands, approval commands, and acceptance evidence for the built-in workflow pack

### Codex Recipe Templates

- `docs/templates/codex-recipes/` stores prompt-to-command recipes for common architect tasks
- `examples recipes` points Codex CLI to the local recipe templates and related safe commands
- `examples <topic> --output json` prints an `example-recipes.v1` envelope with commands and the Codex prompt for that task
- `examples workbench` shows the v4 contract, verifier, path index, receipt index, recipe, and workflow-review command path from one terminal topic
- Recipes are documentation only; RevitCli does not embed a prompt interpreter or hidden agent runtime

### Workbench Contract

- `workbench contract --output json` prints a compact `workbench-contract.v1` envelope for Codex CLI and shell scripts
- `workbench contract --contract workbench-contract.v2 --output json|markdown` prints the v5-compatible `workbench-contract.v2` envelope with the same visible command vocabulary plus issue closure paths
- `workbench verify --dir <path> --output json|markdown` prints `workbench-verification.v1`, checking root-command alignment, MCP public exclusion plus hidden/deprecated legacy MCP compatibility, LLM/dashboard/cloud exclusion, recipe output support, model-health terminal output, risky-command dry-run/receipt coverage, workflow and saved-plan discovery, built-in workflow template validity, workflow duration telemetry, workflow receipt triage, workflow-review pre-run/post-run handoff and artifact-readiness coverage, shell completion coverage, project inventory coverage, handoff readiness actions, handoff recommended command phases, schedule-create safety, schedule spec/export/rollback readiness, view plan id/collision/rollback-guard readiness, and exit-code notes
- `workbench verify --contract workbench-contract.v2 --dir <path> --output json|markdown` prints `workbench-verify-report.v2`, adding v5 issue closure checks for hidden model mutations, package traceability, dashboard optionality, and v2 command/schema compatibility while preserving v1 surfaces
- `workbench receipts --output json|markdown` prints `workbench-receipts.v1`, indexing write/export receipt schemas, path patterns, dry-run commands, and review commands
- `workbench paths --output json|markdown` prints `workbench-paths.v1`, a flat list of concrete `revitcli ...` command paths with risk, output, dry-run, receipt, and exit-code notes
- `workbench exits --output json|markdown` prints `workbench-exit-codes.v1`, a compact command-to-exit-code index for scripts and Codex CLI
- `workbench extensions --output json|markdown` prints `workbench-extensions.v1`, listing terminal-first extension points such as profiles, workflow YAML, standards packs, family rules, and recipe docs with validation and preview commands
- `workbench outputs --output json|markdown` prints `workbench-outputs.v1`, listing readable table support, compact JSON schema names, and Markdown support for key terminal paths
- `workbench safeguards --output json|markdown` prints `workbench-safeguards.v1`, listing dry-run, approval, receipt, and review commands for risky terminal paths
- `workbench project --dir <path> --output json|markdown` prints `workbench-project.v1`, a read-only local inventory of profile, standards, workflows, receipts, history, journal, delivery manifest, plans, and reports with review commands
- `workbench handoff --dir <path> --output json|markdown` prints `workbench-handoff.v1`, combining verification status, readiness check summaries, project artifact counts, machine-readable readiness actions for actionable missing or empty artifacts, recommended next commands such as workflow discovery, saved-plan discovery, and schedule-create dry-run, and non-goal reminders for terminal handoff
- `sheets issue-meta --selector all --issue-code R03 --issue-date 2026-05-20 --plan-output .revitcli/plans/sheet-issue.json --dry-run --output json|markdown` writes a `sheet-issue-plan.v1` dry-run artifact with frozen sheet ids, old/new issue parameter values, skipped-parameter evidence, `plan show` review support, and `plan apply` receipts/rollback actions
- `sheets renumber --rule .revitcli/sheets/numbering.yml --selector all --plan-output .revitcli/plans/sheet-renumber.json --dry-run --output json|markdown` writes a `sheet-renumber-plan.v1` dry-run artifact with frozen sheet ids, old/new sheet numbers, duplicate-target protection, stale-number apply validation, and receipt rollback actions
- `rooms renumber --rule .revitcli/numbering/rooms.yml --scope all --plan-output .revitcli/plans/room-numbering.json --dry-run --output json|markdown` writes a `room-numbering-plan.v1` dry-run artifact with frozen room ids, deterministic level/zone/type sorting, duplicate-target protection, stale-number apply validation, and receipt rollback actions
- `marks assign --category doors --rule .revitcli/numbering/doors.yml --plan-output .revitcli/plans/door-marks.json --dry-run --output json|markdown` writes a `mark-assignment-plan.v1` dry-run artifact with frozen element ids, deterministic level/zone/type/location sorting, duplicate-target protection, stale-Mark apply validation, and receipt rollback actions
- `marks verify --category doors,windows --against ".revitcli/numbering/*.yml" --output json|markdown` prints `mark-verify-report.v1` with duplicate Marks, missing Marks, and optional rule-conformance issues without writing the model
- `schedules ensure --spec .revitcli/schedules/*.yml --plan-output .revitcli/plans/schedule-ensure.json --dry-run --mode create-only|sync-fields --output json|markdown` writes a `schedule-ensure-plan.v1` from `schedule-spec.v1` YAML with fields, filters, sort, key columns, and old structure baselines
- `schedules batch-export --set issue --output-dir exports/schedules/current --format csv --manifest exports/schedules/current/manifest.json --output json|markdown` writes CSVs plus `schedule-export-manifest.v1` entries traceable to schedule ids and output paths
- `schedules compare --from exports/schedules/baseline --to exports/schedules/current --keys Number,Mark --output json|markdown` prints a read-only `schedule-diff-report.v1` for changed, added, and removed CSV rows
- `views audit --rules .revitcli/views/standards.yml --templates --browser --output json|markdown` prints `view-standards-report.v1` for template mismatches, browser parameter gaps, and naming issues
- `views template-apply --selector "Level*" --template "Architectural Plan" --plan-output .revitcli/plans/view-template.json --dry-run --output json|markdown` writes `view-template-plan.v1` with frozen view ids plus old/new template ids
- `views clone-set --from-set "Level*" --to-prefix "Tender - " --naming-rule "{prefix}{name}" --plan-output .revitcli/plans/view-clone.json --dry-run --output json|markdown` writes `view-clone-plan.v1`, fails on target-name collisions, and carries rollback guards for placed views
- `links audit --rules .revitcli/links/rules.yml --check paths,loaded,coordinates --output json|markdown` prints `link-audit-report.v1` for link path, loaded-state, and coordinate fingerprint drift without writing the model
- `links repair --map .revitcli/links/paths.yml --plan-output .revitcli/plans/link-repair.json --dry-run --max-changes 20 --output json|markdown` writes `link-repair-plan.v1` with old/new paths, load-state changes, file existence, and timestamp/size evidence; it does not plan coordinate moves
- `model map-check --against .revitcli/model-mapping.yml --worksets --phases --output json|markdown` prints `model-map-report.v1` for workset and phase ownership drift
- `model map-fix --against .revitcli/model-mapping.yml --scope rooms,doors,walls --plan-output .revitcli/plans/model-map-fix.json --dry-run --output json|markdown` writes `model-map-fix-plan.v1` with element ids, old/new workset or phase values, and write precheck/probe-status fields before any future apply path
- `issue preflight --profile .revitcli/issue.yml --output json|markdown --fail-on warning|error` prints `issue-preflight-report.v1` with artifact, command, mutation-plan, deliverables, and hidden-mutation checks before issue work proceeds
- `issue diff --from baseline.json --to current --review --output json|markdown` prints `issue-diff-report.v1`, reusing snapshot diff review groups for issue-day anomaly/notable/routine triage
- `issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue.zip --dry-run --sign-journal --include-receipts true --output json|markdown` prints `issue-package-receipt.v1` without writing delivery files in dry-run; approved package writes include manifest path, child files/receipts, bundle hash, and optional journal signature evidence
- `schedule create --dry-run --output json|markdown` prints `schedule-create.v1` without calling Revit; real schedule creates write `schedule-create-receipt.v1` under `.revitcli/receipts` by default, expose `receiptRequired`/`receiptSaved`, and JSON/Markdown failures use the same schema
- Shell completions keep inspect, workflow, schedules, views, links, model, schedule, rooms, marks, and issue output formats aligned by subcommand, and `workbench verify` guards the inspect/workbench/workflow/schedules/views/links/model/schedule/rooms/marks/issue completion surface: inspect commands include `inspect plans`, workflow suggest uses table/JSON/YAML, workflow reports use table/JSON/Markdown, schedules ensure/batch-export/compare, views audit/template-apply/clone-set, links audit/repair, model map-check/map-fix, issue preflight/diff/package use table/JSON/Markdown, schedule list/create use table/JSON/Markdown, schedule export also offers CSV, and rooms/marks numbering commands offer table/JSON/Markdown
- The contract lists stable command vocabulary, callable command paths, recipe discovery, risk mode, JSON/Markdown support, recommended first command, dry-run expectations, receipt locations, and exit-code notes
- Write paths without a dry-run/receipt contract are intentionally excluded from the Codex callable path index; `schedule create` is included now that it has a dry-run preview and receipt contract
- JSON includes `commandPaths` entries such as `plan apply`, `score --history`, `inspect workflows`, `inspect plans`, `workflow review`, `workbench contract --contract workbench-contract.v2`, `workbench verify`, `workbench verify --contract workbench-contract.v2`, `workbench receipts`, `workbench paths`, `workbench exits`, `workbench extensions`, `workbench outputs`, `workbench safeguards`, `workbench project`, `workbench handoff`, `schedules ensure`, `schedules batch-export`, `schedules compare`, `views audit`, `views template-apply`, `views clone-set`, `links audit`, `links repair`, `model map-check`, `model map-fix`, `rooms renumber`, `marks assign`, `marks verify`, `issue preflight`, `issue diff`, `issue package`, `deliverables plan`, and `deliverables bundle` so Codex CLI can choose concrete commands without scraping help text
- Output formats are table, JSON, and Markdown; workbench commands are read-only and have no Revit API, dashboard, cloud, LLM runtime, or MCP dependency

### Reports — local project summaries

- `report weekly` reads `.revitcli/history` and `.revitcli/journal.jsonl` without contacting Revit
- `report knowledge` reads local history, journal commands, workflow receipts, delivery manifests/receipts, standards validation, and saved weekly reports to surface reusable review hints and workflow YAML drafts
- Knowledge hints are drafts for human review, such as `workflow suggest`, failed workflow receipt triage, delivery verification, or standards validation; the command never writes workflows or standards
- Suggested workflow YAML in knowledge reports is review evidence only; save and validate it manually before using it as a real workflow
- Output formats: table, JSON, and Markdown
- `--report .revitcli/reports/weekly.md` writes a Markdown report for review handoff

### Standards — local office requirements

- `.revitcli/standards.yml` records pack metadata, compatibility notes, required profiles, workflow files, output paths, schedule templates, and built-in family rule ids
- `standards install <path-or-git-url>` copies governed files from an approved pack into the project, including every relative profile listed under `required.profiles`; use `--dry-run` to preview and `--force` to overwrite existing standards/profile/workflow files
- `standards validate` checks those local files without contacting Revit; run `workflow validate --output markdown` for the installed workflows before issue work starts
- `family validate --rules-from .revitcli/standards.yml` runs the reusable family rule set declared by the standards pack
- Output formats: table, JSON, and Markdown for terminal review, CI jobs, or handoff notes

### Family Cleanup

- `family ls --unused` lists unplaced families before cleanup.
- `family purge` defaults to dry-run unless `--apply --yes` is provided.
- `family purge --dry-run --report .revitcli/reports/family-purge.json` writes a stable `family-purge-report.v1` JSON artifact with candidates, keep-pattern matches, placed/in-place exclusions, safety gates, and apply results.
- The built-in `family-cleanup` workflow uses the same purge report path so Codex CLI can show review evidence before destructive cleanup.

Example manifest:

```yaml
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Portable CLI-only standards pack.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty, category-known]
```

### Import — CSV writeback (v1.3.0)

- `revitcli import doors.csv --category doors --match-by Mark` — bulk-write Revit parameters from a CSV
- **Auto encoding detection**: BOM (UTF-8 / UTF-16 LE / UTF-16 BE) → strict UTF-8 → GBK fallback. Excel-exported Chinese CSV works out of the box
- `--map "col:RevitParam,col2:RevitParam2"` for column → parameter mapping (defaults to identity)
- `--dry-run` previews per-group changes
- `--on-missing error|warn|skip` and `--on-duplicate error|first|all` for row-level policies
- `--batch-size N` chunks `SetRequest` calls (default 100, max 1000)
- Reuses existing `/api/elements/set` endpoint — **no addin changes needed**; v1.2.0 addin works with v1.3.0 CLI
- Exit codes: 0 success / dry-run, 1 setup error, 2 partial row failures

## Profile system (`.revitcli.yml`)

`check`, `publish`, `init`, `score`, `coverage`, and `plan apply` consume project profiles loaded by `ProfileLoader.Discover()` (walks up from cwd looking for `.revitcli.yml`).

```yaml
version: 1
extends: ./shared.yml          # single-parent only; child REPLACES named keys, not deep-merge

defaults:
  planMaxChanges: 50           # default cap for plan apply when --max-changes is omitted
  highImpactChanges: 20        # require --confirm-high-impact at or above this many writes

checks:
  default:
    rules: [naming, room-bounds, level-consistency]
    failOn: error              # error | warning

publish:
  default:
    precheck: default
    presets: [publish-dwg]
    incremental: true                          # v1.2.0
    baselinePath: .revitcli/last-publish.json
    sinceMode: content                          # content | meta
```

Starter templates in `profiles/`:

| Profile | Use case |
|---|---|
| `architectural-issue.yml` | Architectural projects — room data, sheet completeness, pre-issue gate |
| `interior-room-data.yml` | Interior design / FM handover — room metadata, naming, department |
| `general-publish.yml` | Any project — basic health checks + DWG/PDF/IFC export pipelines |

`revitcli init <template>` copies one to your project root.

### Auto-fix Playbooks

- `fix --dry-run` turns `check` issues into a reviewable parameter write plan.
- `fix --plan-output .revitcli/plans/issue-fixes.json` saves a frozen fix plan for `plan show` and `plan apply`.
- `fix --apply --yes` writes a snapshot baseline and fix journal before modifying the model.
- `rollback <artifact> --yes` restores only the parameters touched by that fix journal or plan receipt.
- v1.5 supports parameter-only strategies: `setParam` and `renameByPattern`.

### Safe Plans

- `set --plan-output FILE` validates through dry-run and stores frozen element IDs plus old/new preview values.
- `import --plan-output FILE` parses the CSV, freezes matched element IDs, dry-runs each write group, and stores group previews.
- `fix --plan-output FILE` freezes generated fix actions; `plan apply FILE --yes` captures a baseline and fix journal for rollback.
- `plan show FILE` prints a reviewable summary for humans or Codex CLI.
- `plan show FILE --output json` prints a stable `plan-summary.v1` envelope with risk, commands, issues, and the original plan.
- `plan show FILE --output markdown` prints a handoff-ready review with risk, issues, preview rows, and dry-run/apply commands.
- `plan apply FILE --dry-run` revalidates the saved plan; `plan apply FILE --yes` writes and creates `FILE.receipt.json` with a stable `plan-receipt.v1` schema, affected element IDs, command metadata, model context when available, rollback actions for set/import plans, and baseline/journal pointers for fix plans.
- `rollback FILE.receipt.json --dry-run` previews set/import plan receipt rollback; `rollback FILE.receipt.json --yes` restores old values after checking for current-value conflicts.
- `plan apply` honors profile defaults `defaults.planMaxChanges` and `defaults.highImpactChanges`; high-impact real writes require `--confirm-high-impact` after review.

### Journal Integrity

- `.revitcli/journal.jsonl` records write and publish operations.
- `revitcli journal show` and `revitcli journal stats` review recent operations, actions, operators, categories, affected counts, and affected element IDs from the terminal; `journal show` can filter by `--action`, `--category`, `--operator`, and `--user`.
- `revitcli journal review` groups recent entries by risk, action, category, and operator, highlights mutating/high-impact work, and supports `--output markdown` for handoff notes.
- `revitcli journal sign` writes `.revitcli/journal.jsonl.sig` with a SHA256 hash chain and local HMAC.
- `revitcli journal verify` exits non-zero if a signed entry was inserted, removed, or modified.
- See [docs/journal-signing.md](docs/journal-signing.md) for custom key and CI usage.

## Requirements

- .NET 8 SDK
- Autodesk Revit **2024** (net48), **2025** or **2026** (net8.0-windows) for the add-in
- Windows (Revit is Windows-only); CLI itself runs on Linux/macOS for headless use against a remote Revit host

## Build

```bash
# CLI + Shared (any OS)
dotnet build src/RevitCli/RevitCli.csproj
dotnet test  tests/RevitCli.Tests/

# Add-in (Windows + Revit only). RevitYear picks DLL refs, target framework, and output dir.
dotnet build src/RevitCli.Addin -p:RevitYear=2026 -p:Revit2026InstallDir="D:\Autodesk\Revit 2026"
```

## Install

### CLI

```bash
dotnet tool install --global RevitCli
```

Or build from source:

```bash
dotnet publish src/RevitCli -c Release -o ./publish
```

### Revit Add-in

1. Build: `dotnet publish src/RevitCli.Addin -c Release -p:RevitYear=2026 -p:Revit2026InstallDir="D:\Autodesk\Revit 2026"`
2. Close Revit
3. Copy all files from `src/RevitCli.Addin/bin/Release/2026/publish/` to `%APPDATA%\Autodesk\Revit\Addins\2026\`
4. Start Revit and open a project
5. Verify: `revitcli doctor`

Or run `scripts/install.ps1` for end-user install (auto-detects installed Revit years, generates per-year manifests, adds CLI to PATH).
If Revit is running, the installer updates the CLI immediately and stages
add-in files for the next Revit restart instead of overwriting locked DLLs.

For the v2.3 release package with Revit 2026 outside `C:\Program Files`, pass
the local install directory:

```powershell
.\scripts\install.ps1 -RevitYears 2026 `
  -Revit2026InstallDir "D:\revit2026\Revit 2026" `
  -Force
```

For source-tree installs covering multiple Revit years, pass per-year install
directories:

```powershell
.\scripts\install.ps1 -RevitYears 2024,2025,2026 `
  -Revit2024InstallDir "D:\Autodesk\Revit 2024" `
  -Revit2025InstallDir "D:\Autodesk\Revit 2025" `
  -Revit2026InstallDir "D:\Autodesk\Revit 2026" `
  -Force
```

`-RevitInstallDir` is still accepted as a legacy alias for Revit 2026.

## Configuration

```bash
revitcli config show
revitcli config set serverUrl http://localhost:9999
revitcli config set defaultOutput json
revitcli config set exportDir ./my-exports
revitcli config set Revit2026InstallDir "D:\revit2026\Revit 2026"
```

## Shell Completions

```bash
revitcli completions bash       >> ~/.bashrc
revitcli completions zsh        >> ~/.zshrc
revitcli completions powershell >> $PROFILE
```

## Quick Start (5 minutes)

```bash
# 1. Install CLI + Add-in (see Install section)
revitcli doctor --output json

# 2. Bootstrap a profile in your project
cp profiles/general-publish.yml .revitcli.yml

# 3. Run quality checks → HTML report
revitcli check
revitcli check --report report.html

# 4. Publish deliverables
revitcli deliverables plan --profile .revitcli.yml --output markdown
revitcli publish --dry-run
revitcli publish
revitcli deliverables verify --output markdown
revitcli deliverables bundle --dry-run --output markdown
revitcli deliverables bundle --bundle-path deliverables/review-package.zip

# 5. Capture a baseline for next week
revitcli snapshot --output .revitcli/baseline.json
```

## Using With Codex CLI

Architects can use Codex CLI as a conversational terminal operator that
calls `revitcli` commands for checking, publishing, schedule export, and
safe parameter edits. RevitCli remains deterministic and local; Codex CLI
should run read-only or `--dry-run` commands before any write. CLI-only
inspect commands such as `revitcli inspect sheets --ready-only` and
`revitcli inspect sheets --issues-only --output markdown` help Codex CLI
discover available sheets, review blockers, and export candidates before
building a publish/export plan.

Example prompts:

```text
帮我检查这个模型今天能不能出图,只 dry-run,不要写入。
把门表导成 CSV,放到 deliverables/tables。
帮我查一下为什么 publish 失败,先看 journal 和 check 结果。
```

See [docs/codex-cli-architect-workflows.md](docs/codex-cli-architect-workflows.md)
for the operating model, guardrails, and command paths.
See [docs/architect-terminal-vision.md](docs/architect-terminal-vision.md)
for the product intent and non-goals.

## Revit Real Smoke

Before review or release, validate the real Revit vertical slice with the internal smoke gate:

```text
doctor -> status -> query --id -> query <category> --filter -> set --dry-run -> set -> restore
```

For the current 2026 acceptance contract, use [docs/revit2026-real-smoke.md](docs/revit2026-real-smoke.md). For the multi-version gate, run:

```powershell
revitcli doctor --check-version 2025
.\scripts\smoke-revit.ps1 -Version 2025 -ElementId 12345 -Filter "Mark = W-01"
```

See [docs/revit-version-compatibility.md](docs/revit-version-compatibility.md) and copy [docs/ci/smoke-matrix-template.yml](docs/ci/smoke-matrix-template.yml) for 2024 / 2025 / 2026 self-hosted runner smoke.

## Project Structure

```
src/RevitCli/              # CLI console app (net8.0)
src/RevitCli.Addin/        # Revit add-in: net48 / net8.0-windows
shared/RevitCli.Shared/    # Shared DTOs and IRevitOperations (netstandard2.0)
tests/RevitCli.Tests/      # CLI tests (253 facts, runs anywhere)
tests/RevitCli.Addin.Tests/ # Add-in + protocol tests (Windows + Revit)
profiles/                  # Starter project profiles
docs/superpowers/          # Design specs and implementation plans
```

## Roadmap

- [x] v1.5-v2.0 BIMOps foundation: fix/rollback, history, CI, family, profile governance, dashboard
- [x] v2.1 configuration confidence: profile simulation, multi-version smoke scaffolding, journal signing
- [x] v2.2 release integrity: installer hardening, real multi-version smoke, release checklist
- [x] v2.3 Codex CLI-assisted architect workflow: inspect/discover, safe batch plans, delivery workflows, standards packs
- [x] v3.0 project standards workbench: office standards packs bootstrap profiles, workflows, outputs, family rules, and terminal validation
- [x] v3.x review and knowledge capture: local reports, journal-derived workflow drafts, recipes, and handoff-ready review evidence
- [ ] v4 terminal workbench: stable `workbench contract`, deeper workflow review, and long-running Revit automation ergonomics

See [docs/roadmap-2026q4-v4.md](docs/roadmap-2026q4-v4.md) for the Q4 → v4 terminal-first blueprint.

## Publishing

Follow [docs/release-checklist.md](docs/release-checklist.md) before pushing a tag.

1. Update `RevitCliVersion` in `Directory.Build.props` (single source of truth for both the CLI and add-in projects).
2. Update `CHANGELOG.md`.
3. Run `revitcli release verify --tag vX.Y.Z` and `revitcli workbench verify --dir . --output json`, then complete the Windows/Revit smoke checklist.
4. Tag and push: `git tag vX.Y.Z && git push origin vX.Y.Z`.
5. GitHub Actions packages the Revit 2026 add-in ZIP to the GitHub release. NuGet publishing is manual via the `Publish to NuGet` workflow.

> NuGet publishing requires the `NUGET_API_KEY` repository secret and should only be run when publishing the CLI package to NuGet.org.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`, `ci:`, `chore:`.

## License

MIT
