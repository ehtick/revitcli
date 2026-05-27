#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/smoke-revit-wsl.sh [options]

Runs a read-only/dry-run live Revit 2026 smoke from WSL by invoking the
installed Windows RevitCli.exe. It does not apply model writes.

Options:
  --revitcli <path>      Windows RevitCli.exe path
  --element-id <id>      Element id to query
  --category <name>      Category for filtered query and dry-run set
  --filter <expr>        Filter expression expected to match the element
  --param <name>         Writable text parameter used for dry-run preview
  --value <value>        Dry-run value
  --output-dir <path>    Evidence directory
  --require-current-source
                         Fail when installed Windows CLI/add-in commit differs
                         from the current source HEAD
  -h, --help             Show this help

Environment overrides:
  REVITCLI_WINDOWS_EXE
  REVITCLI_SMOKE_ELEMENT_ID
  REVITCLI_SMOKE_CATEGORY
  REVITCLI_SMOKE_FILTER
  REVITCLI_SMOKE_PARAM
  REVITCLI_SMOKE_VALUE
  REVITCLI_WSL_SMOKE_OUTPUT_DIR
  REVITCLI_REVIT2026_INSTALL_DIR
EOF
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
timestamp="$(date -u +%Y%m%d-%H%M%S)"
revitcli="${REVITCLI_WINDOWS_EXE:-/mnt/c/Users/Lenovo/AppData/Local/RevitCli/bin/RevitCli.exe}"
element_id="${REVITCLI_SMOKE_ELEMENT_ID:-337596}"
category="${REVITCLI_SMOKE_CATEGORY:-walls}"
filter="${REVITCLI_SMOKE_FILTER:-标记 = TEST}"
param="${REVITCLI_SMOKE_PARAM:-注释}"
value="${REVITCLI_SMOKE_VALUE:-revitcli-v6-wsl-smoke-${timestamp}}"
output_dir="${REVITCLI_WSL_SMOKE_OUTPUT_DIR:-${repo_root}/.artifacts/live-smoke/revit2026-wsl-${timestamp}}"
revit2026_install_dir="${REVITCLI_REVIT2026_INSTALL_DIR:-D:\\revit2026\\Revit 2026}"
require_current_source=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --revitcli)
      revitcli="$2"
      shift 2
      ;;
    --element-id)
      element_id="$2"
      shift 2
      ;;
    --category)
      category="$2"
      shift 2
      ;;
    --filter)
      filter="$2"
      shift 2
      ;;
    --param)
      param="$2"
      shift 2
      ;;
    --value)
      value="$2"
      shift 2
      ;;
    --output-dir)
      output_dir="$2"
      shift 2
      ;;
    --require-current-source)
      require_current_source=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ ! -x "$revitcli" ]]; then
  echo "Windows RevitCli.exe is not executable: $revitcli" >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to build the WSL live smoke summary.json." >&2
  exit 1
fi

mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"

run_step() {
  local name="$1"
  shift
  local stdout_path="${output_dir}/${name}.out"
  local stderr_path="${output_dir}/${name}.err"
  printf '%s\n' "$*" > "${output_dir}/${name}.cmd"
  if "$revitcli" "$@" >"$stdout_path" 2>"$stderr_path"; then
    printf 'pass\n' > "${output_dir}/${name}.status"
  else
    local exit_code=$?
    printf 'fail:%s\n' "$exit_code" > "${output_dir}/${name}.status"
    return "$exit_code"
  fi
}

escape_ps_single_quoted() {
  printf "%s" "$1" | sed "s/'/''/g"
}

git -C "$repo_root" rev-parse HEAD > "${output_dir}/source-head.txt"
printf '%s\n' "$revitcli" > "${output_dir}/windows-cli-path.txt"
printf '%s\n' "$element_id" > "${output_dir}/element-id.txt"
printf '%s\n' "$category" > "${output_dir}/category.txt"
printf '%s\n' "$filter" > "${output_dir}/filter.txt"
printf '%s\n' "$param" > "${output_dir}/param.txt"
printf '%s\n' "$value" > "${output_dir}/dry-run-value.txt"

run_step doctor doctor --check-version 2026 --output json
run_step status status --output json
run_step query-id query --id "$element_id" --output json
run_step query-filter query "$category" --filter "$filter" --output json
run_step set-dry-run set "$category" --filter "$filter" --param "$param" --value "$value" --dry-run

