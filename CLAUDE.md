# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build, Test, Run

```bash
# CLI + Shared + portable tests (any OS)
dotnet build src/RevitCli/RevitCli.csproj
dotnet test tests/RevitCli.Tests/

# Run a single test by name
dotnet test tests/RevitCli.Tests/ --filter "FullyQualifiedName~WorkbenchCommandTests.Contract_Json_PrintsStableEnvelopeForCodexCli"
# Or by class
dotnet test tests/RevitCli.Tests/ --filter "ClassName=RevitCli.Tests.Commands.WorkbenchCommandTests"

# Add-in (Windows + Revit only) — RevitYear selects DLL refs and bin\Release\$(RevitYear)\ output
dotnet build src/RevitCli.Addin -p:RevitYear=2026   # default; also 2024 (net48) / 2025/2026 (net8.0-windows)

# Headless release preflight (used in CI before tagging)
dotnet run --project src/RevitCli -- release verify --output json

# Dashboard
cd dashboard && npm install && npm run dev      # dev server
cd dashboard && npm run check && npm run build  # type-check + production build
```

- `TreatWarningsAsErrors=true` is set in `Directory.Build.props`; build will fail on new warnings. CS1591 (missing docs) is suppressed; NU1605 (downgrade) is allowed.
- `Directory.Packages.props` centralizes NuGet versions — add new packages there, not in csproj.
- `RevitCliVersion` in `Directory.Build.props` is the **single source of truth** for both CLI and add-in. Bump it (not csproj `<Version>`) when releasing.

## Architecture

```
CLI (revitcli.exe, net8.0) ──HTTP REST──> Revit Add-in (embedded EmbedIO server)
                                                │
                                          ExternalEvent Bridge
                                                │
                                          Revit API (main thread)
```

Three projects, three target frameworks:

| Project | Target | Notes |
|---|---|---|
| `src/RevitCli` | `net8.0` | Standalone console; runs on Linux/macOS for headless ops against a remote Revit host. Packed as a dotnet tool (`revitcli`). |
| `src/RevitCli.Addin` | `net48` for Revit 2024, `net8.0-windows` for 2025/2026 | Selected by `-p:RevitYear=2024|2025|2026`; defines `REVIT2024/2025/2026` constants. Output goes to `bin\$(Configuration)\$(RevitYear)\` so years don't overwrite each other. |
| `shared/RevitCli.Shared` | `netstandard2.0` | DTOs + `IRevitOperations` interface — must stay framework-neutral so both CLI and add-in (including net48) can reference it. |

### CLI ↔ Add-in protocol

- Add-in binds **`http://127.0.0.1:<port>/`** only (per `CLAUDE.md` path rules; this file lives at `~/CLAUDE.md`). Never bind `localhost` or `0.0.0.0`.
- Default port is `ServerInfo.DefaultPort`; if busy, the add-in falls back through the next 10 ports.
- Auth: every request must carry `X-RevitCli-Token: <token>`. The token is generated per add-in start, written to `~/.revitcli/server.json` (ACL-restricted on Windows), and discovered by `RevitClient.DiscoverServerUrl()`.
- All Revit API access goes through `RevitBridge.InvokeAsync()`, which posts work to Revit's main thread via `ExternalEvent`. **Never call Revit API directly from an HTTP handler thread.**
- `IRevitOperations` is the seam: `RealRevitOperations` (production), `PlaceholderRevitOperations` (smoke harness). Handlers in `src/RevitCli.Addin/Handlers/` depend only on this interface.

### CLI command pattern

Every command in `src/RevitCli/Commands/` exposes two execution paths:

1. **Interactive (Spectre.Console)** — invoked via `System.CommandLine` handler. Renders tables, prompts, colors.
2. **Testable** — a static `ExecuteAsync(TextWriter writer, ...)` (or `ExecuteContractAsync`, etc.) that takes a `TextWriter` and returns an exit code. Tests assert against a `StringWriter`.

