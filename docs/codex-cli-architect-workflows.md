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
4. Codex CLI reads `.revitcli.yml`, profiles, and local docs.
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
revitcli doctor
revitcli status
revitcli profile simulate issue
revitcli check issue --output table
revitcli publish issue --dry-run
```

```text
把门表导成 CSV,放到 deliverables/tables,导出前先告诉我有哪些 schedules。
```

Expected command path:

```powershell
revitcli inspect schedules
revitcli schedule export --name "Door Schedule" --output csv |
  Set-Content -Encoding utf8 deliverables/tables/doors.csv
```

```text
帮我看这个模型有哪些图纸可以导出,先只列候选,不要导出。
```

Expected command path:

```powershell
revitcli inspect sheets
revitcli inspect sheets --ready-only
revitcli inspect sheets --issues-only
```

```text
帮我查一下为什么 publish 失败,先看最近 journal 和模型健康检查。
```

Expected command path:

```powershell
revitcli journal verify
revitcli check --output table
revitcli history trend --window 14d
```

```text
把所有防火门的 Fire Rating 改成 60min,先预览影响哪些门。
```

Expected command path:

```powershell
revitcli query doors --filter "name contains Fire" --output table
revitcli set doors --filter "name contains Fire" --param "Fire Rating" --value "60min" --dry-run
revitcli set doors --filter "name contains Fire" --param "Fire Rating" --value "60min" --plan-output .revitcli/plans/fire-rating.json
revitcli plan show .revitcli/plans/fire-rating.json
revitcli plan apply .revitcli/plans/fire-rating.json --dry-run
```

## Required RevitCli Improvements

- More `inspect` commands so Codex CLI can discover categories,
  parameters, schedules, and command paths without guessing.
- `inspect sheets` as a CLI-only discovery surface so Codex CLI can
  identify sheets, review issues, key title-block parameters, and export
  candidates before it builds a publish/export plan.
- Stable JSON/table outputs with useful exit codes for `doctor`,
  `status`, `check`, `publish --dry-run`, `schedule list`, and `journal`.
- Plan files for risky writes: generate, show, apply, receipt, rollback.
- Safe-plan slices: `set --plan-output`, `import --plan-output`,
  `plan show`, and `plan apply` with frozen element IDs and receipts.
- Workflow commands for common architect tasks: `pre-issue`,
  `export-package`, `weekly-health`, and `family-cleanup`.
- Clear examples in docs so Codex CLI can map user intent to commands.

## Guardrails

- RevitCli must not embed an LLM or prompt interpreter.
- Codex CLI must not bypass RevitCli safety flags.
- Mutating operations must start with dry-run or plan output.
- High-impact writes require explicit user approval.
- Model files, exports, journals, and keys stay local unless the user
  deliberately copies them elsewhere.
