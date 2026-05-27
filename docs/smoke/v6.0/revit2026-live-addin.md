# RevitCli v6.0 Revit 2026 Live Add-in Smoke

Date: 2026-05-25

Scope: live Revit 2026 identity, query, dry-run set preview, approved set,
confirm, restore, local ledger recording, and bounded ledger replay apply on
the controlled `revit_cli.rvt` smoke model.

Refresh: on 2026-05-27, the same open Revit 2026 model was reachable from WSL
by invoking the installed Windows CLI through `scripts/smoke-revit-wsl.sh`.
That helper records `doctor --check-version 2026 --output json`,
`status --output json`, `query --id`, filtered `query`, and a `set --dry-run`
preview into `.artifacts/live-smoke/revit2026-wsl-*`, plus
`summary.json` with the pass/fail rollup, `sourceInstalledDrift`,
`currentSourceDriftKind`, `stagedAddinCommit`, and `stagedAddinPath`, without
passing `--yes` or mutating the model. The refresh is live-environment evidence
only: it also records the source `HEAD`, so installed Windows CLI/add-in version
drift stays visible instead of being treated as current-source validation. Use
`scripts/smoke-revit-wsl.sh --require-current-source` when the claim needs the
currently checked-out source to be installed. The helper fails with
`install-required` when the staged install is not current and writes
`nextActions` plus a generated `install-current-source.ps1` handoff for
reinstall/restart/rerun. It fails with `restart-required` when the staged
install is current but the open Revit process is still running an older loaded
add-in; in that case it writes restart/rerun `nextActions` without generating an
install handoff. For a stable repo-tracked Windows entrypoint, run
`scripts\install-current-source-revit2026.ps1`, restart Revit if the Add-in was
staged, then rerun the WSL helper with `--require-current-source`.

The 2026-05-27 WSL helper run wrote
`.artifacts/live-smoke/revit2026-wsl-20260527-current/` with all five steps
passing. It recorded source `HEAD=38ad41774a3d4f68e6b81e8ce4fdb5338ea8ca4f`,
Windows CLI/add-in/live add-in `2.3.0+05c6d927bcff23777995fbfff7226ecfc55aac3f`,
`revitYear=2026`, document `revit_cli`, element `337596`, filter
`标记 = TEST`, and a one-element dry-run preview for parameter `注释`. A
follow-up summary run wrote
`.artifacts/live-smoke/revit2026-wsl-20260527-summary/summary.json` with
`success=true`, `queryIdCount=1`, `queryFilterCount=1`, `previewCount=1`,
`sourceInstalledDrift=true`, and `mutatesModel=false`.

After staging the current source from WSL, the helper wrote
`.artifacts/live-smoke/revit2026-wsl-doc-gated-staged-evidence-82a54be/summary.json`
with
`success=false`, `currentSourceInstalled=false`,
`currentSourceDriftKind=restart-required`,
`cliCommit=82a54bed84b66c8df0826af32de60f33c0dcab3b`,
`installedAddinCommit=82a54bed84b66c8df0826af32de60f33c0dcab3b`,
`stagedAddinCommit=82a54bed84b66c8df0826af32de60f33c0dcab3b`,
`liveAddinCommit=05c6d927bcff23777995fbfff7226ecfc55aac3f`,
`statusAddinCommit=05c6d927bcff23777995fbfff7226ecfc55aac3f`, and
`mutatesModel=false`. That evidence proves the installer staged the current
add-in source, but the already-open Revit process still needs a restart before
the live add-in/source alignment claim can pass.

Environment:

- Revit process: `D:\revit2026\Revit 2026\Revit.exe`
- Document: `D:\桌面\revit\revit_cli.rvt`
- Element: wall `337596`
- Filter: `标记 = TEST`
- Parameter: `注释`

Evidence:

- `doctor --check-version 2026 --output json` passed with CLI version `2.3.0`,
  installed add-in version `2.3.0`, live add-in version `2.3.0`, and Revit 2026
  server info.
- `status --output json` returned `revitYear=2026`, document `revit_cli`, and
  the active add-in capabilities.
- `query --id 337596 --output json` and
  `query walls --filter "标记 = TEST" --output json` both selected the same
  wall.
- `set walls --filter "标记 = TEST" --param "注释" --value
  "revitcli-v6-smoke-20260525" --dry-run` previewed one change.
