# RevitCli Q4 → v4 Future Blueprint

> Period: 2026 Q4 onward
> Status: strategic roadmap, not a locked implementation plan
> Thesis: evolve RevitCli from a local BIMOps runner into an
> agent-native BIM protocol layer, without losing local-first execution,
> auditability, or Revit engineer control.

## 1. Current Baseline

The v1.5 → v2.0 roadmap delivered the BIMOps foundation: fix/rollback,
history, CI outputs, family management, profile governance, and the
dashboard. v2.1 then hardened trust with profile simulation,
multi-version smoke scaffolding, and journal signing.

That makes the next roadmap less about adding isolated commands and more
about turning the existing surface into a repeatable agent workspace.

## 2. Product Principles

- **Local-first by default**: Revit, files, profiles, journals, and
  history remain operator-owned.
- **Agent-native, not agent-exclusive**: every agent feature must still
  be inspectable and useful to a BIM engineer at a terminal.
- **Trust before autonomy**: no workflow should mutate a model without
  preview, journal, rollback path, and verification.
- **Protocols over prompts**: prefer structured outputs, sessions,
  workflows, and replayable logs over natural-language magic.
- **No built-in LLM lock-in**: RevitCli exposes clean interfaces; users
  choose Claude, ChatGPT, Gemini, local models, or no model.

## 3. Milestone Overview

| Milestone | Theme | Outcome |
| --- | --- | --- |
| v2.2 | Release Integrity | v2.1 becomes release-ready across docs, install paths, CI, and live smoke. |
| v2.3 | Agent Output Protocol | Commands produce compact, self-describing `--output agent` payloads. |
| v2.4 | Workflow Playbooks | Repeated BIMOps procedures become versioned, runnable YAML workflows. |
| v3.0 | Agent Workspace | Sessions, selections, journal explain/blame, and dashboard audit views connect. |
| v3.x | Visual + Knowledge Capture | Agents can inspect rendered context and promote repeated work into reusable patterns. |
| v4.0 | BIM Protocol Layer | RevitCli becomes a stable protocol/runtime for agent-assisted BIM operations. |

## 4. v2.2 — Release Integrity

Goal: convert the current `main` branch into a dependable release
candidate.

Scope:

- Generalize `scripts/install.ps1` install-directory overrides for
  Revit 2024 / 2025 / 2026.
- Run and record real smoke results for Revit 2024, 2025, and 2026.
- Add CI templates for `profile simulate`, `journal verify`, and smoke
  matrix dispatch.
- Refresh README, docs site, roadmap links, and release checklist.
- Decide whether dashboard npm audit findings are fixed, suppressed, or
  explicitly documented.

Exit gate:

- CLI tests pass.
- Dashboard `check`, build, BASE_PATH build, and Playwright tests pass.
- At least Revit 2026 live smoke passes; 2024 / 2025 gaps are documented
  before release claims are broadened.

## 5. v2.3 — Agent Output Protocol

Goal: make the CLI cheap and precise for LLM agents to consume.

Scope:

- Add `--output agent` to high-value read commands first:
  `status`, `doctor`, `query`, `check`, `history trend`, `diff`.
- Define a stable envelope:
  `summary`, `schema`, `items`, `warnings`, `nextActions`, `pagination`,
  `provenance`.
- Add compact paging and drill-down patterns so agents avoid dumping
  thousands of elements into context.
- Add tests that assert token-oriented payload size for representative
  large outputs.

Innovation:

- Treat command output as an API for agents, not a prettier JSON mode.
- Include `nextActions` that are safe suggestions, never automatic
  writes.

Feasibility note:

- This is mostly CLI-side transformation; it should avoid Revit API
  expansion until the protocol shape proves useful.

## 6. v2.4 — Workflow Playbooks

Goal: capture repeated BIMOps procedures as versioned workflows.

Scope:

- Add `.revitcli/workflows/*.yml`.
- Add `revitcli workflow validate`, `workflow simulate`, and
  `workflow run`.
