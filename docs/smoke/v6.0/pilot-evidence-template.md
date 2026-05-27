# RevitCli v6.0 Office Rollout Pilot Evidence Packet

Use this packet only for controlled project-copy pilots. It is no production
support claim, not permission to mutate a central model, and not a substitute
for private office notes.

## Scope

Create a new public-safe packet with
`release pilot scaffold --pilot-id v6-pilot-2026-office-copy-01 --output json`
before collecting private office evidence. Before adding the packet to
`docs/smoke/v6.0/office-rollout-status.json`, run
`release pilot validate --path docs/smoke/v6.0/v6-pilot-2026-office-copy-01.md --output json`
and keep the result free of errors. Use
`release pilot register --pilot-id v6-pilot-2026-office-copy-01 --path docs/smoke/v6.0/v6-pilot-2026-office-copy-01.md --output json`
for a dry-run status update, then repeat with `--yes` only after private
evidence review is complete. Use
`release pilot status --output json` to confirm remaining office pilots,
registered packet validation, per-pilot `missingEvidence`, aggregate
`missingEvidenceSummary`, `evidenceCompleteOfficePilotCount`,
`remainingEvidenceCompleteOfficePilotCount`, and the no-production-support
boundary after registration. Status and claim outputs must include
machine-readable `nextActions` so the remaining pilot intake steps can be
scripted. After the minimum completed pilot threshold is satisfied and private
review is complete, run
`release pilot claim --output json` first as a dry-run, then repeat with
`--yes` only to write the office rollout completion claim. The dry-run claim
result must include machine-readable `claimBlockers` before any completion
write. Add
`--production-support` only after the private support review approves it.

- Pilot identifier:
- Date/time:
- Commit:
- CLI version:
- Installed add-in version:
- Live add-in version:
- Revit year/build:
- Machine class:
- Public model identifier:
- Project class:
- Model type: sample / synthetic / project copy / linked-workshared copy
- Profile identifier:
- Operator role:

## Required Commands

Attach public-safe paths or identifiers for:

- `doctor --check-version 2026 --output json`
- `status --output json`
- `workbench verify --contract workbench-contract.v2 --dir . --output json`
- `release verify --strict --output json`
- `ledger query --source ledger --output json`
- `ledger validate --source ledger --output json`
- `ledger stats --source ledger --analytics-snapshot .revitcli/analytics/ledger-stats.json --output json`
- `ledger timeline --source ledger --analytics-snapshot .revitcli/analytics/ledger-timeline.json --output json`
- `journal verify --output json`

## Live Operation Evidence

For each approved live operation, record:

- Command:
- Dry-run artifact identifier:
- Apply artifact identifier:
- Receipt identifier:
- Ledger operation identifier:
- Replay command, when applicable:
- Rollback result:
- Final verification command:
- Failures:
- Safe retry status:
- Remediation attempted:

## User Review

- Time saved:
- False positives:
- Confusing output:
- Missing evidence:
- Trust blockers:
- Go-forward decision:
- Follow-up owner:
- BIM manager signoff:
- Project-copy owner signoff:
- Support ticket review:
- Multi-user rollout postmortem:

## Completion Threshold

- Machine-readable rollout status:
  `docs/smoke/v6.0/office-rollout-status.json`
- Minimum office pilots: 2-3 completed office pilots before any v6.0 office
  rollout completion claim.
- Each completed pilot listed in the rollout status must include per-pilot
  evidence flags for the required commands, rollback, user review, signoffs,
  support review, and postmortem.
- Each packet's `Pilot identifier` value must match the registered
  `completedPilots[*].pilotId`.
- Each completed pilot's `evidencePacketPath` must be a public-safe
  repo-relative Markdown path under `docs/smoke/v6.0/`; local absolute paths,
  backslashes, drive letters, and parent traversal are not valid release
  evidence.
- Each completed office pilot must attach the required command evidence, live
  operation evidence, user review, BIM manager signoff, project-copy owner
  signoff, support ticket review, and multi-user rollout postmortem.

## Public Boundary

Keep raw machine names, model paths, receipt paths, package paths, and client or
project names in private pilot notes only. Public release notes should use
stable identifiers such as `v6-pilot-2026-office-copy-01`.

Boundary summary: no SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, no database runtime, no central production model mutation, and no production support claim without completed office rollout pilots.
