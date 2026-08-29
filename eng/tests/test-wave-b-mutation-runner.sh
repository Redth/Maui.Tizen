#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUNNER="$REPO_ROOT/eng/tests/run-wave-b-negative-controls.sh"
MANIFEST="$REPO_ROOT/eng/tests/wave-b-mutations.json"
SCRATCH="$REPO_ROOT/artifacts/wave-b-runner-tests/$$"
EMPTY_MANIFEST="$SCRATCH/empty.json"
LOCK_DIR="$REPO_ROOT/artifacts/locks/wave-b-runner-test-$$.lock"
TARGET="$REPO_ROOT/samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj"
runner_pid=""

cleanup() {
  if [[ -n "$runner_pid" ]] && kill -0 "$runner_pid" 2>/dev/null; then
    kill -TERM "$runner_pid"
    wait "$runner_pid" 2>/dev/null || true
  fi
  rm -rf "$SCRATCH"
  rmdir "$LOCK_DIR" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

mkdir -p "$SCRATCH"
printf '[]\n' >"$EMPTY_MANIFEST"

if WAVE_B_MUTATION_MANIFEST="$EMPTY_MANIFEST" \
   WAVE_B_MUTATION_LOCK_DIR="$LOCK_DIR" \
   "$RUNNER" >"$SCRATCH/empty.log" 2>&1; then
  echo "Empty mutation manifest was accepted." >&2
  exit 1
fi
grep -q 'at least one mutation' "$SCRATCH/empty.log"

initial_status="$(git -C "$REPO_ROOT" status --porcelain=v1)"
initial_hash="$(shasum -a 256 "$TARGET" | awk '{print $1}')"

WAVE_B_MUTATION_MANIFEST="$MANIFEST" \
WAVE_B_MUTATION_LOCK_DIR="$LOCK_DIR" \
WAVE_B_MUTATION_PAUSE_SECONDS=30 \
"$RUNNER" >"$SCRATCH/signal.log" 2>&1 &
runner_pid=$!

pause_marker="$REPO_ROOT/artifacts/wave-b-mutations/$runner_pid/paused"
for _ in {1..100}; do
  [[ -f "$pause_marker" ]] && break
  sleep 0.05
done
test -f "$pause_marker"

kill -TERM "$runner_pid"
set +e
wait "$runner_pid"
status=$?
set -e
runner_pid=""

if [[ "$status" -ne 143 ]]; then
  cat "$SCRATCH/signal.log"
  echo "TERM returned $status instead of 143." >&2
  exit 1
fi

test ! -d "$LOCK_DIR"
test "$(shasum -a 256 "$TARGET" | awk '{print $1}')" = "$initial_hash"
test "$(git -C "$REPO_ROOT" status --porcelain=v1)" = "$initial_status"

LOCK_DIR="$REPO_ROOT/artifacts/locks/wave-b-runner-test-int-$$.lock"
status="$(python3 - "$RUNNER" "$MANIFEST" "$LOCK_DIR" "$REPO_ROOT" "$SCRATCH/signal-int.log" <<'PY'
import os
import pathlib
import signal
import subprocess
import sys
import time

runner, manifest, lock_dir, repo_root, log_path = sys.argv[1:]
env = os.environ.copy()
env["WAVE_B_MUTATION_MANIFEST"] = manifest
env["WAVE_B_MUTATION_LOCK_DIR"] = lock_dir
env["WAVE_B_MUTATION_PAUSE_SECONDS"] = "30"

def reset_signals():
    signal.signal(signal.SIGINT, signal.SIG_DFL)

with open(log_path, "wb") as log:
    process = subprocess.Popen(
        [runner],
        cwd=repo_root,
        env=env,
        stdout=log,
        stderr=subprocess.STDOUT,
        preexec_fn=reset_signals)

    marker = pathlib.Path(repo_root) / "artifacts" / "wave-b-mutations" / str(process.pid) / "paused"
    for _ in range(100):
        if marker.exists():
            break
        time.sleep(0.05)
    else:
        process.terminate()
        process.wait()
        raise SystemExit("INT runner never reached pause marker")

    process.send_signal(signal.SIGINT)
    print(process.wait())
PY
)"

if [[ "$status" -ne 130 ]]; then
  cat "$SCRATCH/signal-int.log"
  echo "INT returned $status instead of 130." >&2
  exit 1
fi

test ! -d "$LOCK_DIR"
test "$(shasum -a 256 "$TARGET" | awk '{print $1}')" = "$initial_hash"
test "$(git -C "$REPO_ROOT" status --porcelain=v1)" = "$initial_status"

echo "PASS mutation runner empty-manifest, TERM and INT cleanup behavior"
