#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
MANIFEST="${ESSENTIALS_MUTATION_MANIFEST:-$REPO_ROOT/eng/tests/essentials-mutations.json}"
SCRATCH="$REPO_ROOT/artifacts/essentials-mutations/$$"
LOCK_ROOT="$REPO_ROOT/artifacts/locks"
LOCK_DIR="${ESSENTIALS_MUTATION_LOCK_DIR:-$LOCK_ROOT/essentials-mutations.lock}"
CURRENT_FILE=""
CURRENT_BACKUP=""
CURRENT_STAGED=""

cd "$REPO_ROOT"
mkdir -p "$LOCK_ROOT"

restore_current() {
  if [[ -n "$CURRENT_FILE" && -n "$CURRENT_BACKUP" && -f "$CURRENT_BACKUP" ]]; then
    local restore_path="$CURRENT_FILE.essentials-restore-$$"
    cp -p "$CURRENT_BACKUP" "$restore_path"
    mv -f "$restore_path" "$CURRENT_FILE"
  fi
  [[ -z "$CURRENT_STAGED" ]] || rm -f "$CURRENT_STAGED"
  CURRENT_FILE=""
  CURRENT_BACKUP=""
  CURRENT_STAGED=""
}

cleanup() {
  restore_current
  rm -rf "$SCRATCH"
  rmdir "$LOCK_DIR" 2>/dev/null || true
}

trap cleanup EXIT INT TERM

if ! mkdir "$LOCK_DIR" 2>/dev/null; then
  echo "Another Essentials mutation runner owns '$LOCK_DIR'." >&2
  exit 1
fi

mkdir -p "$SCRATCH"
INITIAL_STATUS="$(git status --porcelain=v1)"
COUNT="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$MANIFEST")"

if [[ ! "$COUNT" =~ ^[1-9][0-9]*$ ]]; then
  echo "Essentials mutation manifest must contain at least one mutation." >&2
  exit 1
fi

CANONICAL_OUTPUTS=(
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.Essentials.Tests/Release/net11.0/Maui.Tizen.Essentials.Tests.dll"
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.SourceTests/Release/net11.0/Maui.Tizen.SourceTests.dll"
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.Core.UnitTests/Release/net11.0/Maui.Tizen.Core.UnitTests.dll"
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.Essentials.RefPackCompile/Release/net11.0/Maui.Tizen.Essentials.dll"
)

snapshot_canonical_outputs() {
  local output
  for output in "${CANONICAL_OUTPUTS[@]}"; do
    if [[ -f "$output" ]]; then
      printf '%s %s\n' "$(shasum -a 256 "$output" | awk '{print $1}')" "$output"
    else
      printf 'MISSING %s\n' "$output"
    fi
  done
}

for project in \
  tests/Maui.Tizen.Essentials.Tests/Maui.Tizen.Essentials.Tests.csproj \
  tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj \
  tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj \
  tests/Maui.Tizen.Essentials.RefPackCompile/Maui.Tizen.Essentials.RefPackCompile.csproj; do
  "$DOTNET" build "$project" -c Release -v:minimal >"$SCRATCH/$(basename "$project").build.log" 2>&1
done

snapshot_canonical_outputs >"$SCRATCH/canonical-before.sha256"

for ((index = 0; index < COUNT; index++)); do
  mutation_json="$(python3 -c 'import json,sys; print(json.dumps(json.load(open(sys.argv[1]))[int(sys.argv[2])]))' "$MANIFEST" "$index")"
  mutation_id="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["id"])' "$mutation_json")"
  relative_file="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["file"])' "$mutation_json")"
  filter="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["filter"])' "$mutation_json")"
  expected="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["expected"])' "$mutation_json")"
  project="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["project"])' "$mutation_json")"
  mode="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1]).get("mode", "build"))' "$mutation_json")"

  CURRENT_FILE="$REPO_ROOT/$relative_file"
  CURRENT_BACKUP="$SCRATCH/$index.original"
  mutated="$SCRATCH/$index.mutated"
  CURRENT_STAGED="$CURRENT_FILE.essentials-mutation-$$"
  log="$SCRATCH/$index.log"
  cp -p "$CURRENT_FILE" "$CURRENT_BACKUP"
  original_hash="$(shasum -a 256 "$CURRENT_FILE" | awk '{print $1}')"

  python3 - "$CURRENT_BACKUP" "$mutated" "$mutation_json" <<'PY'
import json
import pathlib
import sys

source = pathlib.Path(sys.argv[1])
destination = pathlib.Path(sys.argv[2])
mutation = json.loads(sys.argv[3])
text = source.read_text()

