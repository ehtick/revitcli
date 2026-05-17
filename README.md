# RevitCli

Terminal-first Autodesk Revit automation for architects. RevitCli lets a
human operator or Codex CLI inspect models, find drawing blockers, export
sheets and schedules, diagnose publish failures, snapshot/diff model state,
and make reviewed parameter changes without clicking through repetitive
Revit UI.

> **Status: v2.3 - Inspect & Discover release**
> Windows/Revit-first BIMOps runner with source-level support for Revit
> 2024/2025/2026; the v2.3 release ZIP packages the Revit 2026 add-in.
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
revitcli query walls --filter "height > 3000" --output json  # query
revitcli schedule list --output markdown                     # review available schedules
revitcli schedule export --name "Door Schedule" --output csv # export a schedule
revitcli export --format dwg --sheets "A1*" --output-dir .   # batch export
revitcli set doors --param "Fire Rating" --value "60min"     # bulk set
revitcli snapshot --output snap.json                         # capture model state
revitcli diff snap-mon.json snap-fri.json --output markdown  # what changed
revitcli diff snap-mon.json snap-fri.json --review           # flag suspicious changes
revitcli workflow init pre-issue                             # create a workflow template
revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run # preview workflow steps
revitcli workflow suggest --output yaml                       # draft workflow from repeated journal commands
revitcli workflow receipts --output markdown                  # review workflow run receipts
revitcli examples recipes                                     # show Codex CLI recipe templates
revitcli report weekly --report .revitcli/reports/weekly.md  # local weekly report
revitcli standards install ../office-standards --dry-run     # preview standards bootstrap
revitcli standards validate                                  # check local office standards
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
| `revitcli rollback <baseline>` | Restore parameters changed by a fix baseline |
| `revitcli export --format <fmt>` | Export DWG/PDF/IFC; supports JSON dry-run plans, receipts, and delivery manifests |
| `revitcli schedule list` / `export` / `create` | Manage Revit schedules; list/export support table/JSON/Markdown, export also supports CSV |
| `revitcli audit` | Run model quality checks |
| `revitcli check` | Profile-driven check pipeline; supports table/JSON/HTML/SARIF/PR-comment output |
| `revitcli publish [name]` | Profile-driven export pipeline (DWG/PDF/IFC), with JSON dry-run plans, receipts, and delivery manifests |
| `revitcli init <template>` | Bootstrap a `.revitcli.yml` profile |
| `revitcli score` | Model health score from `check` results |
| `revitcli coverage` | Profile coverage report (which checks ran) |
| `revitcli inspect categories` | Discover common categories and next commands |
| `revitcli inspect params <category>` | Discover parameter coverage, write status, storage type, and dry-run probes |
| `revitcli inspect schedules` | Discover schedules, readiness issues, filters, and ready-to-run export commands |
| `revitcli inspect sheets` | Discover sheets, issues, filters, and export candidates for CLI/Codex workflows |
| `revitcli sheets verify` / `index` | Verify sheet numbering, required sheets, and local sheet-frame expectations |
| `revitcli examples <topic>` | Show copy-paste examples for common architect workflows |
| `revitcli workflow validate` / `simulate` / `run` / `suggest` / `receipts` | Check, run, draft, and review reusable terminal workflow YAML with approval gates |
| `revitcli report weekly` | Generate local history / score / diff / journal weekly reports |
| `revitcli deliverables list` / `stats` / `verify` / `bundle` | Review delivery manifest entries, receipt traceability, and package handoff zips |
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
| `revitcli score --history <duration>` | Per-day score time series (v1.6) |
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
- `score` rolls check results into a single 0–100 model-health number
- `coverage` reports which checks actually ran vs which were skipped

### Publish — profile-driven export pipelines