- `scripts/smoke-revit2026.ps1 -Apply` wrote the value, confirmed it, then
  restored the old empty value with approved `set --yes` writes, including
  `set --id 337596 --param "注释" --clear-value --yes`.
- Approved `set` writes now append local `ledger-operation.v1` records with
  `command=set`, `action=set`, affected count, available affected ids, and
  best-effort `modelIdentity`/`modelPath`/`revitVersion` from `/api/status` under
  `.revitcli/ledger/operations.jsonl`; dry-run set previews and `--plan-output`
  do not append ledger records.
- `scripts/smoke-revit2026.ps1 -V6LedgerReplayApply` and
  `scripts/smoke-revit.ps1 -Version 2026 -V6LedgerReplayApply` now provide the
  repeatable bounded replay smoke path. The gated phase creates an isolated
  local ledger record from approved `set --yes`, restores the original value,
  previews `ledger replay --source ledger --action set --limit 1`, applies it
  with `--apply --yes`, confirms the replayed value, and restores the original
  value again. The scripts now also gate the isolated ledger file itself by
  requiring exactly one `ledger.replay.apply` audit row with the affected
  element id, `--apply --yes` replay args, and non-empty
  `modelIdentity`/`modelPath`/`revitVersion`.
- `scripts/smoke-revit2026.ps1 -V6LedgerReplayApply` passed on
  `2026-05-25` with `ledger-replay.v1`, `dryRun=false`,
  `appliedStepCount=1`, `failedStepCount=0`, and `applyStatus=applied` for
  element `337596`.
- The refreshed replay-apply audit smoke also wrote exactly one
  `ledger-operation.v1` row with `command=ledger`,
  `action=ledger.replay.apply`, `affectedElementIds=[337596]`, and replay args
  containing `--apply --yes`. Refreshed replay-audit rows record
  `modelIdentity`/`modelPath`/`revitVersion` from `/api/status` when status is
  available.
- `export --format pdf --sheets "jianzhu2"` passed on `2026-05-25` against the
  loaded model after refreshing the Windows CLI. The export wrote
  `RevitCLI_Export.pdf`, `export-receipt.v1`, `delivery-manifest.v1`, and one
  `ledger-operation.v1` row with `command=export`, `action=export`,
  `category=pdf`, `receiptStatus=valid`, `modelIdentity=revit_cli`,
  `modelPath=D:\桌面\revit\revit_cli.rvt`, and `revitVersion=2026`; `ledger
  query --source ledger` read the row back as `ledger-query.v1`.
- `schedules batch-export --set issue --format csv` passed on `2026-05-25`
  against schedule `B_内墙明细表`. The command wrote one CSV, a
  `schedule-export-manifest.v1`, and one `ledger-operation.v1` row with
  `command=schedules`, `action=schedules.batch-export`, `category=csv`,
  `status=succeeded`, `modelIdentity=revit_cli`,
  `modelPath=D:\桌面\revit\revit_cli.rvt`, and `revitVersion=2026`; `ledger
  query --source ledger` read the row back as `ledger-query.v1`.
- `ledger replay --source ledger --action schedules.batch-export --apply --yes`
  passed on `2026-05-25` against that successful schedule batch-export ledger
  row. The replay applied one step, rewrote the recorded CSV output from the
  recorded manifest entry, and appended one non-replayable
  `ledger.replay.apply` audit row with `category=csv`,
  `modelIdentity=revit_cli`, `modelPath=D:\桌面\revit\revit_cli.rvt`, and
  `revitVersion=2026`.
- Final query confirmed `注释` is empty again and `标记` remains `TEST`.

Report:

- `.artifacts/live-smoke/revit2026-v6-dryrun-20260525.json`
- `.artifacts/live-smoke/revit2026-v6-apply-restore-20260525-fixed.json`
- `.artifacts/live-smoke/revit2026-v6-ledger-set-20260525.json`
- `.artifacts/live-smoke/revit2026-v6-ledger-set-yes-20260525.json`
- `.artifacts/live-smoke/revit2026-v6-ledger-replay-apply-20260525-113938.json`
- `.artifacts/live-smoke/revit2026-v6-ledger-replay-apply-audit-20260525-121641.json`
- `.artifacts/live-smoke/revit2026-v6-ledger-replay-apply-status-evidence-20260525-125420.json`
- `.artifacts/live-smoke/revit2026-v6-set-ledger-model-identity-20260525-123722.json`
- `.artifacts/live-smoke/revit2026-v6-set-ledger-revit-version-20260525-124836.json`
- live ledger file:
  `D:\temp\revitcli-v6-live-ledger-yes-20260525-1028\.revitcli\ledger\operations.jsonl`
  with two approved `set` records: write and restore, both for element
  `337596`; both command args include `--yes`.
