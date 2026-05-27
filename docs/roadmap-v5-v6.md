# RevitCli v5.0 -> v6.0 Product and Technical Plan

> Status: executable planning baseline, created 2026-05-22.
> Predecessors: [roadmap-2026q4-v4.md](roadmap-2026q4-v4.md) and
> [roadmap-v4.1-v5.md](roadmap-v4.1-v5.md).
> North star: keep RevitCli terminal-first, local-first, deterministic,
> dry-run first, receipt-backed, rollbackable, and auditable.

## Strategic Correction

The current repository is ahead of the older v4.1 -> v5 blueprint in command
surface. `main` already contains v4.1 issue-closure automation surfaces for
`issue preflight/diff/package`, `workbench-contract.v2`, sheet issue metadata,
sheet renumbering, numbering, schedules, views, links, model mapping, delivery
plans, receipts, and rollback handoff.

That does not make the product production-ready. It means v5.0 must stop
expanding the command map and convert the existing contract surface into a
trusted release workflow on controlled real Revit models.

v5.0 is therefore:

**Issue Closure Workbench Quality Release**

It proves that an issue-day workflow can answer:

- Can this model/package be issued?
- If not, which blocker is actionable?
- If a command writes, exactly what changed?
- Can the change be rolled back without guessing?
- Can the package be tied back to manifests, receipts, journal evidence, and
  the model state used to produce it?

## Current Capability Inventory

### Product Layer

| Area | Current capability | v5.0 judgment |
| --- | --- | --- |
| Core CLI | `status`, `doctor`, `query`, `inspect`, `set`, `config`, `batch`, completions, interactive mode | Mature enough; do not widen for v5.0. |
| Safe mutation | `set/import/fix` plans, `plan show/apply`, `rollback`, thresholds, receipts | Core moat; needs real Revit conflict and rollback proof. |
| Issue closure | `issue preflight`, `issue diff`, `issue package`, `workbench-contract.v2` | Make this the v5.0 spine. |
| Sheets | `sheets verify`, `issue-meta`, `renumber`, `index` | Highest ROI write surface; harden first. |
| Schedules | `schedule list/export/create`, `schedules ensure/batch-export/compare` | v5.0 should trust export/compare before structural ensure writes. |
| Deliverables | `deliverables plan/list/stats/verify/bundle`, delivery manifests | Harden package traceability and receipt linking. |
| Workflows | `workflow validate/simulate/review/run/suggest/receipts` | Useful orchestration surface; v5.0 should use it for demo recipes, not hide logic. |
| Standards | `standards install/validate`, family rules, profiles | Keep as local standards runtime seed; production packs come in v5.x. |
| Views/links/model map | `views audit/template-apply/clone-set`, `links audit/repair`, `model map-check/map-fix` | Keep mostly experimental or read-only until v5.5. |
| Dashboard | Local optional dashboard | Viewer only; not a v5.0 product center. |

### Technical Layer

| Area | Current capability | v5.0 judgment |
| --- | --- | --- |
| Architecture | CLI -> HTTP REST -> Revit Add-in -> ExternalEvent -> Revit API | Correct; preserve local Revit API boundary. |
| Add-in targets | Revit 2024 `net48`, 2025/2026 `net8.0-windows` | Claims require per-year smoke evidence. |
| Shared DTOs | `RevitCli.Shared` owns API contracts and snapshots | Keep contracts small and cross-platform. |
| Plan model | Frozen ids, old/new values, command metadata, review commands | Add stronger deterministic plan-hash and model identity evidence where missing. |
| Receipt model | Plan receipts, delivery receipts, workflow receipts, journal signatures | v5.0 must normalize what is required for issue-day acceptance. |
| Rollback model | Receipt rollback actions and current-value conflict checks | Need failure injection and live apply/rollback proof. |
| Error handling | Command-specific failures exist | Need taxonomy: connection, Revit context, profile, permission, worksharing, transaction, export, receipt, rollback. |
| Codex integration | Visible shell commands, examples, workbench contract | Keep external; no built-in LLM, hidden prompt layer, or MCP roadmap. |

