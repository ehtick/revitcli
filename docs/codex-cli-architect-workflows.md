# Codex CLI Architect Workflows

> Goal: architects can ask Codex CLI to use `revitcli` for repetitive
> Revit work, while RevitCli stays deterministic, local, and auditable.

## Positioning

Codex CLI is the conversational terminal operator. RevitCli is the BIM
tool it calls. This keeps natural-language help outside RevitCli while
making the product easy for architects who do not want to memorize every
command.

This is not MCP. There is no server protocol, client registry, or hidden
agent runtime. Codex CLI runs normal shell commands such as
`revitcli check`, `revitcli publish --dry-run`, and
`revitcli schedule export`.

## Operating Model

1. Architect opens Revit with the target model.
2. Architect opens Codex CLI in the project folder.
3. Codex CLI checks setup with `revitcli doctor` and `revitcli status`.
4. Codex CLI reads `.revitcli.yml`, `.revitcli/standards.yml`, profiles,
   workflows, and local docs.
5. Codex CLI runs read-only or dry-run commands first.
6. For writes or exports, Codex CLI summarizes the plan and asks for
   explicit approval before using `--yes` or a non-dry-run command.
7. RevitCli writes receipts, history, and journal entries for audit.

## Architect Prompts

```text
帮我检查这个模型今天能不能出图,只 dry-run,不要写入。
```

Expected command path:

```powershell
revitcli doctor --output json
revitcli status --output json
revitcli standards validate
revitcli profile simulate issue
revitcli workflow init pre-issue
revitcli workflow examples pre-issue
revitcli workflow validate .revitcli/workflows/pre-issue.yml
revitcli workflow simulate .revitcli/workflows/pre-issue.yml
revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run
revitcli workflow receipts --failed-only --output markdown
revitcli sheets verify --output json --issues-only
revitcli check issue --output table
revitcli publish issue --dry-run --output json
```

```text
把门表导成 CSV,放到 deliverables/tables,导出前先告诉我有哪些 schedules。
```

Expected command path:

```powershell
revitcli inspect schedules
revitcli inspect schedules --issues-only --output markdown
revitcli inspect schedules --category Doors --ready-only
revitcli schedule list --output markdown
revitcli schedule export --name "Door Schedule" --output csv |
  Set-Content -Encoding utf8 deliverables/tables/doors.csv
revitcli schedule export --name "Door Schedule" --output markdown
```

```text
帮我确认这个项目有没有满足办公室标准,只做本地校验。
```

Expected command path:

```powershell
revitcli standards install ../office-standards --dry-run
revitcli standards validate
revitcli standards validate --output json
revitcli family validate --rules-from .revitcli/standards.yml
revitcli examples standards
```

```text
帮我清理未使用的族,先写一份可审查报告,不要直接删除。
```

Expected command path:

```powershell
revitcli workflow init family-cleanup
revitcli workflow simulate .revitcli/workflows/family-cleanup.yml
revitcli family ls --unused
revitcli family validate --rules-from .revitcli/standards.yml
revitcli family purge --dry-run --report .revitcli/reports/family-purge.json
revitcli examples family
```

```text
帮我看这个模型有哪些图纸可以导出,先只列候选,不要导出。
```

Expected command path:

```powershell
revitcli inspect sheets
revitcli inspect sheets --ready-only
revitcli inspect sheets --issues-only --output markdown
revitcli sheets verify --output json --issues-only
```

```text
帮我查一下为什么 publish 失败,先看最近 journal 和模型健康检查。
```

Expected command path:

```powershell
revitcli journal stats
revitcli journal review
revitcli journal show --limit 10
revitcli journal verify
revitcli check --output table
revitcli history trend --window 14d
revitcli report weekly --window 14d --output markdown
revitcli workflow suggest --output yaml
```

```text
帮我做发版前预检,不要跑真实 Revit smoke。
```

Expected command path:

```powershell
revitcli release verify --tag v2.3.0
revitcli release verify --tag v2.3.0 --output json
revitcli release verify --tag v2.3.0 --output markdown
revitcli examples release
```

