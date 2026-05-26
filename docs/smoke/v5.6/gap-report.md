# RevitCli v5.6 Team Pilot Pack Gap Report

v5.6 Team Pilot Pack makes RevitCli easier for BIM managers and IT to pilot
without changing the product boundary. It stays local-first, terminal-first,
dry-run first, and policy-file driven.

| Scope | Status | Evidence |
| --- | --- | --- |
| Installer bootstrap | portable documented | Pilot checklist starts with installer evidence and `doctor --output json`; timed office installs are not live verified. |
| Doctor support report | portable documented | `doctor` output is the first support artifact for setup, version, connection, and add-in mismatch reports. |
| Team policy files | portable verified | `profiles/team-pilot/.revitcli/team-policy.yml` records install years, required commands, and local-only boundaries. |
| Receipt retention | portable verified | Policy requires `.revitcli/receipts`, `.revitcli/workflows/receipts`, and `.revitcli/journal.jsonl` retention. |
| Training handoff | documented | The pilot recipe and playbook provide training steps for BIM managers, coordinators, and IT. |
| Supportable error reports | documented | Pilot postmortems must include command, output, working directory, model copy, Revit year, receipt/journal path, and remediation. |
| Office pilots | not live verified | Two to three office pilots and installation postmortems remain required before production support claims. |

No SaaS, no MCP, no dashboard-central workflow, and no built-in LLM runtime behavior is introduced.
A local dashboard may only be a viewer for receipts/reports and is
not part of the v5.6 team pilot gate.
