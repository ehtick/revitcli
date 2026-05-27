# RevitCli v6.0 Ledger Analytics Bundle Smoke

This slice introduces a local ledger analytics bundle command:

```powershell
revitcli ledger analytics --source all --bucket day --output-dir .revitcli/analytics --output json
```

The command writes two local JSON snapshots in the requested output directory:

- `ledger-stats.json` with schema `ledger-stats.v1`
- `ledger-timeline.json` with schema `ledger-timeline.v1`

It then emits a `ledger-analytics-bundle.v1` summary containing the generated
snapshot paths, stats summary, timeline summary, project roots, and explicit
local-only boundary flags.

`ledger analytics` is a packaging command for deterministic local evidence. It
does not start Revit, does not call a network service, and does not create a database.
It does not run a background analytics service, does not sync to a dashboard,
and does not become the source of truth for the operations ledger. The source
of truth remains local artifacts read by `ledger query`, `ledger validate`,
`ledger stats`, and `ledger timeline`.

The command accepts the same local filtering scope as stats and timeline:

- `--dir`
- repeated `--project`
- `--source`
- `--since`
- `--until`
- `--window`
- `--action`
- `--category`
- `--operator`
- `--receipt-status`
- `--bucket`

Acceptance evidence for this slice:

- `ledger analytics --output json` emits `ledger-analytics-bundle.v1`;
- the stats snapshot is written as `ledger-stats.v1`;
- the timeline snapshot is written as `ledger-timeline.v1`;
- JSON/table/Markdown output formats describe the same bundle paths and
  operation counts;
- the payload declares `localOnly=true`, `databaseRuntime=false`, and
  `networkService=false`.
- no database runtime is introduced.
