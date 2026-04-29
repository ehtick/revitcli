# RevitCli Check (composite action)

Runs `revitcli check` against a checked-out repository and uploads the resulting
SARIF report to GitHub Code Scanning. Drop this in any workflow to surface
RevitCli findings on the PR's **Security** tab and on the diff itself.

## Inputs

| Input               | Required | Default     | Description                                                                |
| ------------------- | -------- | ----------- | -------------------------------------------------------------------------- |
| `revitcli-version`  | no       | `latest`    | RevitCli .NET tool version. Pin to a specific version in production runs.  |
| `profile`           | no       | `''`        | Path to a `.revitcli.yml` profile (relative to `working-directory`).       |
| `working-directory` | no       | `.`         | Directory to run the check in.                                             |

## Required permissions

Code Scanning ingests SARIF via the `security-events: write` permission. Add it
at job (or workflow) scope:

```yaml
permissions:
  contents: read
  security-events: write
```

## Usage

```yaml
name: RevitCli Check
on:
  pull_request:
permissions:
  contents: read
  security-events: write
jobs:
  check:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: ./.github/actions/revitcli-check
        with:
          revitcli-version: '1.7.0'
          profile: '.revitcli.yml'
```

## Notes

- The composite step pins both `actions/setup-dotnet` and
  `github/codeql-action/upload-sarif` to commit SHAs. Update the SHA **and**
  the trailing version comment in `action.yml` together when bumping.
- Revit can only be driven on Windows. Use `windows-latest` for profile/SARIF
  lint and a self-hosted Windows runner when a live Revit document is needed.
- The SARIF file is written to `${{ runner.temp }}/revitcli.sarif`.
