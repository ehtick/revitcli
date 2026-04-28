# Changelog

All notable changes to RevitCli will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added — v1.6 history (complete)

- `revitcli history` command cluster — local snapshot timeline under
  `.revitcli/history/`, gzip-compressed:
  - `history init` — create the store + empty index.
  - `history capture [--source manual|cron|fix-baseline] [--exclude-fixes]` —
    take a snapshot via the addin and append to the timeline.
  - `history list [--include-fixes] [--limit N]` — table view of recent
    snapshots (id, capturedAt, source, elementCount, size).
  - `history prune --keep <duration|count> [--dry-run] [--apply]` — drop old
    entries; fix-baseline snapshots are protected by default.
  - `history diff <fromRef> <toRef> [--output table|json|markdown]
    [--max-rows N] [--categories LIST]` — diff two snapshots resolved from
    the timeline; reuses the v1.1 `SnapshotDiffer` + `DiffRenderer`.
  - `history trend [--metric <name>] [--window <duration>] [--width N]` —
    ASCII sparkline (`▁▂▃▄▅▆▇█`) + per-point row over a rolling window.
    Default metric `score`, default window `30d`, default width 60. Other
    supported metrics: `elements.<category>`, `sheets`, `schedules`,
    `count.<key>`.
  - Time references parse `@-N` (Nth most recent), ISO 8601 timestamps, and
    durations like `7d` / `24h` / `30m`.
- `revitcli score --history <duration>` — per-day score time series
  (LAST snapshot of each day) with new / resolved / unchanged columns;
  unchanged behaviour when `--history` is not set.

### Added — v1.7 CI (complete)