- refreshed live ledger model evidence:
  `.revitcli\ledger\operations.jsonl` contains approved `set` write and
  restore rows for element `337596` with `modelIdentity=revit_cli` and
  `modelPath=D:\桌面\revit\revit_cli.rvt`; refreshed v6.0 Revit-version
  evidence additionally records `revitVersion=2026` when status is available.
- bounded replay ledger directory:
  `D:\temp\revitcli-v6-ledger-replay-apply-20260525-113938`
- refreshed replay-audit ledger directory:
  `C:\Users\Lenovo\AppData\Local\Temp\revitcli-v6-ledger-replay-apply-20260525-121639`
- refreshed replay-audit status evidence:
  latest replay audit row has `modelIdentity=revit_cli`,
  `modelPath=D:\桌面\revit\revit_cli.rvt`, and `revitVersion=2026`.
- script-gated replay-audit status evidence:
  `.artifacts/live-smoke/revit2026-v6-ledger-replay-script-gate-20260525-130112.json`
  passed with `appliedStepCount=1`, `failedStepCount=0`,
  `action=ledger.replay.apply`, `affectedElementIds=[337596]`,
  `modelIdentity=revit_cli`, and `revitVersion=2026`.
- live export ledger evidence:
  `.artifacts/live-smoke/revit2026-v6-export-ledger-20260525-051539/`
  contains `operations.jsonl`, `manifest.jsonl`, and the export receipt copied
  from
  `C:\Users\Lenovo\AppData\Local\Temp\revitcli-v6-export-ledger-20260525-live`.
  The live ledger row has `command=export`, `action=export`, `category=pdf`,
  `receiptStatus=valid`, `modelIdentity=revit_cli`, and `revitVersion=2026`.
- live schedule batch-export ledger evidence:
  `.artifacts/live-smoke/revit2026-v6-schedules-ledger-20260525/` contains
  `operations.jsonl`, `manifest.json`, and `B_内墙明细表.csv` copied from
  `C:\Users\Lenovo\AppData\Local\Temp\revitcli-v6-schedules-ledger-output`.
  The live ledger row has `command=schedules`,
  `action=schedules.batch-export`, `category=csv`, `status=succeeded`,
  `modelIdentity=revit_cli`, and `revitVersion=2026`.
- live schedule batch-export replay evidence:
  `.artifacts/live-smoke/revit2026-v6-schedules-replay-apply-20260525/`
  contains `operations.jsonl`, `manifest.json`, and `B_内墙明细表.csv` copied
  from
  `C:\Users\Lenovo\AppData\Local\Temp\revitcli-v6-schedules-replay-output-20260525`.
  The ledger has one replayable `schedules.batch-export` row followed by one
  non-replayable `ledger.replay.apply` row with `--action
  schedules.batch-export`, `--apply`, `--yes`, `modelIdentity=revit_cli`, and
  `revitVersion=2026`.

Known boundary:

- This proves the installed Revit 2026 add-in is loaded and can execute the
  live set apply/restore path on the controlled model and record approved set
  writes in the local operations ledger with model identity/path/version
  evidence when status is available.
- It also proves the bounded `ledger replay --source ledger --action set
  --apply --yes` path against one approved local source-ledger record with
  frozen affected ids, including a non-replayable local audit row for the
  replay operation itself.
- It additionally proves one receipt-backed non-`set` live operation path:
  successful PDF export records a local operations-ledger row and can be read
  back through `ledger query --source ledger`; the successful export row can
  also be replayed with `ledger replay --source ledger --action export --apply
  --yes`, appending a non-replayable `ledger.replay.apply` audit row with
  live model/version evidence.
- It also proves one manifest-backed schedule export path: successful
  `schedules batch-export` records a local operations-ledger row and can be
  read back through `ledger query --source ledger`; the successful schedule
  batch-export row can also be replayed with `ledger replay --source ledger
  --action schedules.batch-export --apply --yes`, appending a non-replayable
  `ledger.replay.apply` audit row with live model/version evidence.
- Broader live operations ledger replay/apply beyond approved set/export/schedule records,
  cross-project analytics, office rollout pilots, SaaS, MCP, built-in LLM
  behavior, dashboard-central state, and database runtime remain out of scope.