for replacement in mutation["replacements"]:
    search = replacement["search"]
    count = text.count(search)
    if count != 1:
        raise SystemExit(
            f'{mutation["id"]}: expected exactly one patch location for {search!r}, found {count}')
    text = text.replace(search, replacement["replace"], 1)

destination.write_text(text)
PY

  cp -p "$mutated" "$CURRENT_STAGED"
  mv -f "$CURRENT_STAGED" "$CURRENT_FILE"
  CURRENT_STAGED=""

  set +e
  mtp=0
  if [[ "$mode" == "source" ]]; then
    "$DOTNET" test "$project" \
      -c Release --no-build \
      --filter "$filter" \
      --logger 'console;verbosity=minimal' >"$log" 2>&1
    status=$?
  elif [[ "$project" == "tests/Maui.Tizen.Essentials.Tests/Maui.Tizen.Essentials.Tests.csproj" ]]; then
    mtp=1
    isolated="$SCRATCH/build/$index/"
    "$DOTNET" build "$project" \
      -c Release \
      -p:ArtifactsDir="$isolated" \
      -v:minimal >"$log" 2>&1
    build_status=$?
    if [[ $build_status -eq 0 ]]; then
      executable="$isolated/bin/Maui.Tizen.Essentials.Tests/Release/net11.0/Maui.Tizen.Essentials.Tests"
      test_list="$SCRATCH/$index.tests.json"
      "$executable" --list-tests json >"$test_list" 2>>"$log"
      uid_args=()
      while IFS= read -r uid; do
        uid_args+=(--filter-uid "$uid")
      done < <(python3 - "$test_list" "$expected" <<'PY'
import json
import sys

document = json.load(open(sys.argv[1]))
expected = sys.argv[2]
matches = [test["uid"] for test in document["tests"] if expected in test["displayName"]]
if not matches:
    raise SystemExit(f"No MTP test UID matched {expected!r}")
print(*matches, sep="\n")
PY
)
      "$executable" "${uid_args[@]}" --minimum-expected-tests 1 >>"$log" 2>&1
      status=$?
    else
      status=$build_status
    fi
  else
    "$DOTNET" test "$project" \
      -c Release \
      -p:ArtifactsDir="$SCRATCH/build/$index/" \
      --filter "$filter" \
      --logger 'console;verbosity=minimal' >"$log" 2>&1
    status=$?
  fi
  set -e

  snapshot_canonical_outputs >"$SCRATCH/canonical-after-$index.sha256"
  if ! cmp -s "$SCRATCH/canonical-before.sha256" "$SCRATCH/canonical-after-$index.sha256"; then
    echo "Mutation '$mutation_id' changed canonical outputs." >&2
    exit 1
  fi

  if [[ $status -eq 0 ]]; then
    sed -n '1,120p' "$log"
    echo "Mutation '$mutation_id' did not fail its targeted test." >&2
    exit 1
  fi

  if ! grep -Fq "$expected" "$log" ||
    ! grep -Eq 'A total of [1-9][0-9]* test files matched|Total: [1-9][0-9]*|total: [1-9][0-9]*' "$log"; then
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

for project in \
  tests/Maui.Tizen.Essentials.Tests/Maui.Tizen.Essentials.Tests.csproj \
  tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj \
  tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj \
  tests/Maui.Tizen.Essentials.RefPackCompile/Maui.Tizen.Essentials.RefPackCompile.csproj; do
  "$DOTNET" build "$project" -c Release --no-restore --no-incremental -v:minimal \
    >"$SCRATCH/$(basename "$project").final.log" 2>&1
done

"$DOTNET" test tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~SharedSourceClosureMatchesEveryShippingEssentialsSource' \
  --logger 'console;verbosity=minimal' >"$SCRATCH/final-source-test.log" 2>&1

snapshot_canonical_outputs >"$SCRATCH/canonical-final.sha256"
if grep -q '^MISSING ' "$SCRATCH/canonical-final.sha256"; then
  echo "Canonical Essentials outputs were not rebuilt." >&2
  exit 1
fi

FINAL_STATUS="$(git status --porcelain=v1)"
if [[ "$FINAL_STATUS" != "$INITIAL_STATUS" ]]; then
  echo "Essentials mutation runner changed the working tree." >&2
  diff <(printf '%s\n' "$INITIAL_STATUS") <(printf '%s\n' "$FINAL_STATUS") || true
  exit 1
fi

printf 'All %s Essentials mutations failed their targeted tests and restored cleanly.\n' "$COUNT"
