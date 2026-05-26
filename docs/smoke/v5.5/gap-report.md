# RevitCli v5.5 View and Coordination Hygiene Gap Report

v5.5 View and Coordination Hygiene is proceeding as an audit-first portable
hardening slice. It keeps view, link, workset, and phase risks visible from the
terminal before expanding any production write claim.

## Current Status

| Scope | Status | Evidence |
| --- | --- | --- |
| View standards audit | portable verified | `views audit` reports template, browser, and naming issues without writing. |
| View template planning | portable verified | `views template-apply --dry-run` freezes view ids, old/new template ids, and placed-view counts. |
| View clone planning | portable verified | `views clone-set --dry-run` carries a placed-view rollback guard before cloned views can be deleted. |
| Link audit | portable verified | `links audit` reports path, loaded-state, and coordinate fingerprint drift without writing. |
| Link repair planning | portable verified | `links repair --dry-run` is path/load only; no coordinate moves are planned. |
| Model map audit | portable verified | `model map-check` reports workset and phase drift without writing. |
| Model map planning | portable verified | `model map-fix --dry-run` records workset/phase old/new values and write-precheck evidence. |
| Worksharing locks | not live verified | Requires a controlled workshared model with locked worksets and concurrent users. |
| Coordinate repair | deferred | v5.5 does not move coordinates; coordinate drift remains audit-only. |
| Journal verification | not live verified | `journal verify` evidence is required after any approved coordination apply smoke. |

## Gate

Before v5.5 can be called production-pilot ready, record:

- A controlled Revit 2026 view smoke with placed source views, template changes,
  and clone rollback review.
- A controlled link smoke proving `links audit` reports coordinate drift while
  `links repair` remains path/load only with no coordinate moves.
- A controlled workshared model smoke for `model map-fix` write-precheck
  behavior under worksharing locks.
- Receipt, rollback, and `journal verify` evidence for any approved apply path.
- A pilot postmortem from a BIM coordinator using project copies, not a central
  production model.

v5.5 does not introduce SaaS, MCP orchestration, dashboard-central workflow, or built-in LLM runtime.
External agents may call the commands, but the contract remains
terminal-first, local-first, dry-run first, and human-approved.
