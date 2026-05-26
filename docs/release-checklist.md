# Release Checklist

Use this checklist before pushing a version tag. RevitCli is Windows/Revit-first:
do not ship a release that only passed Linux or documentation checks.

## 1. Preflight

```powershell
git status --short
dotnet restore
dotnet build
dotnet test tests/RevitCli.Tests/
revitcli release verify --tag vX.Y.Z
revitcli release verify --tag vX.Y.Z --output markdown
revitcli release verify --tag vX.Y.Z --strict --output markdown # required for v5.0 RC handoff
```

`release verify` checks local release files, `RevitCliVersion`, changelog and
README release notes, Ubuntu CLI/Shared CI guardrails, portable workbench
verification in CI, installer hardening markers, release packaging workflow
markers, v5.0 RC boundary docs, v6 portable workbench runtime evidence, and
smoke documentation. Markdown output is intended for maintainer handoff notes.
It does not run live Revit smoke. The v6.0 `workbench-gate` check emits and
consumes structured `runtimeEvidence` booleans for command-spine output parity,
command-spine no-write proof, workflow registry, ledger query/validate,
ledger stats, and ledger timeline checks instead of relying on message text
alone. Ubuntu CI also runs `release verify --output json` and
`workbench verify --contract workbench-contract.v2 --dir . --output json` after
the portable CLI/Shared build so release and workbench guardrails fail before
merge.

For a v5.0 RC handoff, `release verify --strict` is required. A disclosed
`NO-GO` status or missing controlled issue-closure smoke for any claimed live
Revit year remains a warning in normal CI, but becomes a blocking failure under
`--strict`.

If the dashboard changed:

```powershell
cd dashboard
npm install
npm run check
npm run build
cd ..
```

## 2. Installer And Add-in

Close all Revit processes before final release verification when possible. If
Revit is running, the installer updates CLI files immediately and stages add-in
files under `%LOCALAPPDATA%\RevitCli\staged` for the next Revit restart.

For the v2.3.0 release package, validate the packaged Revit 2026 add-in. If
Revit 2026 is installed outside `%ProgramFiles%`, pass the local path:

```powershell
.\scripts\install.ps1 -RevitYears 2026 `
  -Revit2026InstallDir "D:\revit2026\Revit 2026" `
  -Force
```

For full multi-version source-tree validation when all local Revit installs are
available:

```powershell
.\scripts\install.ps1 -RevitYears 2024,2025,2026 `
  -Revit2024InstallDir "D:\Autodesk\Revit 2024" `
  -Revit2025InstallDir "D:\Autodesk\Revit 2025" `
  -Revit2026InstallDir "D:\Autodesk\Revit 2026" `
  -Force
```

Run at least the target release year:

```powershell
revitcli doctor --check-version 2026
```

If add-ins were staged while Revit was running, restart Revit once before the
final `doctor` / `status` evidence so the live add-in matches the manifest.

## 3. Real Revit Smoke

Run the scripted smoke on a controlled model:

```powershell
.\scripts\smoke-revit.ps1 -Version 2026 `
  -ElementId 12345 `
  -Category walls `
  -Filter "Mark = W-01" `
  -V4Workbench `
  -V4ProjectDir .
```

For v5.0 issue-closure releases, run the separate issue closure lane on a
disposable controlled model or record the gap explicitly:

```powershell
.\scripts\smoke-revit.ps1 -Version 2026 `
  -ElementId 12345 `
  -Category walls `
  -Filter "Mark = W-01" `
  -V4Workbench `
  -V4ProjectDir . `
  -V5IssueClosure `
  -V5ProjectDir . `
  -V5IssueProfile .\profiles\v5-issue.yml `
  -V5IssueBundlePath .\.revitcli\smoke\v5-issue-2026.zip `
  -V5SheetSelector all `
  -V5IssueCode R03 `
  -V5IssueDate 2026-05-22 `
  -V5SheetParamMap .\.revitcli\smoke\sheet-issue-param-map.yml `
  -V5ApplySheetIssue `
  -V5WriteIssuePackage
```

