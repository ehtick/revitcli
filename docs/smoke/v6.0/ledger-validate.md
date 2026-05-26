# RevitCli v6.0 Ledger Validate Portable Smoke

This slice introduces a read-only ledger validate command for local artifacts:

```bash
revitcli ledger validate --source all --output json
```

The command emits `ledger-validate.v1` and does not write files, start Revit,
call a network service, create a database, or depend on SaaS, MCP, built-in LLM
behavior, or dashboard-central state. In short: no database, no cloud control
plane, no repair action, and no hidden model mutation.

## Checks

`ledger validate` aggregates the same local sources as `ledger query`, then
validates the artifact graph:

- source readability for journal, history, delivery, and workflow receipt
  locations;
- artifact links for referenced local evidence paths;
- receipt status for valid, missing, or unreadable receipt references;
- declared receipt hash values when a delivery manifest includes `receiptHash`;
- timestamp format for parsed operation records, requiring an explicit UTC offset
  (`Z` or `+/-HH:mm`) so local timestamps are not silently coerced.

It supports the same local filters as query, including source, window, action,
category, operator, and receipt status. Time filters preserve invalid timestamp
warnings so malformed or offset-less records remain visible during validation.
`--fail-on error` fails only on errors; `--fail-on warning` also fails on
warnings.

## Portable Evidence

Focused portable tests cover:

- valid journal, history, delivery, and workflow receipt artifacts;
- missing delivery receipt detection;
- delivery manifest `receiptHash` mismatch detection, promoted to the ledger
  validation `receipt.hash` issue and `receipt-hashes` check;
- timestamp warnings for malformed timestamps and timestamps without explicit
  UTC offsets;
- time filters preserve invalid timestamp warnings for malformed or offset-less
  records;
- query-compatible filters for source, action, category, operator, and receipt
  status;
- validation JSON/table/Markdown semantic parity for valid status, operation
  count, error count, warning count, check status, check id, and check evidence;
- event-level no-write evidence plus final file-tree snapshot evidence proving
  `ledger validate` does not mutate the local artifact tree, including failing
  validation paths;
- table, JSON, and Markdown output;
- invalid option failures.

This is not live Revit evidence. It does not claim live ledger apply, automatic
artifact repair, cross-project analytics, office rollout pilots, or a
production Revit operations ledger database runtime.