### v5.0 Receipt Acceptance Rules

Every approved mutation receipt must be good enough for an issue-day reviewer
to answer what was approved, where it ran, what it changed, and when rollback
must stop.

- Required receipt context: `schemaVersion`, command/action, operation, plan
  path, plan hash, operator, machine, timestamp, model path or document name,
  Revit/document version when available, affected ids, and success/failure.
- The plan hash is not decorative: receipt rollback validates the referenced
  plan file and fails on a missing file or hash mismatch.
- Required rollback action evidence: element id, parameter/field, old value,
  new value, and source operation. Missing, unsupported, or mismatched action
  sources fail before any Revit write.
- Required numbering provenance: room/mark receipts include rule path,
  plan action count, skipped count, and deterministic sort evidence where the
  plan has an explicit sort list.
- Rollback stop condition: current-value conflict fails by default for
  `set`, `import`, sheet issue, sheet renumber, room numbering, and mark
  assignment actions. Rollback must not overwrite third-party edits.

### Validation Layer

| Area | Current evidence | v5.0 gap |
| --- | --- | --- |
| Portable tests | Recent commits record `dotnet test tests/RevitCli.Tests/` | Keep as baseline, broaden for v5.0 P0 commands. |
| Workbench contract | v4/v5-compatible verifier surfaces exist | Verify v2 stays green after hardening. |
| Revit 2026 dry-run smoke | v4 workbench dry-run evidence exists with 16 steps and 0 failures | Not enough for write trust. |
| Revit 2026 apply smoke | Earlier `set` apply/restore exists; v4 dry-run did not run apply on discovered field | Need controlled RVT issue-day apply/rollback. |
| Revit 2024/2025 | Source-build support exists | Need actual smoke or explicit "not live verified" claim. |
| User validation | No durable pilot evidence in repo | Need live-adjacent pilots on real project copies. |

## v5.0 Scope

### Must Harden

| Workstream | Commands/modules | Acceptance standard |
| --- | --- | --- |
| Issue closure contract | `issue preflight`, `issue diff`, `issue package`, `workbench verify --contract workbench-contract.v2` | Hidden-mutation checks, schema compatibility, package traceability, and journal-signature evidence are deterministic and tested. |
| Sheet issue writes | `sheets issue-meta`, `sheets renumber`, `plan apply`, `rollback` | Controlled RVT can dry-run, apply, create receipt, rollback, and verify journal without unknown model state. |
| Package traceability | `deliverables plan/bundle/verify`, `issue package` | Every file in a bundle maps to manifest entry, child receipt where applicable, per-file SHA256 evidence, bundle hash, and issue-package receipt. |
| Schedule release | `schedules batch-export`, `schedules compare` | CSV export manifest and diff report include paths, byte counts, and before/after SHA256 evidence for issue-day review. |
| Receipt/rollback integrity | `PlanCommand`, `RollbackCommand`, receipt schemas | Current-value conflict handling blocks unsafe rollback; tampered or incomplete receipts fail clearly. |
| Real Revit matrix | install, doctor, status, preflight, dry-run, apply, rollback, package | `scripts/smoke-revit.ps1 -V5IssueClosure` runs the gated lane; approved sheet writes require `-V5ApplySheetIssue`, package writes require `-V5WriteIssuePackage`, and each Revit year needs a dedicated evidence file or explicit gap report entry. |
| Error taxonomy | CLI output, JSON schemas, docs | Every P0 failure maps to a code, explanation, remediation, and safe-retry rule. |
| Demo and pilot pack | docs, sample profiles, scripts | A BIM manager can run the issue-day demo without reading source. |

### Should Defer

| Deferred area | Why |
| --- | --- |
| Production-grade view cloning/template writes | High Revit display-state risk; harden after sheet/schedule closure. |
| Production-grade link repair/model map fix | Worksharing, coordinates, and permissions are high-risk. Keep read-only first. |
| Large-scale numbering apply as default | Numbering rules vary heavily by office and discipline. |
| Dashboard-centered workflow | Conflicts with CLI-first and local artifact contract. |
| MCP or built-in LLM | External agents can call visible commands; core stays deterministic. |
| SaaS/cloud sync/ACC replacement | Weakens local-first trust and expands the security surface too early. |
| Full openBIM/IFC/BCF platform | Good v6+ bridge, not a v5.0 readiness dependency. |

