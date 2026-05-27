# Team Pilot

Use this recipe to prepare a local team pilot pack without uploading models or
delegating decisions to hidden automation.

```powershell
revitcli doctor --check-version 2026 --output json
revitcli status --output json
revitcli standards validate --output markdown
revitcli workbench verify --contract workbench-contract.v2 --dir . --output json
revitcli workbench handoff --dir . --output markdown
revitcli ledger query --source ledger --output json
revitcli ledger validate --source ledger --output json
revitcli ledger stats --source ledger --analytics-snapshot .revitcli/analytics/ledger-stats.json --output json
revitcli ledger timeline --source ledger --analytics-snapshot .revitcli/analytics/ledger-timeline.json --output json
revitcli journal verify --output json
revitcli release verify --strict --output json
```

Keep the pilot evidence local. Attach the `doctor` JSON, workbench handoff,
standards validation, receipt/journal paths, Revit year, installer notes, and
any supportable error reports to the pilot postmortem.

For v6.0 office rollout pilots, also fill
`docs/smoke/v6.0/pilot-evidence-template.md`. That packet adds ledger query,
ledger validation, analytics snapshot, rollback, and user-review evidence for
controlled project-copy pilots without making a production support claim. Start
each packet with `revitcli release pilot scaffold --pilot-id <public-id>` and
check it with `revitcli release pilot validate --path docs/smoke/v6.0/<public-id>.md`
before listing it in `office-rollout-status.json`; use
`revitcli release pilot register --pilot-id <public-id> --path docs/smoke/v6.0/<public-id>.md`
for the dry-run status update and repeat with `--yes` only after private
review. Inspect register nextActions before rerunning with `--yes` or moving
back to validation/status checks. Use `revitcli release pilot status --output json` to check the
completed/remaining office pilot count, registered packet validation, and
per-pilot `missingEvidence` plus aggregate `missingEvidenceSummary` flags.
Compare `completedOfficePilotCount` with `evidenceCompleteOfficePilotCount`
and `remainingEvidenceCompleteOfficePilotCount` before any completion claim.
After the threshold is met, run `revitcli release pilot claim --output json`
as a dry-run and inspect `claimBlockers` and `nextActions` before any `--yes`
completion write.
Do not claim office rollout completion until 2-3 completed office pilots have
BIM manager signoff, project-copy owner signoff, support ticket review, and
multi-user rollout postmortems. Keep the packet `Pilot identifier` identical
to the registered pilot id.
