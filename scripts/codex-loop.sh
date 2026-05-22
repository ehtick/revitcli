#!/usr/bin/env bash
# scripts/codex-loop.sh
# Daemon mode for the auto-iteration loop. Runs scripts/codex-tick.sh in a
# bounded loop with a sleep between ticks. Exits when:
#   - .codex/HALT is present
#   - the active feature reaches `status: done`
#   - MAX_ITERS is reached
#   - a tick fails RESCUE_LIMIT consecutive times
#
# Environment knobs:
#   INTERVAL_SECS=60       # sleep between ticks
#   MAX_ITERS=200          # safety cap
#   RESCUE_LIMIT=3         # consecutive failed ticks before giving up
#
# Usage:
#   bash scripts/codex-loop.sh
#   INTERVAL_SECS=30 MAX_ITERS=50 bash scripts/codex-loop.sh
#
# Run in foreground so you can Ctrl-C, or via:
#   nohup bash scripts/codex-loop.sh > .codex/state/loop.log 2>&1 &

set -uo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

INTERVAL_SECS="${INTERVAL_SECS:-60}"
MAX_ITERS="${MAX_ITERS:-200}"
RESCUE_LIMIT="${RESCUE_LIMIT:-3}"

ts() { date -u +%FT%TZ; }
log() { printf '[loop %s] %s\n' "$(ts)" "$*"; }

consec_fail=0

for i in $(seq 1 "$MAX_ITERS"); do
  if [ -f .codex/HALT ]; then
    log "HALT present; exiting after $((i-1)) tick(s)."
    exit 0
  fi

  # All features done?
  active=""
  for f in .codex/features/*.md; do
    [ -e "$f" ] || continue
    if grep -qE '^status: (in-progress|planning|blocked)' "$f"; then
      active="$f"; break
    fi
  done
  if [ -z "$active" ]; then
    log "No active feature; loop done after $((i-1)) tick(s)."
    exit 0
  fi

  log "tick $i/$MAX_ITERS (feature=$active)"
  if bash scripts/codex-tick.sh; then
    consec_fail=0
  else
    rc=$?
    consec_fail=$((consec_fail + 1))
    log "tick exited $rc (consecutive fail = $consec_fail/$RESCUE_LIMIT)"
    if [ "$consec_fail" -ge "$RESCUE_LIMIT" ]; then
      log "Rescue limit hit; pausing loop. Inspect .codex/state/needs-attention.md."
      exit 1
    fi
  fi

  log "sleeping ${INTERVAL_SECS}s"
  sleep "$INTERVAL_SECS"
done

log "Max iterations ($MAX_ITERS) reached."
