#!/usr/bin/env bash
# scripts/codex-tick.sh
#
# One tick of the RevitCli auto-iteration loop. Each successful tick
# advances exactly one checkbox in the active feature plan under
# .codex/features/.
#
# Run manually:   bash scripts/codex-tick.sh
# Or via cron / systemd --user timer (see docs/codex-orchestration.md).
#
# Exit codes:
#   0   tick succeeded (something was advanced OR cleanly noop'd)
#   1   tick failed (worker red after rescue, scope violation persisted,
#       or unrecoverable orchestrator failure)
#   2   HALT file present
#
# NOTE on `codex_call` invocation form: the exact Codex CLI surface has
# evolved over time. This script assumes
#   codex exec --agent <name> --config <path> --prompt-file <file>
# Adjust `codex_call` if your local Codex CLI uses different flags
# (e.g. `codex chat --agent ...` or stdin piping).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

STATE_DIR=".codex/state"
FEATURES_DIR=".codex/features"
PROMPTS_DIR=".codex/prompts"
CONFIG_FILE=".codex/config.toml"
mkdir -p "$STATE_DIR"

ts() { date -u +%H:%M:%SZ; }
log() { printf '[tick %s] %s\n' "$(ts)" "$*"; }
die() { log "ERROR: $*"; exit 1; }

write_changed_paths() {
  local out="$1"
  {
    git diff --name-only HEAD -- 2>/dev/null || true
    git ls-files --others --exclude-standard 2>/dev/null || true
  } | sed '/^$/d' | sort -u > "$out"
}

path_state_hash() {
  local f="$1"
  if [ -L "$f" ]; then
    readlink "$f" 2>/dev/null | sha256sum | awk '{print "symlink:" $1}'
  elif [ -f "$f" ]; then
    sha256sum -- "$f" | awk '{print "file:" $1}'
  elif [ -d "$f" ]; then
    printf 'dir'
  elif [ -e "$f" ]; then
    printf 'other'
  else
    printf 'missing'
  fi
}

write_changed_path_states() {
  local paths="$1"
  local out="$2"
  {
    while IFS= read -r f; do
      [ -z "$f" ] && continue
      printf '%s\t%s\n' "$f" "$(path_state_hash "$f")"
    done < "$paths"
  } | sort -k1,1 > "$out"
}

path_state_from_file() {
  local state_file="$1"
  local path="$2"
  awk -F '\t' -v path="$path" '$1 == path { print $2; found=1; exit } END { if (!found) print "" }' "$state_file"
}

# ── Step 0: HALT check ───────────────────────────────────────────────
if [ -f .codex/HALT ]; then
  log "HALT file present (.codex/HALT). Exiting without action."
  exit 2
fi

# ── Codex CLI wrapper ────────────────────────────────────────────────
# Override CODEX_BIN to point to your local Codex CLI binary.
CODEX_BIN="${CODEX_BIN:-codex}"

codex_call() {
  local agent="$1"; local prompt_file="$2"; local timeout_s="${3:-600}"
  log "→ codex agent=$agent timeout=${timeout_s}s prompt=$prompt_file"
  if ! command -v "$CODEX_BIN" >/dev/null 2>&1; then
    die "codex CLI not found in PATH (CODEX_BIN=$CODEX_BIN)"
  fi

  # Turn validity signal: agents read this and refuse if it doesn't match
  # their name. Written before invocation, deleted after. If a manual
  # codex call lands while no tick is active, the file is absent and the
  # agent refuses (see .codex/CONTRACT.md).
  echo "$agent" > "$STATE_DIR/current-agent.txt"
  local rc=0
  timeout "${timeout_s}s" "$CODEX_BIN" exec \
        --agent "$agent" \
        --config "$CONFIG_FILE" \
        --prompt-file "$prompt_file" || rc=$?
  rm -f "$STATE_DIR/current-agent.txt"

  if [ "$rc" != "0" ]; then
    if [ "$rc" = "124" ]; then
      log "agent $agent TIMED OUT after ${timeout_s}s"
    else
      log "agent $agent failed (exit $rc)"
    fi
    return "$rc"
  fi
}