source_head="$(cat "${output_dir}/source-head.txt")"
doctor_success="$(jq -r '.success == true' "${output_dir}/doctor.out")"
doctor_target_year="$(jq -r '.targetRevitYear // empty' "${output_dir}/doctor.out")"
cli_version="$(jq -r '[.checks[] | select(.name == "CLI version")][0].message // "" | sub("^CLI version: "; "")' "${output_dir}/doctor.out")"
installed_addin_version="$(jq -r '[.checks[] | select(.name == "Installed Add-in version")][0].message // "" | sub("^Installed Add-in version: "; "")' "${output_dir}/doctor.out")"
live_addin_version="$(jq -r '[.checks[] | select(.name == "Live Add-in version")][0].message // "" | sub("^Live Add-in version: "; "")' "${output_dir}/doctor.out")"
status_revit_year="$(jq -r '.revitYear // empty' "${output_dir}/status.out")"
status_document="$(jq -r '.documentName // empty' "${output_dir}/status.out")"
status_addin_version="$(jq -r '.addinVersion // empty' "${output_dir}/status.out")"
query_id_count="$(jq -r 'length' "${output_dir}/query-id.out")"
query_filter_count="$(jq -r 'length' "${output_dir}/query-filter.out")"
query_id_first="$(jq -r '.[0].id // empty' "${output_dir}/query-id.out")"
query_filter_first="$(jq -r '.[0].id // empty' "${output_dir}/query-filter.out")"
dry_run_preview_count="$(sed -n 's/^Dry run: \([0-9][0-9]*\) element(s) would be modified\..*/\1/p' "${output_dir}/set-dry-run.out" | head -n 1)"
dry_run_preview_count="${dry_run_preview_count:-0}"
extract_commit() {
  local version="$1"
  if [[ "$version" == *"+"* ]]; then
    printf '%s\n' "${version##*+}"
  fi
}

cli_commit="$(extract_commit "$cli_version")"
installed_addin_commit="$(extract_commit "$installed_addin_version")"
live_addin_commit="$(extract_commit "$live_addin_version")"
status_addin_commit="$(extract_commit "$status_addin_version")"
current_source_installed=false
if [[ "$cli_commit" == "$source_head" &&
      "$installed_addin_commit" == "$source_head" &&
      "$live_addin_commit" == "$source_head" &&
      "$status_addin_commit" == "$source_head" ]]; then
  current_source_installed=true
fi
source_installed_drift=false
if [[ "$current_source_installed" != "true" ]]; then
  source_installed_drift=true
fi
repo_root_windows="$repo_root"
if command -v wslpath >/dev/null 2>&1; then
  repo_root_windows="$(wslpath -w "$repo_root")"
fi
install_handoff_path=""
install_handoff_windows_path=""
post_restart_command="scripts/smoke-revit-wsl.sh --require-current-source"
next_actions_json='[]'
if [[ "$source_installed_drift" == "true" ]]; then
  install_handoff_path="${output_dir}/install-current-source.ps1"
  install_handoff_windows_path="$install_handoff_path"
  if command -v wslpath >/dev/null 2>&1; then
    install_handoff_windows_path="$(wslpath -w "$install_handoff_path")"
  fi
  repo_root_ps="$(escape_ps_single_quoted "$repo_root_windows")"
  revit_install_ps="$(escape_ps_single_quoted "$revit2026_install_dir")"
  cat > "$install_handoff_path" <<EOF
\$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath '$repo_root_ps'
& .\scripts\install-current-source-revit2026.ps1 -Revit2026InstallDir '$revit_install_ps'
Write-Host 'Restart Revit 2026, then rerun from WSL: scripts/smoke-revit-wsl.sh --require-current-source'
EOF
  next_actions_json='[
    "close Revit when convenient",
    "run generated install-current-source.ps1 from Windows PowerShell",
    "restart Revit 2026 to activate any staged add-in",
    "rerun scripts/smoke-revit-wsl.sh --require-current-source"
  ]'
fi
overall_success=false
if [[ "$doctor_success" == "true" &&
      "$status_revit_year" == "2026" &&
      "$query_id_count" == "1" &&
      "$query_filter_count" == "1" &&
      "$query_id_first" == "$query_filter_first" &&
      "$dry_run_preview_count" == "1" ]]; then
  overall_success=true
fi
if [[ "$require_current_source" == "true" && "$source_installed_drift" == "true" ]]; then
  overall_success=false
