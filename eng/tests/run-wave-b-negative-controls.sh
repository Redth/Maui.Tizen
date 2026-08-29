#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
MANIFEST="${WAVE_B_MUTATION_MANIFEST:-$REPO_ROOT/eng/tests/wave-b-mutations.json}"
SCRATCH="$REPO_ROOT/artifacts/wave-b-mutations/$$"
LOCK_ROOT="$REPO_ROOT/artifacts/locks"
LOCK_DIR="${WAVE_B_MUTATION_LOCK_DIR:-$LOCK_ROOT/wave-b-mutations.lock}"
CURRENT_FILE=""
CURRENT_BACKUP=""

cd "$REPO_ROOT"
mkdir -p "$LOCK_ROOT"
if ! mkdir "$LOCK_DIR" 2>/dev/null; then
  echo "Another Wave B mutation runner owns '$LOCK_DIR'." >&2
  exit 1
fi
mkdir -p "$SCRATCH"

restore_current() {
  if [[ -n "$CURRENT_FILE" && -n "$CURRENT_BACKUP" && -f "$CURRENT_BACKUP" ]]; then
    cp "$CURRENT_BACKUP" "$CURRENT_FILE"
  fi
  CURRENT_FILE=""
  CURRENT_BACKUP=""
}

cleanup() {
  restore_current
  rm -rf "$SCRATCH"
  rmdir "$LOCK_DIR" 2>/dev/null || true
}
on_signal() {
  local status="$1"
  trap - EXIT INT TERM
  cleanup
  exit "$status"
}
trap cleanup EXIT
trap 'on_signal 130' INT
trap 'on_signal 143' TERM

INITIAL_STATUS="$(git status --porcelain=v1)"
COUNT="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$MANIFEST")"
if [[ ! "$COUNT" =~ ^[1-9][0-9]*$ ]]; then
  echo "Mutation manifest must contain at least one mutation." >&2
  exit 1
fi
EXECUTED=0

for ((index = 0; index < COUNT; index++)); do
  mutation_json="$(python3 -c 'import json,sys; print(json.dumps(json.load(open(sys.argv[1]))[int(sys.argv[2])]))' "$MANIFEST" "$index")"
  mutation_id="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["id"])' "$mutation_json")"
  relative_file="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["file"])' "$mutation_json")"
  project="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["project"])' "$mutation_json")"
  filter="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["filter"])' "$mutation_json")"
  expected="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["expected"])' "$mutation_json")"

  CURRENT_FILE="$REPO_ROOT/$relative_file"
  CURRENT_BACKUP="$SCRATCH/$index.original"
  log="$SCRATCH/$index.log"
  cp "$CURRENT_FILE" "$CURRENT_BACKUP"
  original_hash="$(shasum -a 256 "$CURRENT_FILE" | awk '{print $1}')"

  python3 - "$CURRENT_FILE" "$mutation_json" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
mutation = json.loads(sys.argv[2])
text = path.read_text()

for replacement in mutation["replacements"]:
    search = replacement["search"]
    count = text.count(search)
    if count != 1:
        raise SystemExit(
            f'{mutation["id"]}: expected exactly one patch location for {search!r}, found {count}')
    text = text.replace(search, replacement["replace"], 1)

path.write_text(text)
PY

  if [[ "${WAVE_B_MUTATION_PAUSE_SECONDS:-0}" != "0" ]]; then
    touch "$SCRATCH/paused"
    sleep "$WAVE_B_MUTATION_PAUSE_SECONDS"
  fi

  set +e
  "$DOTNET" test "$project" -c Release --no-restore --filter "$filter" \
    --logger 'console;verbosity=minimal' >"$log" 2>&1
  status=$?
  set -e

  if [[ $status -eq 0 ]]; then
    sed -n '1,120p' "$log"
    echo "Mutation '$mutation_id' did not fail its targeted test." >&2
    exit 1
  fi

  if ! grep -Fq "$expected" "$log" || ! grep -Eq 'A total of [1-9][0-9]* test files matched' "$log"; then
    sed -n '1,120p' "$log"
    echo "Mutation '$mutation_id' failed before nonzero test discovery or missed '$expected'." >&2
    exit 1
  fi

  restore_current
  restored_hash="$(shasum -a 256 "$REPO_ROOT/$relative_file" | awk '{print $1}')"
  if [[ "$restored_hash" != "$original_hash" ]]; then
    echo "Mutation '$mutation_id' did not restore exact original bytes." >&2
    exit 1
  fi

  printf 'PASS %s\n' "$mutation_id"
  EXECUTED=$((EXECUTED + 1))
done

if [[ "$EXECUTED" -eq 0 || "$EXECUTED" -ne "$COUNT" ]]; then
  echo "Mutation runner executed $EXECUTED of $COUNT mutations." >&2
  exit 1
fi

# Every mutation builds into the ordinary project graph so the test runner exercises the same
# compilation users do. Rebuild the exact restored sources before leaving, then prove --no-build
# execution uses those clean outputs rather than a surviving mutated assembly.
"$DOTNET" build tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj \
  -c Release --no-restore -v:minimal >"$SCRATCH/final-core-build.log" 2>&1
"$DOTNET" build tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj \
  -c Release --no-restore -v:minimal >"$SCRATCH/final-source-build.log" 2>&1
"$DOTNET" build tests/Maui.Tizen.Controls.ConsumerCompile/Maui.Tizen.Controls.ConsumerCompile.csproj \
  -c Release --no-restore -v:minimal >"$SCRATCH/final-consumer-build.log" 2>&1

"$DOTNET" test tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~ReservedOperationRejectsStalePreparedReplacement' \
  --logger 'console;verbosity=minimal' >"$SCRATCH/final-core-test.log" 2>&1
"$DOTNET" test tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~MapperParityTests.ParityManifestMatchesSource' \
  --logger 'console;verbosity=minimal' >"$SCRATCH/final-source-test.log" 2>&1

CORE_ASSEMBLY="$REPO_ROOT/artifacts/bin/Maui.Tizen.Core.UnitTests/Release/net11.0/Maui.Tizen.Core.UnitTests.dll"
SOURCE_ASSEMBLY="$REPO_ROOT/artifacts/bin/Maui.Tizen.SourceTests/Release/net11.0/Maui.Tizen.SourceTests.dll"
test -f "$CORE_ASSEMBLY"
test -f "$SOURCE_ASSEMBLY"

if find src tests/Maui.Tizen.Core.UnitTests -name '*.cs' -newer "$CORE_ASSEMBLY" -print -quit | grep -q .; then
  echo "Core test output is older than restored source." >&2
  exit 1
fi
if find tests/Maui.Tizen.SourceTests eng/Maui.Tizen.Core.Sources.props -newer "$SOURCE_ASSEMBLY" -print -quit | grep -q .; then
  echo "Source-test output is older than restored inputs." >&2
  exit 1
fi

FINAL_STATUS="$(git status --porcelain=v1)"
if [[ "$FINAL_STATUS" != "$INITIAL_STATUS" ]]; then
  echo "Mutation runner changed the working tree." >&2
  diff <(printf '%s\n' "$INITIAL_STATUS") <(printf '%s\n' "$FINAL_STATUS") || true
  exit 1
fi

printf 'All %s Wave B mutations failed their targeted tests and restored cleanly.\n' "$COUNT"
