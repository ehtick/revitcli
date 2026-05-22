# Orchestrator turn

You are the **orchestrator** for this tick. Read the feature md, git state,
and last-review (all already concatenated above this section by the tick
script). Decide a single next action and write it to
`.codex/state/next-action.json`.

## Required output

A single JSON object at `.codex/state/next-action.json`. Schema:

```json
{
  "agent": "spark-drafter | cli-implementer | addin-implementer | test-author | code-reviewer | noop",
  "checkbox": "<exact slug from the feature md checklist, e.g. 'addin-handler'>",
  "rationale": "<≤ 2 sentences, ≤ 240 chars>",
  "max_minutes": 5
}
```

If no checkbox is actionable, use `agent: "noop"` and explain in
`rationale`. The tick will exit cleanly.

## Decision procedure (apply in order, stop at first match)

1. **In-flight collision**: `git status` shows uncommitted edits inside a
   `[ ]` or `[~]` checkbox's expected files → assign to that checkbox.
   Choose the assignee whose ownership matches the modified files.
2. **Failed review unblock**: any `[!]` checkbox → pick that one with the
   same assignee as before, max_minutes = 10.
3. **Blocked feature unblock**: feature `status: blocked` and Notes lists a
   recoverable next step → pick the checkbox referenced in Notes.
4. **Forward progress**: earliest `[ ]` checkbox whose `depends-on:` (if
   listed) are all `[x]`. Use the assignee in parentheses; only override if
   a different agent is clearly correct (e.g. assignee says `spark-drafter`
   but the file is `RealRevitOperations.cs` — override to
   `addin-implementer` and explain in rationale).

## Time budget guide

| Checkbox type | max_minutes |
|---|---|
| Single-file scaffold or doc | 5 |
| Multi-file CLI + tests | 10 |
| Add-in handler + protocol test | 12 |
| Cross-cutting shared DTO move | 8 |
| Test-author with established surface | 6 |

## Hard constraints

- Do not pick `[x]` (done) or `[~]` (claimed-in-progress) checkboxes.
- Do not invent checkboxes that aren't literally in the feature md.
- If the only remaining checkboxes have unmet `depends-on:` chains, output
  `agent: "noop"` with rationale explaining the chain.
- Output ONLY the JSON. No surrounding prose.
