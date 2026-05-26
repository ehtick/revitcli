# RevitCli Codex contract

This file is the single source of truth for how Codex agents are allowed
to run against this repository. If anything in an agent's
`developer_instructions` ever conflicts with this file, **this file
wins**.

## 1. Supported entry points

| Goal | Command |
|---|---|
| Long interactive goal | `/goal <objective>` in the top-level Codex session |
| Advance one checkbox | `scripts/codex tick`  (alias for `bash scripts/codex-tick.sh`) |
| Run continuously    | `scripts/codex loop`  (`scripts/codex-loop.sh` daemon) |
| Pause               | `scripts/codex halt`  (creates `.codex/HALT`) |
| Resume              | `scripts/codex resume` |
| Status              | `scripts/codex status` |

`/goal` is the preferred interactive entry point for larger work that
benefits from 1-3 coordinated subagents. The tick loop is the preferred
entry point for audited, single-checkbox automation.

Do not invoke write-capable project agents from `.codex/agents/*.toml`
directly outside the tick. They are configured to **refuse** when
`.codex/state/current-agent.txt` does not name them. A top-level
`/goal` session may coordinate subagents, but it must not bypass the
protected tick gate by manually calling write-capable project agents.

## 2. Why the tick gate is a hard rule

The tick script provides four guarantees that direct project-agent
invocations do not provide:

1. **Scope enforcement** — workers can only edit paths declared in the
   active feature md's `scope-paths`. Out-of-scope worker-created edits
   from the current tick are physically reverted (`git checkout --` or
   `git clean -fd --`) before commit. Pre-existing dirty worktree paths
   are preserved instead of reverted. The active feature md itself is the
   control-record exception so workers can declare honest blockers in
   `status:` / Notes.
2. **Rescue caps** — a failing build/test gets at most 2 rescue
   attempts per checkbox before the feature is marked `blocked` and
   flagged for human review. Interactive Codex has no such cap → loops
   forever.
3. **Audit trail** — every commit references a feature + checkbox slug;
   `git log` is auditable. Interactive Codex commits drift away from
   this.
4. **Single-turn isolation** — each tick = a fresh `codex exec` call =
   a fresh agent session. No context drift, no "round 2 forgets the
   rules" pathology.

`/goal` does not replace those guarantees. It is a human-supervised,
top-level coordination mode for tasks where parallel read-only
exploration, test writing, or disjoint implementation sidecars are more
efficient than forcing every decision through the tick.

## 3. `/goal` delegation covenant

When a top-level `/goal` session is working in this repo:

- Do a short read-only grounding pass before delegating.
- Spawn 1-3 subagents only when their tasks are bounded and can run in
  parallel without blocking the immediate local critical path.
- Assign every write-capable subagent a disjoint file/module ownership
  scope and tell it not to revert unrelated edits.
- Use `repo-explorer` for read-only side work and `code-reviewer` for
  review-only side work. Use implementation/test agents only through the
  top-level session's own subagent mechanism, not direct
  `.codex/agents/*.toml` calls.
- Maintain `.codex/state/goal-delegation.md` during long goals. Record
  the objective, spawned agents, ownership, pending results, integrated
  results, and the next local action.
- After resume, context compaction, or a long implementation stretch,
  reread `.codex/state/goal-delegation.md` before spawning more agents
  or finalizing.
- Close completed subagents after their results are integrated or no
  longer needed. Update `.codex/state/goal-delegation.md` so stale agent
  handles are not treated as active work.

If a `/goal` session commits outside the tick loop, use an explicit
`manual-override: <reason>` subject so the commit hook records that it
was not produced by the tick.

## 4. The bypass valve (debug only)

If you genuinely need to invoke a **write-capable** agent manually
(e.g. prompt tuning), include the literal phrase **TICK BYPASS** in
your prompt. The agent will then run despite the missing
`current-agent.txt` check. This is for debugging only — bypassed
runs are not audited.

## 4a. Open low-risk agents

