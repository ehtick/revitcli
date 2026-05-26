# RevitCli v6.0 Ledger Query Portable Smoke

This slice introduces a read-only ledger query for local artifacts:

```bash
revitcli ledger query --source all --output json
```

The command emits `ledger-query.v1` and does not write files, start Revit, call
a network service, create a database, or depend on SaaS, MCP, built-in LLM
behavior, or dashboard-central state. In short: no database, no cloud control
plane, and no hidden model mutation.

## Sources

`ledger query` aggregates existing local evidence only:

- journal entries from `.revitcli/journal.jsonl`;
- history snapshots from `.revitcli/history/`;
- delivery manifest entries and receipt status from `.revitcli/deliveries/`;
- workflow receipt summaries from `.revitcli/workflows/receipts/`.

The output sorts operations deterministically by timestamp, source, artifact
path, and line number. Malformed artifacts are reported as structured issues
rather than hidden crashes or writes.

The v6 workbench runtime gate uses a synthetic same-timestamp fixture across
journal, history, delivery, and workflow sources to prove the
timestamp/source/path/line ordering contract, rather than only checking that
all source types are present.

## Portable Evidence

Focused portable tests cover:

- merging journal, history, delivery, and workflow receipt sources;
- source, window, action, and operator filters;
- malformed journal, delivery manifest, and workflow receipt issues;
- JSON/table/Markdown output semantic parity for operation count, operation
  order, timestamp, source, action, receipt status, and artifact path fields;
- event-level no-write evidence plus final file-tree snapshot evidence proving
  `ledger query` does not mutate the local artifact tree;
- table, JSON, and Markdown output;
- invalid option failures.

This is not live Revit evidence. It does not claim live ledger apply,
cross-project analytics, office rollout pilots, or a production Revit
operations ledger database runtime.
