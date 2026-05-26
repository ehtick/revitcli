# RevitCli v6.0 Deliverables Verify Portable Smoke

This slice documents the local deliverables verification spine for the v6.0
Local BIMOps Workbench:

```bash
revitcli deliverables verify --output json
```

`deliverables verify` reads the local delivery manifest, verifies that each
entry points to readable receipt evidence, and checks any declared `receiptHash`
against the referenced receipt file bytes. It emits deterministic local
deliverable status and issue data without starting Revit, exporting files,
writing packages, calling a network service, creating a database, or depending
on SaaS, MCP, built-in LLM behavior, or dashboard-central state.

## Runtime Boundaries

The command validates local manifest-read and readable-receipt evidence. Missing
manifests, malformed manifests, unreadable receipts, and missing receipts become
structured issues. It does not repair the manifest, regenerate receipts, or
bundle deliverables.

`deliverables bundle` is the write-capable packaging path and remains dry-run
first outside this verification smoke scope.

## Portable Evidence

Focused portable tests cover:

- JSON, Markdown, and table output;
- Kinds and Outcomes counts preserved in table and Markdown output from the
  JSON-derived manifest stats;
- missing and malformed delivery manifest handling;
- readable, missing, and unreadable receipt evidence;
- declared receipt-hash reporting and mismatch failures;
- delivery entry status and issue severity reporting;
- local manifest-read and readable-receipt evidence without package writes;
- no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central
  state, and no built-in LLM parser.

This is not live Revit export evidence and does not claim real PDF/DWG/NWC/IFC
delivery generation.
