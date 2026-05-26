# RevitCli v6.0 Workflow Registry Portable Smoke

This slice introduces a read-only workflow registry command for governed local
workflow YAML files:

```bash
revitcli workflow registry --output json
```

The command emits `workflow-registry.v1` and does not write files, run workflow
steps, start Revit, call a network service, create a database, or depend on
SaaS, MCP, built-in LLM behavior, or dashboard-central state. In short: no
SaaS, no MCP control plane, no built-in LLM parser, no dashboard-central source
of truth, and no hidden model mutation.
It does not start Revit.

Boundary summary: no SaaS, no MCP, no built-in LLM, no database, and no
dashboard-central workflow state.

## Registry Fields

`workflow registry` indexes each local workflow with:

- inputs inferred from workflow YAML, required artifact classes, and selected
  concrete local references such as `--profile`, `--manifest`, `--spec`,
  `plan apply <path>`, and `rollback <receipt>`;
- outputs such as `workflow-registry.v1`, `workflow-review.v1`,
  `workflow-run-receipt.v1`, issue packages, delivery bundles, and schedule
  exports when commands imply them, including `schedule-export-manifest.v1`
  when schedule export commands are present;
- read/write scope from declared step modes;
- risk level derived from read-only, dry-run, mutating, or invalid workflow
  steps;
- dry-run command entries for previewable steps;
- approval command entries for steps that declare `requiresApproval: true`;
- rollback support when a rollback command is declared;
- receipt schema entries such as `workflow-run-receipt.v1`,
  `plan-receipt.v1`, `issue-package-receipt.v1`, or
  `delivery-bundle-receipt.v1`, and `publish-receipt.v1`;
- acceptance evidence such as `workflow validate`, `workflow simulate`,
  `workflow review`, `workflow receipts`, `journal verify`, workflow receipt
  schemas, package receipt schemas, and schedule export manifest schemas.

Missing default workflow directories return a warning and an empty registry so
new projects can inspect the contract before installing templates. Explicit
missing paths fail as `source.missing`.

## Portable Evidence

Focused portable tests cover:

- JSON indexing of local workflows with inputs, outputs, read/write scope, risk
  level, dry-run command, approval command, rollback support, receipt schema,
  and acceptance evidence fields;
- JSON/table/Markdown output semantic parity for registry summary counts,
  workflow risk/scope, inputs, outputs, dry-run and approval command counts,
  rollback support, receipt schemas, and acceptance evidence;
- final file-tree snapshot evidence and event-level no-write evidence proving
  registry reads local workflow YAML without writing registry artifacts;
- output and evidence inference for delivery bundle, schedule export, publish,
  and journal verify command classes;
- warning-only empty registry output when `.revitcli/workflows` is absent;
- explicit missing path failure;
- table and Markdown output fields;
- workflow validation accepting `workflow registry` as a read-only command;
- invalid output option failure.

This is not live Revit evidence. It does not claim workflow execution, ledger
replay, cross-project analytics, office rollout pilots, SaaS, MCP, built-in LLM
behavior, or dashboard-central workflow state.