# Always clear the current-agent signal on script exit so a crashed tick
# doesn't leave a stale "you may act" flag lying around.
cleanup_current_agent() {
  rm -f "$STATE_DIR/current-agent.txt" 2>/dev/null || true
}
trap cleanup_current_agent EXIT

# ── Step 1: snapshot git state ───────────────────────────────────────
git status -s > "$STATE_DIR/git-status.txt" || true
git diff --stat HEAD > "$STATE_DIR/git-diff-stat.txt" || true
BEFORE_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
BEFORE_PATHS_FILE="$STATE_DIR/git-paths-before.txt"
AFTER_PATHS_FILE="$STATE_DIR/git-paths-after.txt"
TICK_PATHS_FILE="$STATE_DIR/git-paths-created-this-tick.txt"
BEFORE_PATH_STATES_FILE="$STATE_DIR/git-path-states-before.txt"
AFTER_PATH_STATES_FILE="$STATE_DIR/git-path-states-after.txt"
PRE_EXISTING_PATHS_FILE="$STATE_DIR/git-paths-pre-existing-this-tick.txt"
PRE_EXISTING_MUTATED_PATHS_FILE="$STATE_DIR/git-paths-pre-existing-mutated-this-tick.txt"
write_changed_paths "$BEFORE_PATHS_FILE"
write_changed_path_states "$BEFORE_PATHS_FILE" "$BEFORE_PATH_STATES_FILE"

