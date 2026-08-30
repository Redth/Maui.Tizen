#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUNNER="$REPO_ROOT/eng/tests/run-essentials-negative-controls.sh"
LOCK_DIR="$REPO_ROOT/artifacts/locks/essentials-mutation-lock-test.lock"
LOG_DIR="$REPO_ROOT/artifacts/essentials-mutation-lock-tests"
WORKTREE_ID="$(git -C "$REPO_ROOT" rev-parse --path-format=absolute --git-dir)"
CHILD_PID=""

cleanup() {
  if [[ -n "$CHILD_PID" ]] && kill -0 "$CHILD_PID" 2>/dev/null; then
    kill -TERM "$CHILD_PID" 2>/dev/null || true
    wait "$CHILD_PID" 2>/dev/null || true
  fi
  rm -rf "$LOCK_DIR" "$LOG_DIR"
}
trap cleanup EXIT INT TERM

mkdir -p "$LOG_DIR" "$(dirname "$LOCK_DIR")"

# A failed contender must not remove a live owner's lock.
mkdir "$LOCK_DIR"
printf '%s\n' "$$" >"$LOCK_DIR/owner.pid"
printf '%s\n' "$WORKTREE_ID" >"$LOCK_DIR/owner.worktree"
printf '%s\n' "live-owner" >"$LOCK_DIR/owner.identity"
printf '%s\n' "$REPO_ROOT/artifacts/essentials-mutations/$$" >"$LOCK_DIR/owner.scratch"

set +e
ESSENTIALS_MUTATION_LOCK_DIR="$LOCK_DIR" \
ESSENTIALS_MUTATION_LOCK_ONLY=1 \
  "$RUNNER" >"$LOG_DIR/contender.log" 2>&1
status=$?
set -e

[[ "$status" -ne 0 ]]
[[ -d "$LOCK_DIR" ]]
grep -Fq "owns '$LOCK_DIR'" "$LOG_DIR/contender.log"
rm -rf "$LOCK_DIR"
echo "PASS failed contender preserves live owner lock"

# A terminated owner cleans only its own lock.
ESSENTIALS_MUTATION_LOCK_DIR="$LOCK_DIR" \
ESSENTIALS_MUTATION_LOCK_ONLY=1 \
ESSENTIALS_MUTATION_LOCK_HOLD_SECONDS=60 \
  "$RUNNER" >"$LOG_DIR/term.log" 2>&1 &
CHILD_PID=$!
for _ in {1..100}; do
  [[ -f "$LOCK_DIR/owner.pid" ]] && break
  sleep 0.02
done
[[ -f "$LOCK_DIR/owner.pid" ]]
kill -TERM "$CHILD_PID"
wait "$CHILD_PID" 2>/dev/null || true
CHILD_PID=""
[[ ! -e "$LOCK_DIR" ]]
echo "PASS terminated owner removes its own lock"

# SIGKILL cannot run cleanup. The next owner must verify metadata, reclaim, and remove it.
ESSENTIALS_MUTATION_LOCK_DIR="$LOCK_DIR" \
ESSENTIALS_MUTATION_LOCK_ONLY=1 \
ESSENTIALS_MUTATION_LOCK_HOLD_SECONDS=60 \
  "$RUNNER" >"$LOG_DIR/kill.log" 2>&1 &
CHILD_PID=$!
for _ in {1..100}; do
  [[ -f "$LOCK_DIR/owner.pid" ]] && break
  sleep 0.02
done
[[ -f "$LOCK_DIR/owner.pid" ]]
kill -KILL "$CHILD_PID"
wait "$CHILD_PID" 2>/dev/null || true
CHILD_PID=""
[[ -d "$LOCK_DIR" ]]

ESSENTIALS_MUTATION_LOCK_DIR="$LOCK_DIR" \
ESSENTIALS_MUTATION_LOCK_ONLY=1 \
  "$RUNNER" >"$LOG_DIR/reclaim.log" 2>&1
grep -Fq "Reclaimed stale Essentials mutation lock" "$LOG_DIR/reclaim.log"
[[ ! -e "$LOCK_DIR" ]]
echo "PASS stale SIGKILL lock is safely reclaimed"