- `.revitcli.yml` defines named pipelines (DWG / PDF / IFC presets) with sheet selectors
- Pre-publish hook can run `check` first; failed checks abort by default
- Webhook + journal logging for CI integration
- Successful real exports and publishes write receipts plus `.revitcli/deliveries/manifest.jsonl` entries; dry-runs never write delivery files
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
- `workflow simulate <file>` prints the planned steps, risk modes, and validation issues as table, JSON, or Markdown for Codex CLI review
- `workflow run <file> --dry-run --output markdown` prints a reviewable execution plan; `workflow run <file> --yes` is required before approved mutating steps run
- Real `workflow run` executions write `.revitcli/workflows/receipts/*.json` with a stable `workflow-run-receipt.v1` schema, step exit codes, command metadata, and success/failure status
- `workflow receipts --output markdown` reviews saved workflow-run receipts; add `--failed-only` to triage failed deadline workflows
- `workflow suggest` reads explicit `command` / `commandLine` / `run` fields from `.revitcli/journal.jsonl` and prints suggested YAML only; it does not write workflow files
- `workflow examples [template]` shows architect prompts, preview commands, approval commands, and acceptance evidence for the built-in workflow pack

### Codex Recipe Templates

- `docs/templates/codex-recipes/` stores prompt-to-command recipes for common architect tasks
- `examples recipes` points Codex CLI to the local recipe templates and related safe commands
- Recipes are documentation only; RevitCli does not embed a prompt interpreter or hidden agent runtime

### Reports — local project summaries

- `report weekly` reads `.revitcli/history` and `.revitcli/journal.jsonl` without contacting Revit
- Output formats: table, JSON, and Markdown
- `--report .revitcli/reports/weekly.md` writes a Markdown report for review handoff

### Standards — local office requirements

- `.revitcli/standards.yml` records pack metadata, compatibility notes, required profiles, workflow files, output paths, schedule templates, and built-in family rule ids
- `standards install <path-or-git-url>` copies governed files from an approved pack into the project; use `--dry-run` to preview and `--force` to overwrite existing standards/profile/workflow files
- `standards validate` checks those local files without contacting Revit
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
- `rollback <baseline> --yes` restores only the parameters touched by that fix journal.
- v1.5 supports parameter-only strategies: `setParam` and `renameByPattern`.

### Safe Plans

- `set --plan-output FILE` validates through dry-run and stores frozen element IDs plus old/new preview values.
- `import --plan-output FILE` parses the CSV, freezes matched element IDs, dry-runs each write group, and stores group previews.
- `fix --plan-output FILE` freezes generated fix actions; `plan apply FILE --yes` captures a baseline and fix journal for rollback.
- `plan show FILE` prints a reviewable summary for humans or Codex CLI.
- `plan show FILE --output json` prints a stable `plan-summary.v1` envelope with risk, commands, issues, and the original plan.
- `plan show FILE --output markdown` prints a handoff-ready review with risk, issues, preview rows, and dry-run/apply commands.
- `plan apply FILE --dry-run` revalidates the saved plan; `plan apply FILE --yes` writes and creates `FILE.receipt.json` with a stable `plan-receipt.v1` schema, affected element IDs, command metadata, model context when available, and rollback pointers for fix plans.
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

# Add-in (Windows + Revit only). RevitYear picks DLL refs + output dir.
dotnet build src/RevitCli.Addin -p:RevitYear=2026   # default; also 2024 (net48) / 2025 (net8.0-windows)
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

1. Build: `dotnet publish src/RevitCli.Addin -c Release -p:RevitYear=2026`
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
  -Revit2026InstallDir "D:\revit2026" `
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
- [ ] v4 terminal workbench: dashboard/workbench polish, deeper workflow review, and long-running Revit automation ergonomics

See [docs/roadmap-2026q4-v4.md](docs/roadmap-2026q4-v4.md) for the Q4 → v4 terminal-first blueprint.

## Publishing

Follow [docs/release-checklist.md](docs/release-checklist.md) before pushing a tag.

1. Update `RevitCliVersion` in `Directory.Build.props` (single source of truth for both the CLI and add-in projects).
2. Update `CHANGELOG.md`.
3. Run `revitcli release verify --tag vX.Y.Z`, then complete the Windows/Revit smoke checklist.
4. Tag and push: `git tag vX.Y.Z && git push origin vX.Y.Z`.
5. GitHub Actions auto-publishes the CLI to NuGet.org and the Revit 2026 add-in ZIP to the GitHub release.

> Requires `NUGET_API_KEY` secret in repository settings.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`, `ci:`, `chore:`.

## License

MIT
