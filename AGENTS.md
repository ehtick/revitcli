# Repository Guidelines

## Project Structure & Module Organization

This repository contains a .NET-based Revit automation CLI plus a local dashboard.

- `src/RevitCli/`: .NET 8 command-line tool. CLI commands live in `Commands/`; client and output helpers are grouped by feature.
- `src/RevitCli.Addin/`: Revit add-in and embedded HTTP server. API handlers live in `Handlers/`; Revit-facing implementations live in `Services/`.
- `shared/RevitCli.Shared/`: shared DTOs, filters, snapshots, and API response contracts.
- `tests/RevitCli.Tests/`: cross-platform CLI and shared-library xUnit tests.
- `tests/RevitCli.Addin.Tests/`: add-in/protocol tests, intended for Windows/Revit environments.
- `dashboard/`: SvelteKit/Vite dashboard for history data.
- `profiles/`, `docs/`, and `scripts/`: sample profile YAML, project docs, and install/automation scripts.

## Build, Test, and Development Commands

Run from the repository root unless noted.

```powershell
dotnet restore
dotnet build
dotnet test tests/RevitCli.Tests/
dotnet build src/RevitCli.Addin -p:RevitYear=2026
dotnet publish src/RevitCli -c Release -o ./publish
```

- `dotnet build` builds the solution with nullable reference types and warnings-as-errors enabled.
- `dotnet test tests/RevitCli.Tests/` runs the portable test suite.
- Add-in builds require Windows plus matching Revit API DLLs; `RevitYear` supports `2024`, `2025`, and `2026`.
- Dashboard work uses `cd dashboard`, then `npm install`, `npm run dev`, `npm run check`, and `npm run build`.

## Coding Style & Naming Conventions

Use C# with 4-space indentation, `nullable` enabled, and implicit usings. Keep package versions in `Directory.Packages.props`. Name command classes as `*Command`, tests as `*Tests`, and shared contracts as small DTO-focused types in `RevitCli.Shared`. CLI commands should expose testable `ExecuteAsync(...)` paths using `TextWriter`; keep Spectre.Console-only behavior in interactive command handlers.

## Testing Guidelines

Tests use xUnit with Moq where needed. Add CLI command tests under `tests/RevitCli.Tests/Commands/`; shared behavior belongs in the matching feature folder. Prefer fake HTTP handlers and `StringWriter` for CLI output assertions. Put add-in integration coverage under `tests/RevitCli.Addin.Tests/Integration/`. Run at least `dotnet test tests/RevitCli.Tests/` before submitting changes.

## Commit & Pull Request Guidelines

Use Conventional Commits seen in history: `feat:`, `fix:`, `test:`, `docs:`, `ci:`, and `chore:`. Example: `fix: handle null document path in status`. Pull requests should describe the change, link related issues, list test commands run, and mention any Revit-version or dashboard impact. Include screenshots only for visible dashboard changes.

## Security & Configuration Tips

Do not commit local `.revitcli.yml` secrets, NuGet API keys, generated publish output, or Revit install paths specific to a machine. Use `revitcli doctor` and `revitcli config show` when reporting setup issues.