- `revitcli check --output sarif` — SARIF 2.1.0 output suitable for
  `github/codeql-action/upload-sarif`. Element identity sits in
  `logicalLocations` and a `properties` bag (`revitElementId`,
  `revitCategory`, `revitParameter`, `revitCurrentValue`, `documentPath`),
  not in `physicalLocation` (Revit elements aren't files).
- `revitcli check --output pr-comment` — Markdown table sized for a PR
  comment, severity-grouped, truncated at 50 rows.
- `--report` extension inference now also recognizes `.sarif` and routes to
  the new writer.
- `revitcli ci doctor` — detect the running CI provider (GitHub Actions /
  GitLab / Jenkins / Azure DevOps / Travis / generic) via env vars and
  print a ready-to-paste workflow snippet. Exits 0 (informational).
- `revitcli check` now fires a webhook to `defaults.notify` (HTTPS-only,
  private/loopback hosts rejected) on completion, with payload
  `{ event: "check", name, passed, failed, suppressed, severityFailed,
  timestamp, profilePath }`. Best-effort: failure does not affect the
  check exit code. Mirrors the existing publish webhook.
- New composite GitHub Action at `.github/actions/revitcli-check/` — drops
  into any GitHub workflow to install the CLI, run `check --output sarif`,
  and upload to Code Scanning. See `docs/ci/github-actions.md`.

### Added — v1.9 profile governance (foundation)

- `revitcli profile` command cluster:
  - `profile validate [--profile PATH]` — schema + reference-completeness
    + dead-rule checks. Reports `error` / `warning` / `info` lines; exits
    1 only on `error`.
  - `profile show --resolve [--profile PATH] [--output yaml|json]` —
    walks the `extends` chain via `ProfileResolver` and prints the
    merged effective profile, with a header comment listing the chain.
  - `profile diff <a> <b> [--output table|json|markdown]` — structural
    diff (added / removed / changed) under `checks.`, `publish.`,
    `presets.`, `defaults.` keys.

### Added — MCP adapter (side track, unchanged)

- `revitcli mcp serve` — Model Context Protocol stdio server (spec
  `2024-11-05`). Exposes 3 read-only tools: `status`, `query`, `audit`.
  Use in `claude_desktop_config.json` as
  `"revitcli": { "command": "revitcli", "args": ["mcp", "serve"] }`. Write
  tools (`set` / `import` / `fix` / `rollback`) deferred pending a safety
  review of LLM-driven model writes.

### Added — post-audit follow-ups

- `revitcli export --dry-run` now actually parses: validates inputs and resolves
  sheet/view selectors but writes zero files; previously the README documented
  the flag while the parser rejected it.

### Fixed — post-audit follow-ups

- `revitcli doctor` now honors the `Revit<year>InstallDir` env var (Autodesk
  convention used by the addin csproj) when looking for `RevitAPI.dll`, so
  installs at non-default locations no longer trigger a false `FAIL`. The
  diagnostic message reports the path actually checked.

### Internal — repo hygiene and security hardening (no user-facing API changes)

- **Build**: centralized version (`Directory.Build.props` → `RevitCliVersion`) and
  central NuGet management (`Directory.Packages.props`). `TreatWarningsAsErrors`
  now enabled across all projects.
- **Add-in security**: API token now derives from `RandomNumberGenerator` (32 random
  bytes, hex) instead of `Guid.NewGuid()`. `~/.revitcli/server.json` is ACL-locked
  to the current Windows user (FullControl only, inheritance disabled) on write.
- **Add-in robustness**: `RemoveServerInfo` no longer leaks foreign server entries
  (`&&` → `||` ownership check). Port-bind retry, atomic write, and run-task
  observation now log narrowly-typed exceptions to stderr instead of swallowing
  them silently. `Thread.Sleep` retain-loop kept (already async-safe outside the
  Revit UI thread).
- **Add-in `/api/export`**: path traversal check now uses `Path.GetFullPath`
  canonicalization + `StartsWith(userProfile)` instead of a literal `..`
  substring search. Validated against 8 attack/legit cases.
- **Add-in `set`**: display→internal unit conversion failures now surface as
  `400` with a clear message instead of silently writing the raw value.
- **CLI HTTP**: default `HttpClient.Timeout` is 30s (override via
  `REVITCLI_HTTP_TIMEOUT_SECONDS`, clamped 1-3600). Previously implicit 100s.
- **CLI**: bare catches in `RevitClient.DiscoverServerUrl`, `CliConfig.Load`,
  and `JournalLogger.Log` narrowed and surfaced to stderr.
- **CLI**: shared `ServerInfo.DefaultPort` constant removes 3-way port
  hardcoding between addin, client, and config.
- **CI**: actions pinned to commit SHA. CLI `ci.yml` now matrices on
  ubuntu-latest × windows-latest. `ci-addin.yml` triggers on PR (paths
  filter) plus a daily 02:00 cron, no longer dispatch-only. `release.yml`
  drops `continue-on-error: true` from per-Revit-year add-in builds.
- **Tests**: `Fix_Apply_FailsWhenBaselineCannotBeSavedAndSkipsSet` reworked
  to use a cross-platform invalid baseline path (was Windows-only via `<>`).

## [1.5.0] - 2026-04-26

### Added

- `revitcli fix` for dry-run and apply of profile-driven parameter fixes.
- `revitcli rollback` for journal-scoped restoration of fix changes.
- Structured `AuditIssue` metadata for fix planning.
- `setParam` and `renameByPattern` strategies.

### Out of scope

- Delete, geometry, family editing, and cross-document fixes.

## [1.3.0] - 2026-04-24

Model-as-Code Phase 3 — `revitcli import FILE.csv`. Closes the loop: snapshot
(P1) reads the model, publish --since (P2) re-exports only what changed, and
import (P3) writes parameter values back from CSV in batched transactions.

### Added — CSV import

- **`revitcli import FILE.csv`** — declarative bulk parameter writeback.
  Required: `--category` (e.g. `doors`, `墙`), `--match-by` (e.g. `Mark`).
  Optional: `--map "col:Param,col2:Param2"` (default identity), `--dry-run`,
  `--on-missing error|warn|skip` (default `warn`),
  `--on-duplicate error|first|all` (default `error`),
  `--encoding utf-8|gbk|auto` (default `auto`),
  `--batch-size N` (default 100, max 1000).
  Exit codes: 0 (success / dry-run / no rows), 1 (setup / parse / IO / fatal
  miss-or-dup), 2 (some groups failed at write time).
- **`Output/CsvParser`** — RFC 4180 parser with auto encoding detection:
  BOM (UTF-8 / UTF-16 LE / UTF-16 BE) → strict UTF-8 → GBK fallback. Required
  because Excel-exported Chinese CSV is GBK by default. Handles quoted values,
  escaped doubled-quotes, embedded commas/newlines, CRLF or LF.
- **`Output/CsvMapping`** — parses `--map` into a column→param dict;
  unspecified columns default to identity; `--match-by` column excluded from
  writes. Throws on malformed pairs or unknown columns.
- **`Output/ImportPlanner`** — pure-C# matching + grouping algorithm. Indexes
  Revit elements by trimmed `--match-by` value (case-sensitive ordinal),
  classifies CSV rows into Misses / Duplicates / Skipped per `--on-missing`
  and `--on-duplicate`, groups final writes by `(param, value)` so each
  unique pair becomes one batched `SetRequest`. Last-write-wins on repeated
  `(element, param)` with one warning per conflicting key.
- **`Commands/ImportCommand`** — orchestrator that wires CSV → mapping →
  query → planner → batched `SetParameter`. Single code path (TTY and pipe
  both produce plain text — `import` output is mostly counts).
- **`tests/RevitCli.Tests/TestInit.cs`** — `[ModuleInitializer]` registers
  `CodePagesEncodingProvider` once at test-assembly load so test code that
  builds GBK input via `Encoding.GetEncoding("gbk")` works without per-class
  fixture boilerplate.
- **Shell completions for import** — bash, zsh, and PowerShell tab-complete
  `--match-by`, `--map`, `--on-missing`, `--on-duplicate`, `--encoding`, plus
  static value lists for the latter three.

### Changed

- `src/RevitCli/Program.cs`: registers `CodePagesEncodingProvider` at startup
  so `Encoding.GetEncoding("gbk")` works on .NET 8 / Linux for production use.
- `src/RevitCli/RevitCli.csproj`: adds `System.Text.Encoding.CodePages 9.0.0`.
- `src/RevitCli/Commands/CliCommandCatalog.cs`: registers `import` in
  `TopLevelCommands`, `CreateRootCommand`, and `InteractiveHelpEntries`.

### Backward compatibility

- No changes to existing commands, profiles, DTOs, or addin endpoints.
- `import` reuses the existing `/api/elements` (query) and `/api/elements/set`
  (write) endpoints — **no addin upgrade required**. Users on v1.2.0 addin
  - v1.3.0 CLI gain `import` immediately.

### Test count

- 42 new facts across 4 new files + 3 completions assertions in the existing
  `CompletionsCommandTests`. CLI test suite total: **253** (was 211 after P2).
- New facts: `CsvParserTests` × 12, `CsvMappingTests` × 6,
  `ImportPlannerTests` × 12, `ImportCommandTests` × 12.

### End-to-end verification (manual — Windows + Revit 2026)

To verify on a live Revit:

1. Open a model with at least 3 doors; set `Mark` = `W01`, `W02`, `W03`.
2. Write `doors.csv` (UTF-8):
   ```
   Mark,锁具型号
   W01,YALE-500
   W02,YALE-700
   W03,YALE-500
   ```
3. `revitcli import doors.csv --category doors --match-by Mark --dry-run` →
   expect `groups=2, elementWrites=3` and per-group preview.
4. `revitcli import doors.csv --category doors --match-by Mark` →
   expect `Modified 3 element-parameter pair(s) across 2 group(s)`.
5. `revitcli query doors` → confirm `锁具型号` populated per CSV.
6. Re-save the same CSV from Excel as GBK and re-run; expect
   `encoding=gbk` line in plan summary and identical write outcome.

### Known Carry-forward

- Items already noted in v1.2.0 (Revit 2024 build compat for new `ElementId`
  ctor unverified, `--verbose` / `--severity` unimplemented, no progress
  signal during snapshot, README.md still v1.0.0 lineage).
- `import` writes only existing parameters: if the CSV references a Revit
  parameter that does not exist on the target element, the addin's `set`
  returns success with `Affected = 0` for that element. Treat 0 affected as
  a soft signal; surfacing this distinctly is a future enhancement.
- No interactive Spectre.Console table rendering for `import` results — TTY
  and pipe both emit plain text. Acceptable because `import` output is
  primarily counts.
- Duplicate CSV column names (e.g. two columns both named `Lock`) produce
  last-column-wins behavior with a warning. Users should rename their CSV
  headers if they hit this.

Spec: [docs/superpowers/specs/2026-04-23-model-as-code-design.md](docs/superpowers/specs/2026-04-23-model-as-code-design.md)
Plan: [docs/superpowers/plans/2026-04-24-import-csv.md](docs/superpowers/plans/2026-04-24-import-csv.md)

## [1.2.0] - 2026-04-23

Model-as-Code Phase 2 — incremental publish. Pairs `publish --since` with the
snapshot/diff infrastructure from v1.1.0 so a 50-sheet project can re-export
only the 3 sheets that changed, not the whole set.

### Added

- **`revitcli publish --since SNAPSHOT`** — diff a baseline snapshot against
  the current model and narrow each export preset's sheet selector to only the
  changed sheets. Options: `--since-mode content|meta` (default content),
  `--update-baseline` (rewrite the baseline on successful publish).
- **Profile `publish.<pipeline>.incremental: true`** — enable incremental
  publish by default. Baseline path defaults to `.revitcli/last-publish.json`
  and can be overridden with `publish.<pipeline>.baselinePath: <path>`.
  `sinceMode: content|meta` picks the diff granularity.
- **`SnapshotSheet.ContentHash`** — now populated (empty in v1.1.0). For each
  sheet, hashes its `MetaHash` + the element hashes of every non-type element
  in scope of each placed view. Skipped in `--summary-only` mode.
- **`SnapshotHasher.HashSheetContent`** — stable hash helper for sheet
  content, shared between CLI and addin.
- **`BaselineManager`** — atomic read/write of baseline snapshot files
  (tmp-then-rename via `File.Move(overwrite: true)`), used by publish to
  persist the post-publish state.
- **`SinceMode` enum** — content vs meta; used by `SnapshotDiffer.Diff`'s new
  optional `sinceMode` parameter.
- **Shell completions for publish** — `--since`, `--since-mode`, and
  `--update-baseline` now tab-complete in bash, zsh, and PowerShell.

### Changed

- **`SnapshotDiffer.Diff`** — new signature accepts `SinceMode sinceMode =
SinceMode.Meta`. Existing callers (v1.1.0 `revitcli diff` command) keep
  MetaHash-only behavior; new P2 call sites pass `SinceMode.Content`
  explicitly. A P1 baseline (empty ContentHash) falls back to MetaHash
  comparison automatically — no schema bump, no forced baseline rebuild.
- **Publish receipts / webhook payloads** now include `incremental` (bool)
  and `changedSheets` (int) fields when `--since` or `incremental: true` is
  active.

### Backward compatibility

- `schemaVersion` stays at `1`. A v1.1.0 baseline diffs cleanly against a
  v1.2.0 snapshot (content mode gracefully degrades to meta for sheets where
  either side has empty ContentHash).
- `revitcli diff` command output format unchanged.
- `revitcli snapshot` output is byte-identical for sheet MetaHash; only
  `contentHash` field transitions from `""` to real hashes.

### Known Carry-forward

- ContentHash does not honor Visibility/Graphics overrides — hiding a wall
  via V/G won't change `ContentHash`. This is intentional for performance;
  documented on `SnapshotSheet.ContentHash`.
- No progress signal during snapshot. Typical 100-sheet model should complete
  in <30s; if you hit this limit, open an issue.
- `--verbose` on `snapshot` and `--severity` on `diff` still unimplemented
  (carry-forward from v1.1.0).
- Revit 2024 (`-p:RevitYear=2024`, net48) build compat for the new
  `new ElementId(long)` call in `ComputeSheetContentHash` not verified —
  all existing call sites in the codebase use the same pattern and were
  presumed compiling in 2024 builds; needs a controller-driven test before
  a 2024 release. Revit 2026 verified end-to-end.

### End-to-end verification

Built on a real Revit 2026 model (9 walls, 9 doors, 2 windows, 16 schedules,
1 sheet added for this test). Snapshot writes non-empty contentHash. No-change
incremental publish prints "no sheets changed". After renaming the test
sheet, incremental publish correctly narrows the export to just that one
sheet.

Spec: [docs/superpowers/specs/2026-04-23-model-as-code-design.md](docs/superpowers/specs/2026-04-23-model-as-code-design.md)
Plan: [docs/superpowers/plans/2026-04-23-publish-since-incremental.md](docs/superpowers/plans/2026-04-23-publish-since-incremental.md)

## [1.1.0] - 2026-04-23

Model-as-Code Phase 1 — snapshot + diff infrastructure. This lays the
foundation for incremental publish (v1.2) and CSV import (v1.3).

### Added

- **`revitcli snapshot`** — capture the model's semantic state as JSON:
  elements grouped by category, sheets, schedules, and a counts summary.
  Options: `--output FILE`, `--categories LIST`, `--no-sheets`,
  `--no-schedules`, `--summary-only` (fast path, counts only).
- **`revitcli diff FROM TO`** — diff two snapshot JSONs. Formats:
  `table` (default), `json`, `markdown`. Options: `--report FILE`
  (format inferred from `.md`/`.json` extension), `--categories LIST`,
  `--max-rows N`. Errors on schema version mismatch; warns on
  DocumentPath mismatch.
- **Shared DTOs** (`shared/RevitCli.Shared/`):
  `ModelSnapshot`, `SnapshotRequest`, `SnapshotDiff` with every property
  carrying `[JsonPropertyName("camelCase")]` attributes for stable wire
  format.
- **`SnapshotHasher`** — pure static helper producing stable 16-char
  SHA256 hashes for elements, sheet metadata, and schedules. Sorts
  parameter keys Ordinally and escapes `\n`, `\`, `|` in values to
  prevent collisions.
- **`SnapshotDiffer`** — pure C# diff algorithm. Elements keyed by Id
  within each category, sheets by Number, schedules by Id. Defensively
  dedupes via `GroupBy().First()` so malformed input produces a clean
  diff instead of a raw `ArgumentException`.
- **`DiffRenderer`** — renders diffs as table / json / markdown with
  truncation at `--max-rows` and pipe-escape protection for Markdown
  table cells.
- **Addin endpoint** — `POST /api/snapshot` via `SnapshotController`
  wired into `RealRevitOperations.CaptureSnapshotAsync` (runs on the
  Revit main thread through `RevitBridge`). Full Revit API traversal:
  `FilteredElementCollector.OfCategory(...)` for elements,
  `OfClass(typeof(ViewSheet))` for sheets,
  `OfClass(typeof(ViewSchedule))` for schedules with body-cell hash.
- **48 new tests** across shared/CLI layers: DTO roundtrip, hash
  stability, diff algorithm edge cases, renderer formatting, CLI
  commands, and HTTP client wiring.

### Deferred to Phase 2 (v1.2)

- `SnapshotSheet.ContentHash` — view-level element aggregation for
  "sheet really changed?" detection. Currently left as empty string.
- `revitcli publish --since SNAPSHOT` — incremental re-export of only
  sheets whose content changed since the baseline.
- Profile `publish.incremental: true` flag + baseline auto-management.

### Known Carry-forward

- Addin test project (`tests/RevitCli.Addin.Tests/`) has pre-existing
  compile errors against Revit 2026 API (`UIApplication` reference
  missing in test csproj). Not caused by this release; flagged for a
  separate `chore: restore addin tests` commit.
- `--verbose` flag on the `snapshot` command is specified in the design
  doc but not yet implemented.
- `--severity added|removed|modified|all` flag on the `diff` command is
  specified in the design doc but not yet implemented. Workaround:
  filter by category with `--categories` or post-process the JSON
  output.
- `snapshot` / `diff` do not yet appear in the interactive REPL help
  listing (`CliCommandCatalog.InteractiveHelpEntries`) or in the
  shell-completion option-per-command blocks. Top-level command
  completion works.

Spec: [docs/superpowers/specs/2026-04-23-model-as-code-design.md](docs/superpowers/specs/2026-04-23-model-as-code-design.md)
Plan: [docs/superpowers/plans/2026-04-23-snapshot-and-diff.md](docs/superpowers/plans/2026-04-23-snapshot-and-diff.md)

## [1.0.0] - 2026-04-04

RevitCli v1.0 — production-ready local BIMOps runner for Revit teams.

### Added

- **`revitcli init`** — interactive profile creation from 3 starter templates
- **`revitcli score`** — model health score 0-100 with weighted rules and letter grade
- **`revitcli coverage`** — parameter fill rates by category with visual bar chart
- **Publish receipts** — `.revitcli/receipts/<pipeline>-<timestamp>.json` with profile hash, user, machine
- **15 commands total**: status, query, set, audit, export, check, publish, init, score, coverage, config, doctor, batch, completions, interactive

### Changed

- PDF export: no sheets + no views → actionable error instead of exporting all views
- Starter profiles embedded in CLI package (init works from installed tool)
- Publish receipt includes profileHash for auto-discovered profiles

### Production Readiness

- 122 tests (contract tests, integration tests, validation tests)
- 10 built-in audit rules + 2 profile-driven checks
- Documentation website with 5 pages
- 3 starter profiles (architectural, interior, general)
- Supports Revit 2024/2025/2026
- Install scripts, GitHub Actions release workflow
- Output contracts locked (JSON, HTML, exit codes, webhook payload)

## [0.5.0] - 2026-04-04

### Added

- **Documentation website** (GitHub Pages, dark theme)
  - Quick Start guide, 3 core workflows, profile reference, troubleshooting
  - Live at https://xiaodream551-a11y.github.io/revitcli/

- **3 starter profiles** (`profiles/`)
  - `architectural-issue.yml` — room data, sheet completeness, pre-issue gate
  - `interior-room-data.yml` — room metadata, naming, FM handover
  - `general-publish.yml` — health checks, DWG/PDF/IFC pipelines

- **Doctor onboarding** — quickstart guidance when no profile detected
- **34 new tests** (86 → 122) — check command, journal, diff engine, output contracts, profile validation

### Changed

- Profile inheritance: `defaults.Notify` now properly inherited from parent
- Error messages are actionable (suggest fixes, list available options)
- `set --ids-from <file>` for Windows pipe workaround
- Malformed `--ids-from` input rejected with clear error (not silent partial)
- Publish suggests 'revitcli doctor' on connection failure

### Fixed

- Placeholder contract tests replaced with real assertions
- ParseIds tests exercise actual parser path

## [0.4.0] - 2026-04-04

### Added

- **Pipe composability**
  - `set --stdin`: read element IDs from stdin (JSON array or one per line)
  - `set --ids-from <file>`: read element IDs from JSON file (Windows-friendly)
  - `SetRequest.ElementIds`: batch element ID list for pipe workflows
  - Strict parsing with clear error messages on malformed input

- **Webhook notifications**
  - `defaults.notify` in `.revitcli.yml`: HTTPS URL to POST results
  - Fires after `check` and `publish` with status, counts, timestamp
  - HTTPS-only for security, non-blocking (errors to stderr)

- **Operation journal**
  - `.revitcli/journal.jsonl`: append-only log of `set` and `publish` operations
  - Records: action, parameters, affected count, user, timestamp
  - Both interactive and non-interactive paths log consistently

### Known Issues

- `--stdin` pipe does not work reliably with PowerShell + `dotnet run`; use `--ids-from` instead
- `--ids-from` has a serialization issue with the add-in API; use category+filter or --id for now

## [0.3.0] - 2026-04-04

### Added

- **6 new audit rules** (total: 10 built-in + 2 profile-driven)
  - `views-not-on-sheets` — printable views/schedules not placed on sheets
  - `imported-dwg` — imported (not linked) CAD files
  - `in-place-families` — in-place families that should be loadable
  - `duplicate-room-numbers` — rooms sharing the same number
  - `room-metadata` — rooms missing number or using default name
  - `sheets-missing-info` — sheets with no number or empty content

- **Check result diff engine**
  - Auto-saves results to `.revitcli/results/` after each check run
  - Compares against previous run: reports new, resolved, unchanged issues
  - `--no-save` flag to skip storage (used by publish precheck)

### Changed

- **Naming rule rewritten**: replaced broad regex with prefix-based detection
  using known Revit default view prefixes (English + Chinese + German + French).
  System names like "标高 1" / "Level 1" are now whitelisted and no longer flagged.
- `views-not-on-sheets` and `sheets-missing-info` account for `ScheduleSheetInstance`
  placements (schedules on sheets are not false positives)
- `audit --list` output generated from rule registry (no more stale hardcoded strings)
- Diff output only appears in table format (JSON/HTML remain clean for CI parsers)
- Result storage wrapped in try/catch (I/O failures don't break audit output)

## [0.2.0] - 2026-04-04

### Added

- **Project Profiles** (`.revitcli.yml`)
  - Named check sets with audit rules, required parameter checks, naming patterns
  - Named export presets (format, sheets/views, output directory)
  - Named publish pipelines with precheck gates
  - Single-parent inheritance via `extends` with cycle detection
  - Validation of `failOn` and `severity` values at load time

- **`revitcli check`** — Run project checks from profile
  - Sends all checks in single request to add-in (fast batch execution)
  - `--output table|json|html` for different consumers
  - `--report <file>` to save reports (format inferred from extension)
  - Exit code based on `failOn` (error/warning) for CI gating
  - Suppression/waiver system: by rule, category, parameter, element IDs, with expiry dates

- **`revitcli publish`** — Run export pipelines from profile
  - Optional precheck gate (runs a check set first)
  - Sequential export preset execution
  - `--dry-run` support
  - Output paths resolved relative to profile file

- **Check Report Renderer**
  - Table (plain text), JSON (CI), HTML (dark mode with summary cards)
  - All formats display suppressed issue count

- **Server-side audit extensions**
  - `required-parameter`: batch check per category with duplicate-aware parameter scan
  - `naming-pattern`: custom regex patterns for views, sheets, or any category

- **Multi-version Revit support**
  - Dual TFM: `net48` (Revit 2024) + `net8.0-windows` (Revit 2025/2026)
  - `RevitYear` build parameter with per-year output directories
  - Element IDs widened from `int` to `long` (64-bit ElementId since Revit 2024)

- **Capability/version model**
  - `status` reports `revitYear`, `addinVersion`, and `capabilities` list
  - CLI displays add-in version and capabilities

- **Installer**
  - `install.ps1`: auto-detects Revit years, per-year add-in deployment, PATH setup
  - `uninstall.ps1`: multi-year manifest removal, optional `-Purge`
  - `release.yml`: GitHub Actions builds per Revit year, packages ZIP with checksum

- **`doctor`** now displays detected `.revitcli.yml` profile info

### Changed

- Release notes derive supported Revit years from actual build outcomes
- Profile inheritance documented as full-object replacement (not deep merge)

## [0.1.0] - 2026-04-04

### Added

- **Real Revit API Integration (Revit 2026)**
  - `status` — Returns actual Revit version and active document info
  - `query --id` — Fetch real elements by ElementId
  - `query <category> --filter` — Category collection with typed filter matching, unit conversion, duplicate parameter handling
  - `set` — Parameter modification with Transaction safety, type coercion (String/Integer/Double/ElementId), `--dry-run` with full validation, all-or-nothing semantics
  - `audit` — 4 real rules: `naming`, `room-bounds`, `level-consistency`, `unplaced-rooms`
  - `export` — DWG/PDF per-view/sheet export with wildcard matching, IFC whole-model export

- **Add-in Architecture**
  - `IExternalApplication` + `ExternalEvent` bridge for safe main-thread access
  - `RealRevitOperations` with real Revit API calls
  - Exception mapping in controllers (ArgumentException->400, InvalidOperationException->409)
  - Transaction commit status verification
  - Export return value checking

- **CLI Enhancements**
  - `--views` option for export command
  - Category aliases in English + Chinese (walls/墙, doors/门, etc.)
  - Duplicate parameter disambiguation via `[N]` suffix in both query and set

### Changed

- Removed `clash` audit rule (placeholder; deferred to future release)
- Controllers now return structured JSON errors instead of HTTP 500

## [0.1.0-alpha] - 2026-04-02

### Added

- **CLI Commands (10)**
  - `revitcli status` — Check Revit plugin connection status
  - `revitcli query` — Query elements by category, filter, or ID with table/JSON/CSV output
  - `revitcli export` — Batch export sheets as DWG, PDF, or IFC with progress bar
  - `revitcli set` — Modify element parameters with `--dry-run` preview
  - `revitcli audit` — Run model checking rules
  - `revitcli config` — View and modify CLI configuration (`config show` / `config set`)
  - `revitcli doctor` — Diagnose setup issues and connection problems
  - `revitcli completions` — Generate shell completion scripts (bash/zsh/PowerShell)
  - `revitcli interactive` — Interactive REPL mode (`-i` shortcut)
  - `revitcli batch` — Execute commands from a JSON file

- **Revit Add-in**
  - Embedded HTTP server (EmbedIO) with REST API
  - `IRevitOperations` interface separating business logic from HTTP handlers
  - Port fallback mechanism (tries 10 ports if default is occupied)
  - Server discovery via `~/.revitcli/server.json` with PID validation

- **Developer Experience**
  - Spectre.Console colored output with automatic TTY detection
  - Pipe-friendly plain text fallback when stdout is redirected
  - `--version` and `--verbose` global options
  - Configuration system (`~/.revitcli/config.json`)
  - Non-zero exit codes on command errors
  - Packaged as .NET global tool (`dotnet tool install --global RevitCli`)
  - GitHub Actions CI (build + test on push/PR)
  - NuGet auto-publish workflow on tag push
  - 86 unit, integration, and protocol tests