## v5.x Roadmap

| Version | Theme | Goal | Pain | Commands/modules | Acceptance and test strategy |
| --- | --- | --- | --- | --- | --- |
| v5.0 | Issue Closure Workbench Quality Release | Prove issue-day closure on real RVT copies | Trust gap between contract surface and production writes | `issue`, `sheets`, `deliverables`, `schedules export/compare`, `journal`, `rollback` | Portable tests, workbench v2, controlled Revit smoke, fault injection, 3 live-adjacent pilots. |
| v5.1 | Sheet Release Control | Make sheet metadata operations production-grade | Titleblock date/code/number mistakes cause issue errors | `sheets verify`, `issue-meta`, `renumber`, `index` | 100/300/1000 sheet fixtures, titleblock map fixtures, stale value conflicts, Revit 2024/2025/2026 smoke. |
| v5.2 | Schedule and Deliverable Closure | Make schedules and delivery packages release artifacts | CSV/schedule/package drift is hard to prove | `schedules batch-export/compare`, `deliverables bundle/verify`, `issue package` | Manifest/hash/receipt trace, baseline/current diff, path failure injection, receipt tamper tests. |
| v5.3 | Numbering Controlled Apply | Move room and mark numbering from plan surface to field-tested apply | Room/door/window numbering is frequent but office-specific | `rooms renumber`, `marks assign/verify`, numbering specs | Portable hardening now covers deterministic reserved/hold gaps, duplicate-target failure, max-change and receipt identity gates; live pilot models remain required before production readiness. |
| v5.4 | Standards Runtime Pack | Make office standards executable and portable | Standards live in PDFs and templates, not repeatable workflows | `standards install/validate`, profiles, workflows, schedule specs, sheet maps | Canonical `profiles/office-standard` pack, offline install/validate gate, sheet map and numbering rule file checks; timed bootstrap and BIM manager pilot remain required. |
| v5.5 | View and Coordination Hygiene | Harden read-only and limited safe repairs | View/link/workset issues block issue-day confidence | `views audit`, limited `views template-apply`, `views clone-set`, `links audit`, limited `links repair`, `model map-check`, `model map-fix` | Audit first, plan-only portable gate, no coordinate moves, worksharing lock tests, placed-view rollback guards. |
| v5.6 | Team Pilot Pack | Make RevitCli deployable by BIM managers and IT | Installing, training, log retention, profile updates | installer, doctor, policy files, receipt retention, demo docs | Two to three office pilots, installation postmortems, supportable error reports. |

## v6.0 Positioning

v6.0 should become:

**BIM Release OS**

Subtitle:

**Local BIMOps Workbench for Revit**

Technical kernel:

**Revit Model Operations Ledger**

The product promise is not "AI replaces architects" and not "natural language
controls Revit." The promise is that repetitive, high-consequence BIM
production operations become previewable, approvable, auditable, and
rollbackable local workflows.

### v5.0 vs v6.0 Boundary

| Boundary | v5.0 | v6.0 |
| --- | --- | --- |
| Primary proof | One issue-day workflow on real RVT copies | Repeatable BIMOps workbench across projects and standards packs |
| User | BIM manager/coordinator comfortable with terminal | BIM teams with repeatable local standards and release routines |
| Core artifact | Plan, receipt, rollback, issue package, journal evidence | Operations ledger, project memory, standards runtime, workflow registry |
| UI | CLI and Markdown/JSON reports | CLI first, optional local TUI/dashboard viewer |
| Agent role | External shell operator only | External shell operator plus richer command contracts |
| Cloud/SaaS | Out of scope | Still optional bridge, not the source of truth |

### v6.0 Capabilities

- Standards runtime: profile schema for sheet naming, issue metadata,
  schedule specs, view templates, link policy, family policy, export policy,
  receipt retention, and rollback requirements.
