# Codex Recipe Templates

These templates map architect prompts to explicit `revitcli` command paths.
They are documentation only: no hidden runtime, no LLM inside RevitCli, and no
automatic writes. Codex CLI should read the recipe, run read-only or dry-run
commands first, summarize the result, then ask before using `--yes` or any
non-dry-run write/export command.

| Recipe | Use When |
|---|---|
| [pre-issue.md](pre-issue.md) | Check whether a model is ready for issue without writing first |
| [standards-bootstrap.md](standards-bootstrap.md) | Install or validate local office standards |
| [weekly-review.md](weekly-review.md) | Prepare a weekly history/diff/journal summary |
| [parameter-change-plan.md](parameter-change-plan.md) | Plan a risky parameter update without applying it |
| [publish-failure.md](publish-failure.md) | Diagnose a failed publish using journal and check outputs |
| [family-cleanup.md](family-cleanup.md) | Review unused families and purge candidates with a JSON report |
| [release-preflight.md](release-preflight.md) | Check release files and CI guardrails before live smoke |
| [sheet-frame-verify.md](sheet-frame-verify.md) | Verify sheet numbering and required sheet expectations before publish |

For repeated local command sequences, run:

```bash
revitcli workflow examples
revitcli workflow receipts --failed-only --output markdown
revitcli workflow suggest --output yaml
```

Use `workflow examples` to pick a built-in workflow acceptance path. Review
failed workflow receipts before reruns, and review any suggested YAML before
saving it under `.revitcli/workflows/`.
