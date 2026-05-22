# .codex/state/

Ephemeral state for the tick engine and long `/goal` sessions. Most
tick-owned files in this directory are overwritten by
`scripts/codex-tick.sh`; **nothing here should be committed** except the
tracked documentation placeholders allowed by `.gitignore`.

| File | Written by | Lifespan |
|---|---|---|
| `git-status.txt` / `git-diff-stat.txt` | tick (step 1) | overwritten each tick |
| `current-feature.txt` | tick (step 2) | overwritten each tick |
| `orchestrator-input.md` | tick (step 3) | overwritten each tick |
| `next-action.json` | orchestrator agent | overwritten each tick |
| `worker-input.md` | tick (step 4) | overwritten each tick |
| `last-build.log` / `last-test.log` | tick (step 6) | overwritten each tick |
| `rescue-input.md` | tick (step 6, on red) | overwritten on red |
| `rescue-count-<checkbox>.txt` | tick (step 6) | persistent until checkbox completes |
| `reviewer-input.md` | tick (step 7) | overwritten each tick |
| `last-review.md` | code-reviewer agent | persists between ticks (input to orchestrator) |
| `last-tick.md` | tick (step 10) | persists between ticks (input to orchestrator) |
| `needs-attention.md` | tick (on blocked) | persists until human clears |
| `goal-delegation.md` | top-level `/goal` session | persists until the goal is complete |

## Control files

- `.codex/HALT` — touch this to pause the loop. The next tick exits
  immediately with code 2. Use `scripts/codex-halt.sh` / `codex-resume.sh`.

## `/goal` delegation state

For long interactive goals, maintain `.codex/state/goal-delegation.md`
with:

- objective
- spawned agents and their ownership scopes
- pending results
- integrated results
- closed agents
- next local action

Reread this file after resume, context compaction, or a long
implementation stretch before spawning more agents or finalizing.

## Inspecting a stuck loop

```bash
# What does the orchestrator think?
cat .codex/state/next-action.json | jq .

# Why is the feature blocked?
cat .codex/state/needs-attention.md
grep -A2 '^## Notes' .codex/features/*.md

# What did the last build complain about?
tail -50 .codex/state/last-build.log

# Reset rescue counter for a stuck checkbox
rm .codex/state/rescue-count-<checkbox>.txt
```
