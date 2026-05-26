# RevitCli v6.0 Issue Spine Portable Smoke

This slice documents the issue-day command spine for the v6.0 Local BIMOps
Workbench:

```bash
revitcli issue preflight --profile .revitcli/issue.yml --output json
revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue-package.zip --dry-run --output json
```

`issue preflight` reads a local issue profile and verifies declared artifacts,
review commands, mutation-plan references, shell-safety rules, and hidden mutation guards. `issue package --dry-run` builds the issue package plan and
traceability report without writing a zip or receipt.

This portable check does not start Revit, write model data, package live
deliverables, call a network service, create a database, or depend on SaaS, MCP,
built-in LLM behavior, or dashboard-central state.

## Runtime Boundaries

The issue spine is dry-run first. Write-capable issue packaging requires an
explicit non-dry-run command, a bundle path, and valid package evidence. The
v6.0 portable smoke scope only claims local preflight and dry-run package
behavior.

`issue diff --to current` can contact Revit through the client path and is not
part of this portable no-Revit smoke claim.

## Portable Evidence

Focused portable tests cover:

- `issue-preflight-report.v1` output for valid and invalid issue profiles;
- table summary and Markdown detail parity for JSON-derived preflight checks,
  command paths, package files, planned actions, and package command paths;
- artifact and mutation-plan reference checks;
- shell-operator and hidden-mutation rejection;
- `issue-package-receipt.v1` dry-run package reports;
- package traceability, per-file hashes, and receipt references;
- dry-run no-write evidence proving no bundle or receipt is created;
- no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central
  state, and no built-in LLM parser.

This is not live Revit issue-closure evidence and does not claim approved issue
package writes on a real RVT.
