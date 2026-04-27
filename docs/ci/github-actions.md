# RevitCli on GitHub Actions

Wire `revitcli check` into a pull-request workflow in two steps:

1. Add `permissions: { security-events: write }` to your job (Code Scanning
   needs it to ingest SARIF).
2. Reference the bundled composite action at
   `.github/actions/revitcli-check`.

## Quickstart

```yaml
# .github/workflows/revitcli-check.yml
name: RevitCli Check
on:
  pull_request:
permissions:
  contents: read
  security-events: write
jobs:
  check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: ./.github/actions/revitcli-check
        with:
          revitcli-version: '1.7.0'   # pin in production
          profile: '.revitcli.yml'    # optional; auto-discovered when omitted
```

Findings appear on the PR's **Files changed** tab (Code Scanning annotations)
and the **Security** tab.

## Webhook payloads

`revitcli check` honours `defaults.notify` from the project profile. When set
to an HTTPS URL, every successful run POSTs the following JSON:

```json
{
  "event": "check",
  "name": "default",
  "passed": 12,
  "failed": 1,
  "suppressed": 0,
  "severityFailed": true,
  "timestamp": "2026-04-27T12:34:56.7890000Z",
  "profilePath": "/repo/.revitcli.yml"
}
```

`severityFailed` mirrors the command's exit code: `true` when the check would
exit non-zero per the profile's `failOn` policy. Webhook delivery is
best-effort — failures emit a stderr warning but never change the exit code.

See [`.github/actions/revitcli-check/README.md`](../../.github/actions/revitcli-check/README.md)
for the action's input reference.