# ── Step 2: pick active feature ──────────────────────────────────────
CURRENT_FEATURE=""
for f in "$FEATURES_DIR"/*.md; do
  [ -e "$f" ] || continue
  if grep -q '^status: in-progress' "$f"; then
    CURRENT_FEATURE="$f"; break
  fi
done
if [ -z "$CURRENT_FEATURE" ]; then
  for f in "$FEATURES_DIR"/*.md; do
    [ -e "$f" ] || continue
    if grep -q '^status: planning' "$f"; then
      CURRENT_FEATURE="$f"; break
    fi
  done
fi

if [ -z "$CURRENT_FEATURE" ]; then
  log "No in-progress or planning feature. Invoking feature-architect."
  codex_call feature-architect "$PROMPTS_DIR/architect.md" 600 \
    || die "feature-architect failed"
  log "Architect run complete; rerun the tick to start work."
  exit 0
fi
log "Current feature: $CURRENT_FEATURE"
echo "$CURRENT_FEATURE" > "$STATE_DIR/current-feature.txt"

# ── Step 3: orchestrator decides next action ─────────────────────────
{
  echo "# Orchestrator context"
  echo
  echo "Feature file: $CURRENT_FEATURE"
  echo
  echo "## Feature contents"
  cat "$CURRENT_FEATURE"
  echo
  echo "## git status -s"
  cat "$STATE_DIR/git-status.txt"
  echo
  echo "## git diff --stat HEAD"
  cat "$STATE_DIR/git-diff-stat.txt"
  echo
  echo "## Last review (if any)"
  if [ -f "$STATE_DIR/last-review.md" ]; then
    cat "$STATE_DIR/last-review.md"
  else
    echo "(none yet)"
  fi
  echo
  echo "## Last tick summary (if any)"
  if [ -f "$STATE_DIR/last-tick.md" ]; then
    cat "$STATE_DIR/last-tick.md"
  else
    echo "(none yet)"
  fi
  echo
  echo "## Instructions"
  cat "$PROMPTS_DIR/orchestrator.md"
} > "$STATE_DIR/orchestrator-input.md"

rm -f "$STATE_DIR/next-action.json"
codex_call orchestrator "$STATE_DIR/orchestrator-input.md" 180 \
  || die "orchestrator call failed"
[ -f "$STATE_DIR/next-action.json" ] \
  || die "orchestrator did not produce next-action.json"

NEXT_AGENT=$(jq -r '.agent // ""'        "$STATE_DIR/next-action.json")
NEXT_CHECKBOX=$(jq -r '.checkbox // ""'  "$STATE_DIR/next-action.json")
NEXT_MAX_MIN=$(jq -r '.max_minutes // 10' "$STATE_DIR/next-action.json")
NEXT_RAT=$(jq -r '.rationale // ""'      "$STATE_DIR/next-action.json")

log "orchestrator → agent=$NEXT_AGENT checkbox=$NEXT_CHECKBOX (${NEXT_MAX_MIN}m)"
log "rationale: $NEXT_RAT"

if [ "$NEXT_AGENT" = "noop" ] || [ -z "$NEXT_AGENT" ]; then
  log "Nothing actionable this tick."
  printf 'feature=%s\nagent=noop\nrationale=%s\n' \
    "$CURRENT_FEATURE" "$NEXT_RAT" > "$STATE_DIR/last-tick.md"
  exit 0
fi

# ── Step 4: invoke the worker ────────────────────────────────────────
{
  echo "# Worker assignment"
  echo
  echo "Feature file: $CURRENT_FEATURE"
  echo "Checkbox: $NEXT_CHECKBOX"
  echo "Rationale: $NEXT_RAT"
  echo "Time budget: ${NEXT_MAX_MIN} minutes"
  echo
  echo "## Feature contents"
  cat "$CURRENT_FEATURE"
  echo
  echo "## Instructions"
  cat "$PROMPTS_DIR/worker.md"
} > "$STATE_DIR/worker-input.md"

WORKER_TIMEOUT=$((NEXT_MAX_MIN * 60))
codex_call "$NEXT_AGENT" "$STATE_DIR/worker-input.md" "$WORKER_TIMEOUT" \
  || log "(worker exited non-zero; scope and build steps will judge it)"

# ── Step 5: scope enforcement ────────────────────────────────────────
extract_paths() {
  local section="$1"; local file="$2"
  awk -v s="$section" '
    BEGIN { in_block=0; in_fm=0 }
    /^---$/ { in_fm = !in_fm; if (!in_fm) exit; next }
    in_fm && $1 == s":" { in_block=1; next }
    in_fm && in_block && /^[a-z]/ { in_block=0 }
    in_fm && in_block && /^  - / { sub(/^  - /,""); print }
  ' "$file"
}

mapfile -t SCOPE     < <(extract_paths "scope-paths"     "$CURRENT_FEATURE")
mapfile -t FORBIDDEN < <(extract_paths "forbidden-paths" "$CURRENT_FEATURE")

matches_pattern() {
  local file="$1" pat="$2"
  [ "$file" = "$pat" ] && return 0
  case "$pat" in
    */**)
      local prefix="${pat%/**}"
      case "$file" in "$prefix"/*) return 0 ;; esac ;;
    *\*)
      case "$file" in $pat) return 0 ;; esac ;;
    *)
      [ "$file" = "$pat" ] && return 0 ;;
  esac
  return 1
}

write_changed_paths "$AFTER_PATHS_FILE"
write_changed_path_states "$AFTER_PATHS_FILE" "$AFTER_PATH_STATES_FILE"
comm -12 "$BEFORE_PATHS_FILE" "$AFTER_PATHS_FILE" > "$PRE_EXISTING_PATHS_FILE" || true
comm -13 "$BEFORE_PATHS_FILE" "$AFTER_PATHS_FILE" > "$TICK_PATHS_FILE" || true
PRE_EXISTING_DIRTY_COUNT=$(wc -l < "$PRE_EXISTING_PATHS_FILE" | tr -d ' ')
TICK_CREATED_CHANGE_COUNT=$(wc -l < "$TICK_PATHS_FILE" | tr -d ' ')
if [ "$PRE_EXISTING_DIRTY_COUNT" -gt 0 ]; then
  log "Preserving $PRE_EXISTING_DIRTY_COUNT pre-existing dirty path(s); scope enforcement only reverts worker-created paths from this tick."
fi
: > "$PRE_EXISTING_MUTATED_PATHS_FILE"
while IFS= read -r f; do
  [ -z "$f" ] && continue
  BEFORE_STATE=$(path_state_from_file "$BEFORE_PATH_STATES_FILE" "$f")
  AFTER_STATE=$(path_state_from_file "$AFTER_PATH_STATES_FILE" "$f")
  if [ "$BEFORE_STATE" != "$AFTER_STATE" ]; then
    printf '%s\n' "$f" >> "$PRE_EXISTING_MUTATED_PATHS_FILE"
  fi
done < "$PRE_EXISTING_PATHS_FILE"
VIOLATIONS=0
while IFS= read -r f; do
  [ -z "$f" ] && continue
  # The active feature md is the control record for blocked/done state.
  # Workers may append honest blocker Notes; the tick owns done marking.
  if [ "$f" = "$CURRENT_FEATURE" ]; then
    continue
  fi
  # forbidden first
  for pat in "${FORBIDDEN[@]:-}"; do
    [ -z "$pat" ] && continue
    if matches_pattern "$f" "$pat"; then
      log "FORBIDDEN worker-created edit: $f (matches $pat) → reverting"
      git checkout -- "$f" 2>/dev/null || git clean -fd -- "$f" 2>/dev/null || true
      VIOLATIONS=$((VIOLATIONS+1))
      continue 2
    fi
  done
  # scope
  in_scope=false
  for pat in "${SCOPE[@]:-}"; do
    [ -z "$pat" ] && continue
    if matches_pattern "$f" "$pat"; then in_scope=true; break; fi
  done
  if ! $in_scope; then
    log "OUT OF SCOPE worker-created edit: $f → reverting"
    git checkout -- "$f" 2>/dev/null || git clean -fd -- "$f" 2>/dev/null || true
    VIOLATIONS=$((VIOLATIONS+1))
  fi
done < "$TICK_PATHS_FILE"
if [ "$VIOLATIONS" -gt 0 ]; then
  log "$VIOLATIONS scope violation(s) reverted."
fi

PRE_EXISTING_VIOLATIONS=0
while IFS= read -r f; do
  [ -z "$f" ] && continue
  # Pre-existing dirty paths may contain user work, so the tick must not
  # auto-revert them. Fail closed if the worker changed one out of scope.
  if [ "$f" = "$CURRENT_FEATURE" ]; then
    continue
  fi
  for pat in "${FORBIDDEN[@]:-}"; do
    [ -z "$pat" ] && continue
    if matches_pattern "$f" "$pat"; then
      log "FORBIDDEN pre-existing dirty path changed by worker: $f (matches $pat)"
      PRE_EXISTING_VIOLATIONS=$((PRE_EXISTING_VIOLATIONS+1))
      continue 2
    fi
  done

  in_scope=false
  for pat in "${SCOPE[@]:-}"; do
    [ -z "$pat" ] && continue
    if matches_pattern "$f" "$pat"; then in_scope=true; break; fi
  done
  if ! $in_scope; then
    log "OUT OF SCOPE pre-existing dirty path changed by worker: $f"
    PRE_EXISTING_VIOLATIONS=$((PRE_EXISTING_VIOLATIONS+1))
  fi
done < "$PRE_EXISTING_MUTATED_PATHS_FILE"
if [ "$PRE_EXISTING_VIOLATIONS" -gt 0 ]; then
  die "$PRE_EXISTING_VIOLATIONS pre-existing dirty path scope violation(s) detected; not reverting user work automatically."
fi

write_changed_paths "$AFTER_PATHS_FILE"
comm -13 "$BEFORE_PATHS_FILE" "$AFTER_PATHS_FILE" > "$TICK_PATHS_FILE" || true
TICK_CREATED_CHANGE_COUNT=$(wc -l < "$TICK_PATHS_FILE" | tr -d ' ')

# ── Step 6: build + test (with rescue) ───────────────────────────────
run_build_and_tests() {
  dotnet build src/RevitCli/RevitCli.csproj -c Debug -v q \
    > "$STATE_DIR/last-build.log" 2>&1 || return 1
  dotnet test  tests/RevitCli.Tests/ -v q \
    > "$STATE_DIR/last-test.log" 2>&1 || return 2
  return 0
}

RESCUE_COUNT_FILE="$STATE_DIR/rescue-count-${NEXT_CHECKBOX}.txt"
[ -f "$RESCUE_COUNT_FILE" ] || echo 0 > "$RESCUE_COUNT_FILE"

if ! run_build_and_tests; then
  rc=$?
  log "build/tests red (rc=$rc). Attempting rescue."
  COUNT=$(cat "$RESCUE_COUNT_FILE")
  if [ "$COUNT" -ge 2 ]; then
    log "Rescue cap reached for $NEXT_CHECKBOX (count=$COUNT). Marking blocked."
    sed -i 's/^status: in-progress/status: blocked/' "$CURRENT_FEATURE"
    {
      echo
      echo "## Notes"
      echo "- $(date -u +%FT%TZ) rescue cap exceeded on checkbox \`$NEXT_CHECKBOX\`."
      echo "  See \`.codex/state/last-build.log\` / \`last-test.log\`."
    } >> "$CURRENT_FEATURE"
    : > "$STATE_DIR/needs-attention.md"
    echo "feature=$CURRENT_FEATURE checkbox=$NEXT_CHECKBOX status=blocked" \
      >> "$STATE_DIR/needs-attention.md"
    die "Rescue cap exceeded."
  fi
  echo $((COUNT+1)) > "$RESCUE_COUNT_FILE"

  {
    echo "# Rescue context"
    echo "Feature: $CURRENT_FEATURE  Checkbox: $NEXT_CHECKBOX  Attempt: $((COUNT+1))/2"
    echo
    echo "## last-build.log"
    cat "$STATE_DIR/last-build.log"
    echo
    echo "## last-test.log"
    cat "$STATE_DIR/last-test.log"
    echo
    echo "## Instructions"
    cat "$PROMPTS_DIR/rescue.md"
  } > "$STATE_DIR/rescue-input.md"

  codex_call rescue-diagnostician "$STATE_DIR/rescue-input.md" 600 \
    || log "rescue agent itself errored"

  # Re-run build & test once
  if ! run_build_and_tests; then
    log "Still red after rescue. Marking blocked."
    sed -i 's/^status: in-progress/status: blocked/' "$CURRENT_FEATURE"
    die "build/tests still red after rescue"
  fi
  log "Rescue succeeded."
  # rescue success resets the counter for this checkbox
  echo 0 > "$RESCUE_COUNT_FILE"
fi

# ── Step 7: code review (read-only) ──────────────────────────────────
AFTER_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
WORKING_DIRTY=$([ -n "$(git status --porcelain)" ] && echo yes || echo no)

if [ "$AFTER_HEAD" != "$BEFORE_HEAD" ] || [ "$WORKING_DIRTY" = "yes" ]; then
  {
    echo "# Review request"
    echo "Feature: $CURRENT_FEATURE"
    echo "Checkbox: $NEXT_CHECKBOX"
    echo
    if [ -n "$BEFORE_HEAD" ] && [ "$AFTER_HEAD" != "$BEFORE_HEAD" ]; then
      echo "## Diff: $BEFORE_HEAD..$AFTER_HEAD"
      git diff "$BEFORE_HEAD..$AFTER_HEAD"
    else
      echo "## Working-tree diff (uncommitted)"
      git diff
    fi
    echo
    echo "## Instructions"
    cat "$PROMPTS_DIR/reviewer.md"
  } > "$STATE_DIR/reviewer-input.md"

  codex_call code-reviewer "$STATE_DIR/reviewer-input.md" 180 \
    || log "(reviewer call failed; continuing)"
fi

# ── Step 8: mark checkbox done in feature md ─────────────────────────
FEATURE_BLOCKED=$([ -f "$CURRENT_FEATURE" ] && grep -q '^status: blocked' "$CURRENT_FEATURE" && echo yes || echo no)
WORKER_MADE_PROGRESS=no
if [ "$AFTER_HEAD" != "$BEFORE_HEAD" ] || [ "${TICK_CREATED_CHANGE_COUNT:-0}" -gt 0 ]; then
  WORKER_MADE_PROGRESS=yes
fi

if [ "$FEATURE_BLOCKED" = "yes" ]; then
  log "Feature status is blocked; not marking checkbox '$NEXT_CHECKBOX' done."
elif [ "$WORKER_MADE_PROGRESS" != "yes" ]; then
  log "No worker-created changes detected; not marking checkbox '$NEXT_CHECKBOX' done."
elif grep -qE "^- \[ \] \*\*${NEXT_CHECKBOX}\*\*" "$CURRENT_FEATURE"; then
  sed -i "s/^- \[ \] \*\*${NEXT_CHECKBOX}\*\*/- [x] **${NEXT_CHECKBOX}**/" "$CURRENT_FEATURE"
  log "Checkbox '$NEXT_CHECKBOX' marked done."