- Operations ledger: append-only local JSONL records plus queryable receipts,
  journal entries, signatures, bundle hashes, model identity, affected element
  evidence, and rollback pointers, with approved `set` writes recorded after
  successful Revit API mutation, default replay previews, and bounded set-only
  replay apply before broader live ledger replay/apply.
- Project memory layer: local history of issues, failing checks, repeated
  commands, recurring sheet/schedule problems, release evidence, and read-only
  `ledger timeline` day/hour buckets plus local `ledger stats --project` and
  `ledger timeline --project` aggregation over explicitly supplied project
  roots.
- Workflow registry: governed local recipes indexed by `workflow registry` with
  declared inputs, outputs, read/write scope, risk level, dry-run command,
  approval command, rollback support, receipt schema, and acceptance evidence.
- Optional viewer: local dashboard/TUI for receipts and trends only. CLI,
  JSON, Markdown, receipts, and journals remain first-class.
- Bridges: export to BCF/CSV/JSON/IFC-related metadata when useful, without
  becoming a cloud collaboration platform.

### v6.0 Contract Baseline

The first v6.0 slice is contract-first, then staged runtime. It adds
`docs/v6-local-bimops-contract.md` and `docs/smoke/v6.0/gap-report.md`, gates
them through `workbench verify --contract workbench-contract.v2` and
`release verify --strict`, introduces `ledger append` as a local JSONL runtime
path, and adds `ledger replay` as a preview-by-default local plan over appended
records with bounded approved set-only apply.

Done means:

- The Local BIMOps Workbench command spine is visible as terminal commands.
- Revit Model Operations Ledger records have required local fields for command,
  args, model identity, plan hash, deterministic receipt, receipt hash, journal
  path, rollback pointer, status, checks, and artifacts.
- `ledger append` can dry-run by default and append local
  `ledger-operation.v1` records only with `--yes`; `ledger query`, `ledger
  replay`, `ledger validate`, `ledger stats`, and `ledger timeline` can read
  the `ledger` source.
- Approved `set` writes append `ledger-operation.v1` records with
  `command=set`, `action=set`, target scope, affected count, and available
  affected ids plus best-effort live `modelIdentity`/`modelPath`/`revitVersion` after
  successful Revit API writes; dry-run and `--plan-output` paths do not append
  ledger records.
- `ledger replay` emits `ledger-replay.v1` with `dryRun=true`,
  per-step `canApply` and block-reason evidence by default; `--source ledger
  --action set --apply --yes` is limited to successful approved `set --yes`
  records with frozen affected ids, appends a non-replayable
  `ledger.replay.apply` audit row after successful apply, and has controlled
  Revit 2026 live smoke evidence.
- Revit 2026 live add-in identity plus set dry-run/apply/confirm/restore is
  proven on the controlled `revit_cli.rvt` smoke model, alongside bounded
  replay apply, and recorded in
  `docs/smoke/v6.0/revit2026-live-addin.md`.
- Dry-run first, explicit approval, hidden-mutation bans, deterministic output,
  rollback preconditions, current-value conflicts, and audit trail invariants
  are documented before implementation.
- Standards runtime, project memory, and workflow registry remain local files
  and reports.
- Ledger stats/timeline can persist and read back local single-file analytics
  snapshots, and `ledger analytics` packages the stats/timeline pair as a
  local evidence bundle; no analytics service or database runtime is
  introduced.
