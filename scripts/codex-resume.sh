#!/usr/bin/env bash
# scripts/codex-resume.sh
# Clear the HALT flag so the next codex-tick.sh invocation runs normally.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HALT="$REPO_ROOT/.codex/HALT"
if [ -f "$HALT" ]; then
  rm -f "$HALT"
  echo "Halt flag cleared."
else
  echo "No halt flag present (already running)."
fi