When adding a command:
- Create `src/RevitCli/Commands/<Name>Command.cs` with both paths.
- Register in `CliCommandCatalog.CreateRootCommand()` (and add to `TopLevelCommands` + `InteractiveHelpEntries` arrays).
- If the command adds shell-completion-visible flags, update `CompletionsCommand.cs`.
- For new HTTP endpoints, add a controller in `src/RevitCli.Addin/Handlers/`, register it in `ApiServer.CreateServer()`, and add the request/response DTO in `shared/RevitCli.Shared/`.

### Profile system (`.revitcli.yml`)

- `ProfileLoader.Discover()` walks upward from cwd looking for `.revitcli.yml`. Consumed by `check`, `publish`, `init`, `score`, `coverage`, `plan apply`, `fix`.
- `extends:` is **single-parent, no deep merge** — a child key fully replaces the parent's. Copy parent rules into the child if you want to extend rather than replace.
- Starter templates live in `profiles/` and are copied into the published tool output (see `RevitCli.csproj` `<Content Include="..\..\profiles\*.yml" ...>`); `revitcli init <template>` picks one.
- Schema validation: `ProfileValidator` / `profile validate` lint without contacting Revit.

### Receipts, plans, and stable schemas

User-facing JSON outputs use versioned schema names — change them carefully and bump the version when breaking shape:
- `workbench-contract.v1` (`workbench contract --output json`)
- `plan-summary.v1`, `plan-receipt.v1` (`plan show`, `plan apply`)
- `workflow-run-receipt.v1` (`workflow run`)
- `family-purge-report.v1` (`family purge --report`)
- `delivery-bundle-receipt.v1` (`deliverables bundle`)
- Local artifacts go under `.revitcli/` (gitignored): `journal.jsonl`, `plans/`, `receipts/`, `history/`, `deliveries/`, `workflows/receipts/`, `reports/`, `standards.yml`, `sheets/index.yml`.

### Safety invariants

- `set`, `fix`, `import` default to plan/dry-run flows. Real writes go through `Transaction` + journal entry + receipt with rollback info; `plan apply` honors `defaults.planMaxChanges` / `defaults.highImpactChanges` gates from the profile.
- `export` / `publish` guard `OutputDir` against path traversal and **restrict to the user home directory**.
- `journal sign` / `verify` chains entries with SHA256 + HMAC; `release verify` checks signing and CI guardrails before tagging.

## Testing conventions

- xUnit + Moq. CLI tests under `tests/RevitCli.Tests/Commands/`, mirroring `src/RevitCli/Commands/`. Add-in/protocol tests under `tests/RevitCli.Addin.Tests/` (Windows + Revit only — skipped on the Ubuntu CI runner).
- For HTTP interactions, use `FakeHttpHandler` (see `tests/RevitCli.Tests/Client/RevitClientTests.cs:144`) — never spin a real EmbedIO server in unit tests.
- Tests that mutate `Environment.ExitCode` or `Console.Out` go in `[Collection("Sequential")]` (see `tests/RevitCli.Tests/SequentialCollection.cs`) so they don't race other parallel tests.
- `InternalsVisibleTo` is set: `RevitCli.Tests` can reach `RevitCli` internals; `RevitCli.Addin.Tests` can reach `RevitCli.Addin` internals.
- CI (`.github/workflows/ci.yml`) only runs the portable CLI tests on Ubuntu. The add-in is built/tested on the `[self-hosted, windows, revit2026]` runner via `ci-addin.yml` and `release.yml`.

## Conventions

- C#: 4-space indent, `nullable` enabled, implicit usings on. Command classes end in `Command`, tests in `Tests`. Keep Spectre.Console out of `ExecuteAsync`-style paths so tests can assert plain text.
- Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`, `ci:`, `chore:`.
- Don't commit `.revitcli/`, `.revitcli.yml`, NuGet keys, publish output, or machine-specific Revit install paths — they're gitignored or environment-specific.
- Output formats are stable contracts: `--output table|json|markdown` (and `csv` / `sarif` / `pr-comment` for specific commands). Adding a format means updating both the command and its tests.