- `docs/smoke/v6.0/pilot-evidence-template.md` defines the office rollout
  evidence packet needed for controlled project-copy pilots, without claiming
  production support before 2-3 completed office pilots have command evidence,
  BIM manager signoff, project-copy owner signoff, support review, and
  multi-user rollout postmortems, with each packet `Pilot identifier` matching
  the registered pilot id. `release pilot scaffold` creates the
  public-safe per-pilot Markdown scaffold without changing rollout status, and
  `release pilot validate` checks a packet before it is listed as completed
  evidence. `release pilot register` dry-runs or writes the completed-pilot
  status entry only after validation and reports register nextActions for
  validation failures, dry-run writes, and post-write status checks. `release
  pilot status` reports current
  completed/remaining office pilots, validates registered evidence packets,
  and surfaces per-pilot `missingEvidence` plus aggregate
  `missingEvidenceSummary` flags without changing rollout status. It reports
  `evidenceCompleteOfficePilotCount` and
  `remainingEvidenceCompleteOfficePilotCount` separately from the registered
  status count so incomplete pilot entries do not inflate evidence-complete
  rollout progress, and provides `nextActions` for the remaining pilot intake
  path.
  `release pilot claim` is the dry-run-first explicit completion claim path
  with machine-readable `claimBlockers` and `nextActions` until validated
  completed pilots reach the threshold.
- `docs/smoke/v6.0/office-rollout-status.json` records the current
  machine-readable status as 0 completed office pilots and no office rollout
  completion claim. Future completion requires per-pilot evidence flags, not
  only a count.
- `docs/smoke/v6.0/local-controlled-pilot-20260525.md` records one local
  controlled pilot packet for the `revit_cli.rvt` smoke model with
  doctor/status, workbench/release, ledger analytics, and journal verification
  evidence; it is not office rollout completion.
- No SaaS, MCP adapter, built-in LLM parser, or dashboard-central state is
  introduced by this baseline.
- Broader live ledger replay/apply runtime, database-backed analytics,
  analytics service runtime, broader live Revit ledger integration, and office
  rollout pilots remain not live verified until later evidence exists.

## Post-v6 Blueprint

### Reality Route

These are credible after v5/v6 prove real Revit trust:

- BCF/IDS/IFC metadata bridges for issue and standards exchange.
- Receipt and journal analytics across projects.
- Local dashboard/TUI as a viewer for ledger, packages, and trends.
- pyRevit/Dynamo bridge as import/export of governed recipes, not hidden
  script execution.
- Team profile policy, retention, and installation management.

### Long-Range Imagination

These should not be promised until the reality route works:

- Cross-BIM ModelOps CLI across Revit, IFC, Navisworks, Tekla, Archicad, or
  other authoring/coordination tools.
- BIM knowledge graph from sheets, rooms, views, links, issues, packages,
  receipts, approvals, and standards.
- Operations replay: issue-day workflow playback for audit and onboarding.
- AI drawing/PDF review that creates review reports, not silent model writes.
- BIM GitHub Actions/Terraform-like flow: `plan`, `apply`, `diff`,
  `release verify`, and signed artifacts.
- Multi-agent BIM studio where agents assist with checks and reports while
  humans approve risky writes.

## 90-Day Execution Plan

| Week | Milestone | Concrete output |
| --- | --- | --- |
| 1 | Scope freeze | This roadmap, ordered `.codex/features/v5.0-*` plans, README status update, archived stale v4.1 feature state. |
| 2 | Readiness definitions | Receipt requirements, error taxonomy, P0 command list, model fixture selection, sample issue profile. |
| 3 | Sheet apply chain audit | Tests for `sheets issue-meta/renumber -> plan apply -> receipt -> rollback`; identify Revit-only gaps. |
| 4 | Controlled Revit 2026 sheet smoke | Apply/rollback on disposable controlled model with `-V5IssueClosure -V5ApplySheetIssue`; journal verify; evidence packet. |
| 5 | Issue package traceability | Harden child receipt, manifest, per-file SHA256, bundle hash, journal signature, and dry-run no-write checks. |
| 6 | Schedule/export closure | Harden schedule export manifest and compare reports with path, byte-count, and before/after SHA256 evidence. |
| 7 | Fault injection | Missing profiles, missing receipts, tampered receipts, path permission failure, stale values, worksharing lock where available; portable workbench self-checks cover the non-Revit cases. |
| 8 | Multi-version install smoke | Revit 2024/2025/2026 install/doctor/status matrix or explicit gap report. |
| 9 | Demo pack | Issue-day walkthrough, scripts, sample profile, rollback demo, expected evidence. |
| 10 | User interviews | 6-8 interviews across BIM manager, coordinator, sheet team, PM, and IT/standards owner. |
| 11 | Live-adjacent pilots | Three real project copies; no central production model mutation. |
| 12 | v5.0 RC gate | `docs/v5-rc-readiness.md`, RC/no-go report, known experimental commands list, strict release gate, v5.1-v5.3 feature-plan refresh. |

