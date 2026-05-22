#!/usr/bin/env bash
# scripts/codex-halt.sh
# Pause the auto-iteration loop. Any in-progress tick continues, but the
# next invocation of codex-tick.sh exits immediately.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$REPO_ROOT/.codex"
touch "$REPO_ROOT/.codex/HALT"
echo "Halt flag set at $REPO_ROOT/.codex/HALT"
echo "Run scripts/codex-resume.sh to resume."
