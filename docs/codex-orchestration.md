# Codex auto-iteration loop — quickstart

This repo supports two Codex workflows:

- `/goal <objective>` for long interactive work where the top-level Codex
  session can coordinate 1-3 bounded subagents without carrying every
  detail in one context.
- `scripts/codex tick` / `scripts/codex loop` for audited automation where
  each tick advances exactly one feature-plan checkbox with scope
  enforcement, rescue caps, and deterministic next-step.

The detailed contract is in [`.codex/CONTRACT.md`](../.codex/CONTRACT.md);
this page is the 5-minute orientation.

## TL;DR

```bash
# one-time setup (recommended)
git config core.hooksPath scripts/git-hooks

# long interactive work
/goal "implement the next bounded RevitCli feature"

# advance one audited checkbox
scripts/codex tick

# run continuously until done / HALT / safety cap
scripts/codex loop

# pause / resume / inspect
scripts/codex halt
scripts/codex resume
scripts/codex status
```

Do **not** run upstream `codex` interactively against this repo — the
write-capable project agents will refuse when called directly. Use a
top-level `/goal` session for interactive work, or use `scripts/codex`
for the tick loop.

## What's in `.codex/`

| Path | Purpose | Tracked? |
|---|---|---|
| `config.toml` | agent registry; `default_agent = "coordinator"` | yes |
| `agents/*.toml` | one file per agent role | yes |
| `prompts/*.md` | per-role prompt templates fed to each codex_call | yes |
| `features/*.md` | durable work plans, one per milestone command | yes |
| `state/*` | ephemeral run-time state (current-agent, logs, `/goal` delegation, etc.) | gitignored |
| `HALT` | sentinel file; presence pauses the loop | gitignored |
| `CONTRACT.md` | the rules every agent must follow | yes |

## The tick: 10 steps per call

```
0. HALT check
1. git status / diff snapshot to .codex/state/
2. Find active feature (in-progress > planning > new-via-architect)
3. orchestrator (gpt-5.5/high) writes next-action.json
4. invoke worker (spark / cli / addin / test-author) for ONE checkbox
5. scope enforcement — revert out-of-scope edits via git checkout --
6. dotnet build + dotnet test; if red, rescue-diagnostician (cap 2/feature)
7. code-reviewer reads HEAD diff, writes last-review.md
8. mark checkbox [x] in feature md (or [!] if review says needs-revision)
9. git commit (with `Verify:` line)
10. if all checkboxes done → status: done; next tick spawns next feature
```

Each step is one chunk in `scripts/codex-tick.sh`; read it for the
exact behavior.

## The agents at a glance

| Agent | Model | When it runs |
|---|---|---|
| coordinator | gpt-5.4-mini | DEFAULT — runs the tick and reports |
| orchestrator | gpt-5.5/high | every tick, step 3 |
| feature-architect | gpt-5.5/high | when no feature is active |
| spark-drafter | gpt-5.3-codex-spark | scaffolding, single-file CLI surface |
| cli-implementer | gpt-5.3-codex | multi-file CLI: receipts, rollback, plan |
| addin-implementer | gpt-5.3-codex | Revit add-in handlers/services |
| test-author | gpt-5.4-mini | tests for already-implemented surface |
| rescue-diagnostician | gpt-5.5/high | build/tests red (cap 2) |
| code-reviewer | gpt-5.4/medium | review-only final pass, writes `.codex/state/last-review.md` |
| repo-explorer | gpt-5.3-codex-spark | ad-hoc read-only lookups |

## `/goal` delegation

When a `/goal` session works on this repo, it should:

1. Do a short read-only grounding pass.
2. Spawn 1-3 subagents only for bounded sidecar work that can run in
   parallel with the local critical path.
3. Give write-capable subagents disjoint ownership scopes such as
   `src/RevitCli/`, `src/RevitCli.Addin/`, tests, docs, or shared DTOs.
4. Keep `.codex/state/goal-delegation.md` current with objective,
   spawned agents, ownership, pending results, integrated results, and
   next local action.
5. Reread that state file after resume or context compaction before
   spawning more agents or finalizing.
6. Close completed subagents after their results are integrated or no
   longer needed, then record the closure in the delegation state file.

The read-only `repo-explorer` and review-only `code-reviewer` project
agents are open tools and may run from `/goal`. Write-capable project
agents remain protected by the tick gate; `/goal` should coordinate
implementation through its top-level subagent mechanism rather than
calling those `.codex/agents/*.toml` sessions directly.

## When it goes wrong

| Symptom | Where to look |
|---|---|
| Write-capable project agent refused with "tick-runner" message | Working as intended — see `.codex/state/current-agent.txt`. Use `/goal` from the top-level session or run `scripts/codex tick`. |
| `/goal` says "exploration sub-agent refused" | Fixed in stage 1 — `repo-explorer` and `code-reviewer` are now open tools. If still seeing this, you may be running stale agent configs; re-pull `.codex/agents/`. |
| `/goal` forgot which subagents are running | Inspect `.codex/state/goal-delegation.md`; update it before spawning more work. |
| commit-msg hook says "lacks Tick: marker" | Either let the tick produce the commit, or prefix the subject with `manual-override: <reason>`. Last-resort bypass: `git commit --no-verify`, which leaves no visible hook marker. |
| Feature stuck in `blocked` | `.codex/state/needs-attention.md` + the feature md's Notes section |
| Same checkbox keeps failing | Inspect `.codex/state/last-build.log` + `last-test.log`; rescue counter at `.codex/state/rescue-count-<slug>.txt` (delete to reset) |
| Worker keeps making out-of-scope edits | Tighten `scope-paths` in the feature md; the tick reverts them automatically anyway |
| Pre-commit hook blocks a legitimate commit | Either widen `scope-paths`, use `manual-override:` if appropriate, or use emergency `git commit --no-verify` with no visible hook marker |

## When you want to bypass

Add the literal phrase `TICK BYPASS` to your prompt when invoking an
agent manually. The turn-validity check passes, the rest of the
contract still applies (no [Skip], no out-of-scope edits, etc.).

Use this for prompt-tuning sessions, not for "just this once" feature
work.

## Adding a new feature plan

Two options:

1. **Let the architect do it**: with no `in-progress` feature, run
   `scripts/codex tick`. The architect agent reads
   `docs/roadmap-v4.1-v5.md` and writes the next feature md.
2. **Write it yourself**: see the format in
   `.codex/agents/feature-architect.toml` or copy
   `.codex/features/v4.1-sheet-issue-meta.md` as a template.

Either way, the next `scripts/codex tick` will start work on it.

## Adding a new agent

1. Drop a `.codex/agents/<name>.toml` modelled on an existing worker.
2. Copy the **Turn validity check** + **Single-turn mandate** blocks
   verbatim, replacing the agent name.
3. Register it in `.codex/config.toml`:
   ```toml
   [agents.<name>]
   description = "..."
   config_file = "agents/<name>.toml"
   ```
4. Reference it from `feature-architect.toml` if it should appear as
   an assignee in future plans.
