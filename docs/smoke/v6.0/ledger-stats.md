# RevitCli v6.0 Ledger Stats Portable Smoke

This slice introduces a read-only ledger stats command for local artifacts:

```bash
revitcli ledger stats --source all --output json
revitcli ledger stats --dir project-a --project project-b --output json
revitcli ledger stats --analytics-snapshot .revitcli/analytics/ledger-stats.json --output json
revitcli ledger stats --from-analytics-snapshot .revitcli/analytics/ledger-stats.json --output json
```

The default command emits `ledger-stats.v1` and does not write files, start Revit,
call a network service, create a database, or depend on SaaS, MCP, built-in LLM
behavior, or dashboard-central state. An explicit
`--analytics-snapshot` path persists the same `ledger-stats.v1` JSON as a
local file, and `--from-analytics-snapshot` reads that file back without
recomputing from source artifacts. In short: no database, no cloud control
plane, no replay engine, and no hidden model mutation.

## Summaries

`ledger stats` aggregates the same local sources as `ledger query`, then
summarizes project-memory operation counts:

- source counts for journal, history, delivery, and workflow receipt artifacts;
- action counts for repeated local operations;
- category and operator counts when artifacts include those fields;
- receipt status counts for valid, missing, or unreadable receipt references;
- issue source counts for malformed or unreadable artifact sources;
- issue severity counts from malformed or unreadable artifacts.

It supports the same local filters as query, including source, window, action,
category, operator, and receipt status. The optional analytics snapshot is a
single local JSON artifact, not a persistent stats service, and it does not
replace `report knowledge`; `report knowledge` remains the broader reusable
project report, while `ledger stats` is the ledger-specific deterministic
summary.

`--project` can be repeated to include additional local project roots in the
same read-only summary. Multi-project stats emit `projectDirectories` and
`byProject` so BIM managers can compare local project evidence without a
database, cloud sync, dashboard state source, or hidden model access.

## Portable Evidence

Focused portable tests cover:

- deterministic operation counts across journal, history, delivery, and
  workflow receipt sources;
- filter reuse for source, window, action, category, operator, and receipt
  status;
- malformed journal, delivery manifest, and workflow receipt artifacts being
  surfaced as issue source counts and issue severity counts without failing the
  read-only command;
- JSON/table/Markdown stats semantic parity for operation counts, issue counts,
  source counts, action counts, category counts, operator counts, receipt status
  counts, issue source counts, and issue severity counts;
- cross-project JSON output with deterministic `projectDirectories` and
  `byProject` counts;
- explicit local analytics snapshot persistence and readback for
  `ledger-stats.v1`;
- event-level no-write evidence plus final file-tree snapshot evidence proving
  default `ledger stats` does not mutate the local artifact tree;
- table, JSON, and Markdown output;
- invalid option failures.

This is not live Revit evidence. It does not claim live ledger apply,
database-backed analytics, office rollout pilots, or a production Revit
operations ledger database runtime. Cross-project analytics in this slice means
aggregation over explicitly supplied local project directories plus optional
single-file JSON snapshots.