```text
帮我看这周模型变化有没有可疑项,先不要打开 Revit 写任何东西。
```

Expected command path:

```powershell
revitcli history diff @-2 @-1 --review
revitcli history diff @-2 @-1 --review --output json
```

```text
把所有防火门的 Fire Rating 改成 60min,先预览影响哪些门。
```

Expected command path:

```powershell
revitcli inspect params doors
revitcli inspect params doors --name "Fire*" --writable-only --missing-only
revitcli query doors --filter "name contains Fire" --output table
revitcli set doors --filter "name contains Fire" --param "Fire Rating" --value "60min" --dry-run
revitcli set doors --filter "name contains Fire" --param "Fire Rating" --value "60min" --plan-output .revitcli/plans/fire-rating.json
revitcli plan show .revitcli/plans/fire-rating.json
revitcli plan apply .revitcli/plans/fire-rating.json --dry-run
```

## Recipe Templates

Reusable prompt-to-command recipes live under
[`docs/templates/codex-recipes`](templates/codex-recipes/). They are local
documentation templates, not executable hidden logic. Use them when the user
asks for a common BIM task and then run the listed `revitcli` commands
explicitly.

```powershell
revitcli examples recipes
```

## Required RevitCli Improvements

- More `inspect` commands so Codex CLI can discover categories,
  writable parameters, schedules, and command paths without guessing.
- `inspect categories|params|schedules|sheets --output markdown` produces
  handoff-ready discovery notes while preserving JSON for scripts.
- `inspect schedules` supports category/name filters plus `--ready-only`,
  `--empty-only`, and `--issues-only`, so Codex CLI can separate exportable
  schedules from empty or incomplete ones before writing handoff files.
- `schedule list/export --output markdown` provides handoff-ready schedule
  tables while keeping `--output json` and `--output csv` for automation.
- `inspect params` supports `--name`, `--writable-only`, and
  `--missing-only`, so Codex CLI can find candidate parameters for safe
  plan generation instead of scanning every parameter in a category. Writable
  results include sample element IDs and element-scoped `set --dry-run`
  probes for the smallest possible first check.
- `inspect sheets` as a CLI-only discovery surface so Codex CLI can
  identify sheets, review issues, key title-block parameters, and export
  candidates before it builds a publish/export plan.
- `sheets verify` adds a read-only sheet-frame check for duplicate numbers,
  numbering gaps/ranges, required sheet declarations, and minimum placed-view
  counts against `.revitcli/sheets/index.yml`.
- Stable JSON/table outputs with useful exit codes for `doctor`,
  `status`, `check`, `publish --dry-run`, `schedule list`, and `journal`.
  First `status --output json`, `doctor --output json`,
  `check --output json`, `publish --dry-run --output json`,
  `export --dry-run --output json`, `schedule list --output json`, and
  journal `--output json` contract slices shipped for setup, status success,
  check gates, export/publish plans, schedule discovery, journal review, and
  connection-failure output.
  Schedule list/export now also support Markdown handoff output, and export
  rejects unknown `--output` values before contacting Revit.
- Export receipts are written under
  `<outputDir>/.revitcli/receipts/export-*.json` after successful real
  exports; dry-runs never write receipt files.
- Publish receipts include a stable `publish-receipt.v1` schema with
  success, dry-run, preset, command, operator, and machine fields.
- Successful real exports and publishes append `delivery-manifest.v1`
  entries to `.revitcli/deliveries/manifest.jsonl` beside the command's
  delivery root, giving Codex CLI a stable index back to the receipt file.
- `deliverables list`, `deliverables stats`, and `deliverables verify`
  read that manifest locally and can render Markdown handoff notes while
  confirming each entry points to a readable export or publish receipt.
- `deliverables bundle --dry-run --output markdown` previews the receipts
  and output files that will go into a handoff zip; real bundle runs write
  the zip plus a `delivery-bundle-receipt.v1` sidecar.
- `diff --review` and `history diff --review` for deterministic
  anomaly/notable/routine summaries before Codex CLI drills into raw diffs.
