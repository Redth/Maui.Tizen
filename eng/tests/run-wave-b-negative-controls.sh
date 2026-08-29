#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
MANIFEST="$REPO_ROOT/eng/tests/wave-b-mutations.json"
SCRATCH="$REPO_ROOT/artifacts/wave-b-mutations/$$"
CURRENT_FILE=""
CURRENT_BACKUP=""

cd "$REPO_ROOT"
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
}
trap cleanup EXIT INT TERM

INITIAL_STATUS="$(git status --porcelain=v1)"
COUNT="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$MANIFEST")"

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
done

FINAL_STATUS="$(git status --porcelain=v1)"
if [[ "$FINAL_STATUS" != "$INITIAL_STATUS" ]]; then
  echo "Mutation runner changed the working tree." >&2
  diff <(printf '%s\n' "$INITIAL_STATUS") <(printf '%s\n' "$FINAL_STATUS") || true
  exit 1
fi

printf 'All %s Wave B mutations failed their targeted tests and restored cleanly.\n' "$COUNT"