The following low-risk agents may be called from any context — the tick
loop, `/goal`, feature-architect, or a direct manual session — **without**
the `TICK BYPASS` keyword:

| Agent | Why open |
|---|---|
| `repo-explorer` | Read-only sandbox; cannot write files, change git state, or run dotnet |
| `code-reviewer` | Review-only; may write only `.codex/state/last-review.md` or an ad-hoc review file |

For audit, both agents prepend an `[<agent>] invoked` line to their
output. `code-reviewer` writes to `.codex/state/last-review.md` by
default; ad-hoc reviews can be routed to
`.codex/state/ad-hoc-review-<UTC-yyyymmddTHHMMSS>.md` to avoid
contaminating the next tick's orchestrator decision.

## 5. The git hooks (recommended)

```bash
git config core.hooksPath scripts/git-hooks
```

This installs two hooks:

### 5a. `pre-commit` — content-level checks

Rejects:
- Staged paths outside the active feature's `scope-paths`.
- Dishonest test patterns: `[Skip]`, `Assert.True(true)`, trivial
  `Assert.NotNull` as sole assertion, `if (false)` wrappers, empty
  `catch` blocks.
- (Advisory) commit messages without a `Verify:` line.

### 5b. `commit-msg` — origin-level check

Rejects any commit whose message body does **not** contain a
`Tick: agent=<name>` line, **unless** one of these allow-list
conditions is true:

| Allowed | Marker |
|---|---|
| Subject begins with `manual-override:` | warning + audit; commit accepted |
| Subject begins with `Merge `, `Revert `, `fixup! `, `squash! `, `amend! ` | git's own tooling subjects; auto-allowed |
| Body contains `Tick: agent=` (the tick script always adds this) | preferred path |

Bypass with `git commit --no-verify` only for emergencies; it leaves no
explicit hook-bypass marker. Use `manual-override:` instead when you want
the audit trail visible in `git log`.

## 6. Cheat sheet for agents

If you are an agent reading this:

- If you are **write-capable** (orchestrator, feature-architect,
  spark-drafter, cli-implementer, addin-implementer, test-author,
  rescue-diagnostician): your name must match
  `.codex/state/current-agent.txt`, else refuse with the tick-runner
  message. A top-level `/goal` session is an alternative coordinator,
  not permission to run this project-agent session directly. No
  exceptions absent `TICK BYPASS`.
- If you are `repo-explorer` or `code-reviewer`: no `current-agent.txt`
  check. Prepend `[<your-name>] invoked` to your output and proceed.
  `repo-explorer` is read-only. `code-reviewer` may write only the
  configured review file under `.codex/state/`.
- You handle exactly ONE atomic task per invocation. When done, exit.
- Read the active feature md (path in `.codex/state/current-feature.txt`)
  to know your scope.
- Tests are non-optional. Failures are diagnostic. No `[Skip]` ever.
- Never modify `.codex/agents/*.toml` or `.codex/config.toml` from
  within an agent session.
- Commits you produce (via the tick) must carry `Tick: agent=<name>`
  in the body. The `commit-msg` hook will reject otherwise.

## 7. Cheat sheet for humans

- Start a long interactive task: use `/goal <objective>` and let the
  top-level session coordinate 1-3 subagents. Inspect
  `.codex/state/goal-delegation.md` if the session resumes after a long
  break.
- Add a new milestone: write `.codex/features/v4.X-<slug>.md` (or let
  `feature-architect` do it on the next empty tick).
- Pause: `scripts/codex halt`. Resume: `scripts/codex resume`.
- Stuck checkbox: inspect `.codex/state/needs-attention.md`, fix
  manually, reset the rescue counter:
  `rm .codex/state/rescue-count-<checkbox>.txt`.
- Add a new agent: drop a toml under `.codex/agents/`, register in
  `.codex/config.toml`. Include the turn-validity guard at the top
  of its `developer_instructions` (copy from any existing worker).
