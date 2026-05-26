# RevitCli v6.0 Ledger Timeline Portable Smoke

This slice introduces a read-only ledger timeline command for local artifacts:

```bash
revitcli ledger timeline --source all --bucket day --output json
revitcli ledger timeline --dir project-a --project project-b --bucket day --output json
revitcli ledger timeline --analytics-snapshot .revitcli/analytics/ledger-timeline.json --bucket day --output json
revitcli ledger timeline --from-analytics-snapshot .revitcli/analytics/ledger-timeline.json --output json
```

The default command emits `ledger-timeline.v1` and does not write files, start Revit,
call a network service, create a database, or depend on SaaS, MCP, built-in LLM
behavior, or dashboard-central state. An explicit
`--analytics-snapshot` path persists the same `ledger-timeline.v1` JSON as a
local file, and `--from-analytics-snapshot` reads that file back without
recomputing from source artifacts. In short: no database, no cloud control
plane, no replay engine, and no hidden model mutation.

## Timeline Buckets

`ledger timeline` aggregates the same local sources as `ledger query`, then
buckets project memory by `day` or `hour`:

- bucket start and end timestamps in UTC;
- source counts for journal, history, delivery, and workflow receipt artifacts;
- action counts for repeated local operations;
- category counts per bucket for model, issue, schedule, or standards areas;
- operator counts per bucket for local human or automation operators;
- receipt status counts for valid, missing, unreadable, or none values;
- issue severity counts from malformed or unreadable artifacts;
- unbucketed timestamp warnings when an operation has a missing or invalid
  timestamp, including timestamps without an explicit UTC offset.

It supports the same local filters as query, including source, window, action,
category, operator, and receipt status. Time filters preserve unbucketed
timestamp warnings so malformed or offset-less records remain visible during
timeline review. The optional analytics snapshot is a single local JSON
artifact, not a persistent timeline service, and it does not replace
`report knowledge`; `ledger timeline` is a ledger-specific deterministic view
for release and project-memory review.

Repeated `--project` roots add local cross-project timeline review over
explicitly supplied project directories. The JSON emits `projectDirectories`
and `byProject` counts, and table/Markdown output includes the project count
and by-project summary. This remains file-based aggregation only; it does not
create a database, dashboard state source, cloud sync, or background analytics
service.

## Portable Evidence

Focused portable tests cover:

- deterministic day and hour bucket output;
- local cross-project `--project` timeline aggregation with `projectDirectories`
  and `byProject` evidence, including table and Markdown project summaries;
- explicit local analytics snapshot persistence and readback for
  `ledger-timeline.v1`;
- filter reuse before bucketing, including category, operator, and receipt
  status filters;
- missing-source warnings for new projects;
- unbucketed timestamp warnings for invalid timestamps and timestamps without
  explicit UTC offsets;
- time filters preserve unbucketed timestamp warnings for malformed or
  offset-less records;
- JSON/table/Markdown timeline semantic parity for bucket name, operation
  counts, bucket counts, issue counts, source counts, action counts, category
  counts, operator counts, receipt status counts, and issue severity counts;
- event-level no-write evidence plus final file-tree snapshot evidence proving
  default `ledger timeline` does not mutate the local artifact tree;
- table, JSON, and Markdown output;
- invalid bucket option failures.