## Risk Register

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Real Revit validation gap | Contract looks good but live writes fail | Controlled RVT apply/rollback is a release gate. |
| Windows/Revit add-in drift | CLI and live add-in mismatch | Doctor/version smoke before every live claim; restart after staged installs. |
| Cross-version compatibility | 2026 evidence gets overgeneralized | Separate 2024/2025/2026 status and docs. |
| Scope creep | v5.0 becomes another feature expansion | Freeze v5.0 to issue closure quality. |
| Over-early SaaS/dashboard | Product loses local-first trust | Dashboard stays optional viewer; no SaaS mainline. |
| Over-early LLM/MCP | Deterministic safety gets bypassed | External agents call visible shell commands only. |
| Rollback safety | A rollback overwrites third-party changes | Current-value conflicts fail by default. |
| Worksharing complexity | Locks, central/local files, permissions cause unknown states | v5.0 uses project copies and conservative support claims. |
| User trust | BIM users do not believe automatic writes | Dry-run-first demos, receipts, rollback, and pilot postmortems. |
| Feature-plan drift | Tick loop follows stale checklists | Archive superseded plans and keep v5 feature files ordered. |

## Required Artifacts

- `docs/roadmap-v5-v6.md` — this executable plan.
- `docs/v5-demo-and-pilot-playbook.md` — demo script, pilot checklist, and
  user interview guide.
- `.codex/features/v5.0-01-issue-closure-quality.md` — active v5.0 quality
  feature plan.
- `.codex/features/v5.0-02-receipt-rollback-hardening.md` — receipt and
  rollback hardening follow-up.
- `.codex/features/v5.0-03-real-revit-smoke-matrix.md` — Windows/Revit smoke
  evidence plan.
- `.codex/features/v5.0-04-demo-and-pilot-pack.md` — demo, docs, and pilot
  pack.
- `profiles/v5-issue.yml` — sample dry-run-first issue profile.
- `scripts/v5-issue-day-demo.ps1` — dry-run-first issue-day demo script.
- `docs/smoke/v5.0/` — real Revit smoke evidence or explicit per-version gap
  report before any v5.0 live-support claim.
- `docs/v5-rc-readiness.md` — explicit v5.0 RC GO/NO-GO boundary, stable P0
  commands, experimental/deferred command list, and strict release gate.
- README roadmap/status update.
- `.codex/features/v5.0-05-schedule-package-traceability.md` — schedule and
  package traceability hardening.
- `.codex/features/v5.0-06-fault-injection-readiness.md` — portable
  fault-injection semantics and verifier coverage.
- `.codex/features/v5.0-07-rc-readiness-and-boundaries.md` — RC/no-go release
  boundary and stable/experimental scope.
- `.codex/features/v5.1-sheet-release-control.md` — planned-blocked sheet
  release control hardening; entry is gated by v5.0 RC GO evidence.
- `.codex/features/v5.2-schedule-deliverable-closure.md` —
  planned-blocked schedule/package release artifact hardening; entry is gated
  by v5.0 RC GO evidence and v5.1 pilot evidence or an explicit decision.
- `.codex/features/v5.3-numbering-controlled-apply.md` — in-progress
  numbering apply hardening; entry proceeds by explicit written decision while
  v5.1/v5.2 live fixtures remain unclaimed.
- `.codex/features/v5.4-standards-runtime-pack.md` — complete standards
  runtime pack hardening; scope is local/offline install and validation of the
  canonical `profiles/office-standard` pack.
- `.codex/features/v5.5-view-coordination-hygiene.md` — complete
  audit-first view/coordination hardening; scope is placed-view guard,
  no-coordinate link repair, model-map writable probe, and explicit live
  worksharing gaps.