- `report weekly` for local Markdown/JSON summaries from history, score,
  diff review, and journal before a human review handoff.
- `standards install` / `standards validate` for local office requirements:
  required profiles, workflows, output paths, schedule templates, and
  built-in family rule ids. Standards manifests now carry `packVersion`
  and `compatibility` metadata so Codex CLI can report pack version,
  supported RevitCli version, supported Revit years, and compatibility notes.
  `--output markdown` produces review notes for standards bootstrap handoff.
- `family validate --rules-from .revitcli/standards.yml` so the standards
  pack controls the reusable family validation rule set.
- Plan files for risky writes: generate, show, apply, receipt, rollback.
- Safe-plan slices: `set --plan-output`, `import --plan-output`,
  `fix --plan-output`, `plan show`, and `plan apply` with frozen element
  IDs or fix actions, receipts, and fix rollback baselines.
- `plan show FILE --output json` emits a stable `plan-summary.v1`
  envelope for Codex CLI approval prompts, including risk level, change
  count, commands, issues, and the original plan payload.
- `plan show FILE --output markdown` emits a handoff-ready review with
  risk, issues, preview rows, and dry-run/apply commands for architect
  approval notes.
- `plan apply FILE --yes` writes a stable `plan-receipt.v1` sidecar with
  the exact apply command, timestamp, operator, machine, affected element
  IDs, model context when available, and fix rollback baseline/journal
  pointers.
- `plan apply` reads profile safety defaults when available:
  `defaults.planMaxChanges` supplies the write cap when `--max-changes` is
  omitted, and `defaults.highImpactChanges` requires
  `--confirm-high-impact` for real writes at or above that threshold.
- Workflow commands for common architect tasks: `pre-issue`,
  `export-package`, `weekly-health`, and `family-cleanup`.
- `workflow examples [template]` lists architect prompts, preview commands,
  approval commands, and acceptance evidence for those workflow templates.
- `family purge --report FILE` writes `family-purge-report.v1` JSON with
  purge candidates, keep-pattern matches, placed/in-place exclusions, safety
  gate state, and Revit purge results for cleanup review.
- `release verify --tag vX.Y.Z` writes table/JSON/Markdown
  `release-verify.v1` evidence for version/tag consistency, release docs,
  Ubuntu CLI/Shared-only CI guardrails, installer markers, and release
  packaging workflow markers.
- Workflow validation/simulation commands support table, JSON, and Markdown
  output so Codex CLI can show declared read-only, dry-run, and mutating
  steps before anything runs.
- `workflow run` with `--dry-run` and `--yes` gates, so approved workflow
  YAML can execute visible RevitCli commands without becoming a shell script.
  `workflow run --dry-run --output markdown` prints a reviewable handoff plan.
- Real `workflow run` executions write `workflow-run-receipt.v1` JSON
  receipts under `.revitcli/workflows/receipts/`, including command
  metadata, operator/machine, step exit codes, and success/failure status.
- `workflow receipts --output markdown` turns saved workflow-run receipts into
  a local handoff table; `--failed-only` narrows review to failed runs.
- `workflow suggest` to print workflow YAML drafts from repeated explicit
  journal command entries. The command never writes the workflow file.
- `journal stats` summarizes actions, categories, users, operators,
  affected element totals, and distinct affected element IDs for local audit
  review before drafting reports or workflows.
- `journal review` turns the same local audit data into a review preset:
  risk buckets, top operators/categories, affected ID samples, and highlighted
  mutating or high-impact entries. Use `--output markdown` for handoff notes.
- `journal show` supports `--action`, `--category`, `--operator`, and
  `--user` filters so Codex CLI can narrow recent audit entries before
  explaining what changed.
- Clear examples and recipe templates in docs so Codex CLI can map user
  intent to commands.

## Guardrails

- RevitCli must not embed an LLM or prompt interpreter.
- Codex CLI must not bypass RevitCli safety flags.
- Mutating operations must start with dry-run or plan output.
- High-impact writes require explicit user approval.
- Model files, exports, journals, and keys stay local unless the user
  deliberately copies them elsewhere.