fi

# Honor reviewer needs-revision
if [ -f "$STATE_DIR/last-review.md" ] && \
   grep -q '^Decision: needs-revision\|^needs-revision' "$STATE_DIR/last-review.md"; then
  log "Reviewer flagged needs-revision. Marking checkbox [!]."
  sed -i "s/^- \[x\] \*\*${NEXT_CHECKBOX}\*\*/- [!] **${NEXT_CHECKBOX}**/" "$CURRENT_FEATURE"
fi

# ── Step 9: commit ───────────────────────────────────────────────────
if [ -n "$(git status --porcelain)" ]; then
  git add -A
  FEATURE_TAG="$(basename "$CURRENT_FEATURE" .md)"
  git commit -m "feat(${FEATURE_TAG}): ${NEXT_CHECKBOX}

Tick: agent=${NEXT_AGENT}, checkbox=${NEXT_CHECKBOX}.
Rationale: ${NEXT_RAT}
Verify: see exit-criteria in ${CURRENT_FEATURE}

Co-Authored-By: Codex Tick <noreply@openai.com>" \
    || log "commit failed (no changes? hook rejected?)"
fi

# ── Step 10: feature complete? ───────────────────────────────────────
if ! grep -q '^- \[ \]' "$CURRENT_FEATURE"; then
  log "All checkboxes done."
  if ! grep -q '^- \[!\]' "$CURRENT_FEATURE"; then
    sed -i 's/^status: in-progress/status: done/' "$CURRENT_FEATURE"
    if ! grep -q '^done:' "$CURRENT_FEATURE"; then
      sed -i "/^created:/a done: $(date -u +%FT%TZ)" "$CURRENT_FEATURE"
    fi
    log "Feature marked done: $CURRENT_FEATURE"
  else
    log "Feature has unresolved [!] checkboxes; staying in-progress."
  fi
fi

# ── Wrap up ──────────────────────────────────────────────────────────
{
  printf 'tick=%s\nfeature=%s\nagent=%s\ncheckbox=%s\nrationale=%s\n' \
    "$(date -u +%FT%TZ)" "$CURRENT_FEATURE" \
    "$NEXT_AGENT" "$NEXT_CHECKBOX" "$NEXT_RAT"
} > "$STATE_DIR/last-tick.md"

log "Tick complete."
