# RevitCli

Command-line interface for Autodesk Revit. Query elements, batch export, modify parameters, snapshot/diff models, and write parameters back from CSV — all from your terminal.

> **Status: v2.1 — Configuration Confidence complete**
> Terminal-first BIMOps runner for architects. Standards checking, deliverable publishing, model snapshots, incremental publish, CSV writeback, safe `fix`/`rollback`, multi-version smoke scaffolding, and signed journals. Supports Revit 2024/2025/2026.

```bash
revitcli status                                              # connection check
revitcli query walls --filter "height > 3000" --output json  # query
revitcli export --format dwg --sheets "A1*" --output-dir .   # batch export
revitcli set doors --param "Fire Rating" --value "60min"     # bulk set
revitcli snapshot --output snap.json                         # capture model state
revitcli diff snap-mon.json snap-fri.json --output markdown  # what changed
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
| `revitcli status` | Show Revit version, addin version, active document |
| `revitcli doctor` | Diagnose setup and connection issues |
| `revitcli query <category>` | Query elements with filters; output table/JSON/CSV |
| `revitcli set <category>` | Modify parameters with `--dry-run` preview |
| `revitcli fix [checkName]` | Preview or apply profile-driven parameter fixes |
| `revitcli rollback <baseline>` | Restore parameters changed by a fix baseline |
| `revitcli export --format <fmt>` | Export DWG / PDF / IFC |
| `revitcli schedule list` / `export` / `create` | Manage Revit schedules |
| `revitcli audit` | Run model quality checks |
| `revitcli check` | Profile-driven check pipeline |
| `revitcli publish [name]` | Profile-driven export pipeline (DWG/PDF/IFC) |
| `revitcli init <template>` | Bootstrap a `.revitcli.yml` profile |
| `revitcli score` | Model health score from `check` results |
| `revitcli coverage` | Profile coverage report (which checks ran) |
| `revitcli inspect schedules` | Discover schedules and ready-to-run export commands |
| `revitcli snapshot` | Capture model semantic state as JSON |
| `revitcli diff <from> <to>` | Diff two snapshots (table / JSON / markdown) |
| `revitcli import <file>` | Batch-write parameters from CSV |
| `revitcli config show` / `set` | View or modify CLI configuration |
| `revitcli batch <file>` | Execute commands from a JSON file |
| `revitcli completions <shell>` | Generate shell completions (bash/zsh/PowerShell) |
| `revitcli interactive` / `-i` | Interactive REPL mode |
| `revitcli history init` / `capture` / `list` / `prune` / `diff` / `trend` | Local snapshot timeline + ASCII trend (v1.6) |
| `revitcli score --history <duration>` | Per-day score time series (v1.6) |
| `revitcli check --output sarif\|pr-comment` | SARIF 2.1.0 / PR-comment report (v1.7) |
| `revitcli ci doctor` | Detect CI provider + emit workflow snippet (v1.7) |
| `revitcli profile validate` / `show --resolve` / `diff` / `install` | Profile lint, resolve, diff, git install (v1.9) |
| `revitcli family ls` | List Revit families (--unused, --category) (v1.8) |
| `revitcli dashboard serve` / `build` | Serve / package the static dashboard (v2.0 — phase 1) |

## Features

### Query / Set

- Category-based collection with English + Chinese aliases (`walls`/`墙`, `doors`/`门`, etc.)
- Filter expressions: `name=Foo`, `height > 3000`, `type!=Default`
- Pseudo fields: `id`, `name`, `category`, `type` + parameter fields with numeric unit conversion
- Duplicate parameter disambiguation via `[N]` suffix
- Output formats: table (Spectre.Console), JSON (scriptable), CSV
- `set` supports category+filter, `--id`, `--ids-from FILE`, or stdin pipe; all-or-nothing Transaction; `--dry-run` previews

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
- `--summary-only` for fast metric-only snapshots
- Use cases: weekly model report, PR description for shared models, baseline for `publish --since`

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

`check`, `publish`, `init`, `score`, `coverage` consume project profiles loaded by `ProfileLoader.Discover()` (walks up from cwd looking for `.revitcli.yml`).

```yaml
version: 1
extends: ./shared.yml          # single-parent only; child REPLACES named keys, not deep-merge

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
- `fix --apply --yes` writes a snapshot baseline and fix journal before modifying the model.
- `rollback <baseline> --yes` restores only the parameters touched by that fix journal.
- v1.5 supports parameter-only strategies: `setParam` and `renameByPattern`.

### Journal Integrity

- `.revitcli/journal.jsonl` records write and publish operations.
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

## Configuration

```bash
revitcli config show
revitcli config set serverUrl http://localhost:9999
revitcli config set defaultOutput json
revitcli config set exportDir ./my-exports
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
revitcli doctor

# 2. Bootstrap a profile in your project
cp profiles/general-publish.yml .revitcli.yml

# 3. Run quality checks → HTML report
revitcli check
revitcli check --report report.html

# 4. Publish deliverables
revitcli publish --dry-run
revitcli publish

# 5. Capture a baseline for next week
revitcli snapshot --output .revitcli/baseline.json
```

## Using With Codex CLI

Architects can use Codex CLI as a conversational terminal operator that
calls `revitcli` commands for checking, publishing, schedule export, and
safe parameter edits. RevitCli remains deterministic and local; Codex CLI
should run read-only or `--dry-run` commands before any write.

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
- [ ] v2.2 release integrity: installer hardening, real multi-version smoke, release checklist
- [ ] v2.3+ Codex CLI-assisted architect workflow: inspect/discover, safe batch plans, delivery workflows, standards packs, v4 terminal workbench

See [docs/roadmap-2026q4-v4.md](docs/roadmap-2026q4-v4.md) for the Q4 → v4 terminal-first blueprint.

## Publishing

1. Update `RevitCliVersion` in `Directory.Build.props` (single source of truth for both the CLI and add-in projects).
2. Update `CHANGELOG.md`.
3. Tag and push: `git tag v1.5.0 && git push origin v1.5.0`.
4. GitHub Actions auto-publishes the CLI to NuGet.org and the multi-Revit-year add-in ZIP to the GitHub release.

> Requires `NUGET_API_KEY` secret in repository settings.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`, `ci:`, `chore:`.

## License

MIT
