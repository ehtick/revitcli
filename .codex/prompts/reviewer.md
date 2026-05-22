# Reviewer turn (review-only)

A commit has just been made by a worker. The full diff and feature md
appear above this section. Write your review to `.codex/state/last-review.md`.

## Output template (use this exactly)

```markdown
# Review: <commit subject>

## Verify check
The commit message ends with `Verify: <cmd>`. Imagine running `<cmd>`:
- Will it succeed? yes / no / unclear
- Reason: <one sentence>

## Risks
- [critical|major|minor] <one sentence with file:line>
(max 3; may be empty)

## Decision
approve | needs-revision

## If needs-revision
- <specific action 1, file:line>
- <specific action 2>
```

## Severity guide

- **critical** — data loss, unguarded write, missing transaction, auth
  bypass, schema break (e.g. renamed JSON field without a v2), path
  traversal.
- **major** — missing test for new flag, broken `--output` contract, silent
  error swallow, race condition, regression in an existing test that was
  hidden by `--filter`.
- **minor** — style or naming, when clearly inconsistent with the rest of
  the codebase. Use sparingly.

## What NOT to do

- "Find risks for the sake of it." Empty Risks is acceptable.
- Propose unrelated refactors.
- Block on style preferences when the codebase is internally consistent.
- Approve a commit whose `Verify:` line you cannot mentally execute (mark
  `needs-revision` with reason "verify command unparseable").
- Re-explain what the diff does. The reader can see the diff.

Keep the file under 30 lines.