- Support ordered steps, conditions, named outputs, failure policy, and
  dry-run propagation.
- Allow workflows to call existing commands only; no new hidden runtime.
- Add CI template for running workflow gates on scheduled or manual
  triggers.

Example:

```yaml
name: pre-issue
steps:
  - run: revitcli profile simulate issue
  - run: revitcli check issue --output sarif
  - run: revitcli history capture --source pre-issue
  - run: revitcli publish issue --dry-run
```

Exit gate:

- Workflow simulation must catch missing profiles, missing presets, and
  commands that cannot run in dry-run mode.

## 7. v3.0 — Agent Workspace

Goal: make agent work stateful, reviewable, and blameable.

Scope:

- Add `revitcli session start/show/note/end`.
- Support stable selection references such as `@last`, `@selection:name`,
  and `@history:<capture>`.
- Expand `journal` from `sign/verify` into
  `show`, `explain`, `blame`, and safe `replay` previews.
- Add dashboard audit views for signed journal status, recent writes,
  session summaries, and rollback availability.

Innovation:

- This becomes "git for BIM operations": not just what changed, but who
  or what agent changed it, why, and how to inspect or reverse it.

Feasibility note:

- Build on existing journal, history, and dashboard data. Avoid real-time
  Revit event capture until session and journal semantics are stable.

## 8. v3.x — Visual + Knowledge Capture

Goal: let agents use BIM visual context and turn repeated operator work
into reusable assets.

Candidate tracks:

- `capture view/sheet/element`: export PNG/base64 visual context from
  Revit views and sheets.
- `diff --review`: produce human-readable risk summaries from snapshot
  diffs and journal context.
- `learn start/stop --suggest`: detect repeated command patterns and
  propose a workflow, profile rule, or fix recipe.
- Cross-project index: answer "which projects use family X?" or "which
  projects violate rule Y?" from local history stores.

Constraint:

- Knowledge capture must always propose, never silently create policy.
  The engineer approves before new workflows or rules are written.

## 9. v4.0 — Agent-Native BIM Protocol Layer

v4.0 is the north-star version. It should not start until v2.3/v2.4/v3.0
prove real usage.

Target outcome:

- RevitCli has a documented agent protocol covering output envelopes,
  sessions, workflows, negotiation, journal provenance, and replay.
- CLI, dashboard, and MCP are three front doors into the same protocol,
  not separate products.
- Teams can version BIM standards, run agent-assisted workflows, review
  model changes, and prove audit integrity without handing model control
  to a SaaS layer.

Possible v4 surfaces:

- `revitcli protocol doctor`: verify an agent integration supports the
  required protocol version.
- `revitcli mcp serve` upgraded from optional wrapper to first-class
  protocol gateway.
- Workflow registry for local/team-approved playbooks.
- Policy packs: signed profile + workflow + journal verification rules.
- Federated, privacy-preserving learning only after local learn/workflow
  adoption is proven.

Non-goals for v4:

- No "AI replaces BIM engineers" positioning.
- No built-in proprietary LLM dependency.
- No ACC/BIM360 clone.
- No uncontrolled autonomous writes.

## 10. Decision Gates

Before starting each major phase:

- **v2.3**: at least one agent workflow shows measurable context
  reduction from `--output agent`.
- **v2.4**: at least three repeated manual procedures are identified and
  can be expressed as workflows.
- **v3.0**: journal signing/verify is used in at least one real project
  or smoke workflow.
- **v4.0**: there is evidence that external agents or contributors want
  a stable protocol, not just ad-hoc CLI commands.

## 11. Immediate Next Actions

1. Execute v2.2 release integrity.
2. Write a short spec for `--output agent` envelope before coding v2.3.
3. Collect 3-5 real BIMOps workflows from actual use, not imagined demos.
4. Keep MCP work thin until the protocol model is stable.
5. Revisit this roadmap after every release and demote ideas that fail
   real operator validation.