If `-V5ApplySheetIssue` is omitted, the v5 lane proves only workbench v2,
issue preflight, sheet issue plan/dry-run when sheet inputs are supplied, and
issue package dry-run. An apply claim requires receipt creation, rollback
dry-run, approved rollback, and `journal verify` evidence from the report.
If `-V5WriteIssuePackage` is omitted, it does not prove approved issue package
zip/receipt writing. Use a disposable non-existing `-V5IssueBundlePath` before
enabling that switch.

The smoke report records every command attempt. A single retry is allowed only
for explicit transient add-in communication timeouts on read-only or dry-run
commands; ordinary command failures and write/rollback failures remain
blocking.

For 2024/2025, record gaps if a runner or local install is unavailable. Do not
claim live support beyond the versions that passed smoke. Use
[revit2026-real-smoke.md](revit2026-real-smoke.md) for the evidence packet.
For v5.0 issue closure, each release handoff must include either
`docs/smoke/v5.0/revit-<year>-issue-closure.md` for the verified year or
`docs/smoke/v5.0/gap-report.md` stating that the year is not live verified.
The RC boundary and stable/experimental command split are tracked in
[v5-rc-readiness.md](v5-rc-readiness.md).

### v5.0 Fault Injection Evidence

Before a v5.0 RC claim, record portable fault injection and any available
Windows/Revit fault evidence:

- Missing profile: `issue preflight/package` must fail clearly when the issue
  profile path is missing.
- Missing receipt: `deliverables verify` must report `receipt-missing`.
- Tampered receipt: malformed receipt JSON must report `receipt-json-invalid`;
  plan rollback must reject a missing plan file or plan hash mismatch.
- Path permission or write failure: package/bundle writes must fail without
  leaving a partial ZIP or receipt sidecar.
- Stale value: rollback must stop on current-value conflicts instead of
  overwriting intervening edits.
- Worksharing lock: where a live workshared copy is available, record the lock
  behavior as evidence or as a gap.

### v5.5 View and Coordination Hygiene Evidence

For v5.5 handoff, keep the claim audit-first unless controlled Revit evidence
exists:

- `views audit`, `links audit`, and `model map-check` are read-only evidence.
- `views template-apply`, `views clone-set`, `links repair`, and
  `model map-fix` must stay dry-run/plan-first in portable gates.
- `links repair` must remain path/load only; coordinate drift is audit-only and
  no coordinate moves are claimed in v5.5.
- `model map-fix` must expose write-precheck and writable-probe evidence before
  any approved apply.
- Worksharing locks, placed-view rollback review, receipt/rollback, and
  `journal verify` need controlled Revit project-copy evidence or explicit gap
  rows in `docs/smoke/v5.5/gap-report.md`.

## 4. Journal Integrity

After a smoke that writes or exports, verify the local journal:

```powershell
revitcli journal stats
revitcli journal show --limit 10
revitcli journal sign
revitcli journal verify
```

Do not commit `.revitcli/` smoke artifacts unless a release note explicitly asks
for sanitized evidence.

## 5. Version And Changelog

Update:

- `Directory.Build.props` `RevitCliVersion`
- `CHANGELOG.md` `[Unreleased]` section
- README command list or docs for new user-facing commands

Use Conventional Commits and keep release notes focused on architect workflows,
installer changes, Revit compatibility, and known smoke gaps.

## 6. Tag And Release

```powershell
git tag vX.Y.Z
git push origin main
git push origin vX.Y.Z
```

GitHub Actions packages the CLI and add-in ZIP on a tag push. After the run,
download the ZIP, verify `SHA256SUMS.txt`, install it on Windows, restart Revit,
and run:

```powershell
revitcli doctor --check-version 2026
revitcli status
```

NuGet publishing is a separate manual `Publish to NuGet` workflow. Run it only
when publishing the CLI package to NuGet.org, and only after adding the
`NUGET_API_KEY` repository secret.
