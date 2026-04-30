# Release Checklist

Use this checklist before pushing a version tag. RevitCli is Windows/Revit-first:
do not ship a release that only passed Linux or documentation checks.

## 1. Preflight

```powershell
git status --short
dotnet restore
dotnet build
dotnet test tests/RevitCli.Tests/
```

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

For source-tree validation with custom Revit paths:

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
  -Filter "Mark = W-01"
```

For 2024/2025, record gaps if a runner or local install is unavailable. Do not
claim live support beyond the versions that passed smoke. Use
[revit2026-real-smoke.md](revit2026-real-smoke.md) for the evidence packet.

## 4. Journal Integrity

After a smoke that writes or exports, verify the local journal:

```powershell
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
