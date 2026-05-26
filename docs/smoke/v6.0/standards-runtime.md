# RevitCli v6.0 Standards Runtime Portable Smoke

This slice documents the local standards command spine for the v6.0 Local
BIMOps Workbench:

```bash
revitcli standards validate --output json
```

`standards validate` reads a local standards manifest and project files, then
emits a deterministic validation report for office standards runtime checks. It
does not start Revit, does not write model data, install standards files, call
a network service, create a database, or depend on SaaS, MCP, built-in LLM
behavior, or dashboard-central state.

## Runtime Boundaries

The command is read-only for the target project. It validates local evidence
such as profile files, workflow files, deliverable output rules, schedule specs,
family rules, and compatibility metadata. It reports missing or malformed local
artifacts as issues instead of repairing them.

`standards install` remains the write-capable standards command and is outside
this v6.0 portable smoke scope.

## Portable Evidence

Focused portable tests cover:

- JSON, Markdown, and table output;
- table summary and Markdown detail parity for the JSON-derived name, pack
  version, status, and no-issue fields;
- populated-target final file-tree snapshot evidence that `standards install
  --dry-run` leaves pre-existing target project files unchanged before any
  approved standards install writes;
- manifest and project path resolution;
- valid and invalid standards manifests;
- compatibility and policy issue reporting;
- standards runtime pack validation after a dry-run or approved install path;
- no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central
  state, and no built-in LLM parser.

This is not live Revit evidence and does not claim that office standards have
been piloted across real projects.