fi

jq -n \
  --arg schemaVersion "revitcli-wsl-live-smoke.v1" \
  --arg sourceHead "$source_head" \
  --arg windowsCliPath "$revitcli" \
  --arg cliVersion "$cli_version" \
  --arg installedAddinVersion "$installed_addin_version" \
  --arg liveAddinVersion "$live_addin_version" \
  --arg statusAddinVersion "$status_addin_version" \
  --arg cliCommit "$cli_commit" \
  --arg installedAddinCommit "$installed_addin_commit" \
  --arg liveAddinCommit "$live_addin_commit" \
  --arg statusAddinCommit "$status_addin_commit" \
  --arg installHandoffPath "$install_handoff_path" \
  --arg installHandoffWindowsPath "$install_handoff_windows_path" \
  --arg postRestartCommand "$post_restart_command" \
  --arg documentName "$status_document" \
  --arg category "$category" \
  --arg filter "$filter" \
  --arg param "$param" \
  --arg value "$value" \
  --argjson success "$overall_success" \
  --argjson requireCurrentSource "$require_current_source" \
  --argjson doctorSuccess "$doctor_success" \
  --argjson targetRevitYear "${doctor_target_year:-0}" \
  --argjson revitYear "${status_revit_year:-0}" \
  --argjson sourceInstalledDrift "$source_installed_drift" \
  --argjson currentSourceInstalled "$current_source_installed" \
  --argjson elementId "$element_id" \
  --argjson queryIdCount "$query_id_count" \
  --argjson queryFilterCount "$query_filter_count" \
  --arg queryIdFirst "$query_id_first" \
  --arg queryFilterFirst "$query_filter_first" \
  --argjson dryRunPreviewCount "$dry_run_preview_count" \
  --argjson nextActions "$next_actions_json" \
  '{
    schemaVersion: $schemaVersion,
    success: $success,
    sourceHead: $sourceHead,
    windowsCliPath: $windowsCliPath,
    versions: {
      cli: $cliVersion,
      installedAddin: $installedAddinVersion,
      liveAddin: $liveAddinVersion,
      statusAddin: $statusAddinVersion,
      cliCommit: $cliCommit,
      installedAddinCommit: $installedAddinCommit,
      liveAddinCommit: $liveAddinCommit,
      statusAddinCommit: $statusAddinCommit,
      sourceInstalledDrift: $sourceInstalledDrift
    },
    installHandoff: {
      path: $installHandoffPath,
      windowsPath: $installHandoffWindowsPath,
      postRestartCommand: $postRestartCommand
    },
    revit: {
      doctorSuccess: $doctorSuccess,
      targetRevitYear: $targetRevitYear,
      statusRevitYear: $revitYear,
      documentName: $documentName
    },
    query: {
      elementId: $elementId,
      category: $category,
      filter: $filter,
      queryIdCount: $queryIdCount,
      queryFilterCount: $queryFilterCount,
      queryIdFirst: $queryIdFirst,
      queryFilterFirst: $queryFilterFirst
    },
    dryRun: {
      param: $param,
      value: $value,
      previewCount: $dryRunPreviewCount
    },
    boundary: {
      noYesArgument: true,
      mutatesModel: false,
      currentSourceInstalled: $currentSourceInstalled,
      requireCurrentSource: $requireCurrentSource
    },
    nextActions: $nextActions
  }' > "${output_dir}/summary.json"

if [[ "$overall_success" != "true" ]]; then
  echo "WSL live smoke summary reported success=false: ${output_dir}/summary.json" >&2
  if [[ "$require_current_source" == "true" && "$source_installed_drift" == "true" ]]; then
    echo "Installed Windows CLI/add-in or live Revit add-in commit differs from source HEAD." >&2
  fi
  exit 1
fi

cat > "${output_dir}/README.md" <<EOF
# RevitCli WSL Live Smoke Evidence

- Source HEAD: $(cat "${output_dir}/source-head.txt")
- Windows CLI: ${revitcli}
- Summary: summary.json
- Element: ${element_id}
- Category: ${category}
- Filter: ${filter}
- Dry-run parameter: ${param}
- Dry-run value: ${value}

This WSL helper invokes the installed Windows RevitCli.exe and only runs
doctor, status, query, and set --dry-run. It does not pass --yes and does not
apply model writes.
EOF

echo "Wrote WSL live smoke evidence to: $output_dir"
