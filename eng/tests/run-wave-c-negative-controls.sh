#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
MANIFEST="${WAVE_C_MUTATION_MANIFEST:-$REPO_ROOT/eng/tests/wave-c-mutations.json}"
SCRATCH="$REPO_ROOT/artifacts/wave-c-mutations/$$"
LOCK_ROOT="$REPO_ROOT/artifacts/locks"
LOCK_DIR="${WAVE_C_MUTATION_LOCK_DIR:-$LOCK_ROOT/wave-c-mutations.lock}"
WORKTREE_ID="$(git -C "$REPO_ROOT" rev-parse --path-format=absolute --git-dir)"
LOCK_TOKEN="$$-$(date +%s)-$RANDOM"
LOCK_OWNED=0
CURRENT_FILE=""
CURRENT_BACKUP=""
CURRENT_STAGED=""

cd "$REPO_ROOT"
mkdir -p "$LOCK_ROOT"

restore_current() {
  if [[ -n "$CURRENT_FILE" && -n "$CURRENT_BACKUP" && -f "$CURRENT_BACKUP" ]]; then
    local restore_path="$CURRENT_FILE.wave-c-restore-$LOCK_TOKEN"
    cp -p "$CURRENT_BACKUP" "$restore_path"
    mv -f "$restore_path" "$CURRENT_FILE"
  fi
  [[ -z "$CURRENT_STAGED" ]] || rm -f "$CURRENT_STAGED"
  if [[ "$LOCK_OWNED" -eq 1 ]]; then
    rm -f \
      "$LOCK_DIR/current.relative" \
      "$LOCK_DIR/current.backup" \
      "$LOCK_DIR/current.original-hash" \
      "$LOCK_DIR/current.mutated-hash" \
      "$LOCK_DIR/current.staged"
  fi
  CURRENT_FILE=""
  CURRENT_BACKUP=""
  CURRENT_STAGED=""
}

lock_value() {
  local name="$1"
  [[ -f "$LOCK_DIR/$name" ]] && sed -n '1p' "$LOCK_DIR/$name"
}

recover_stale_lock() {
  local owner_pid owner_worktree owner_identity owner_scratch scratch_pid relative backup backup_name original_hash mutated_hash staged target target_hash stale
  owner_pid="$(lock_value owner.pid)"
  owner_worktree="$(lock_value owner.worktree)"
  owner_identity="$(lock_value owner.identity)"
  owner_scratch="$(lock_value owner.scratch)"
  scratch_pid="${owner_scratch##*/}"

  if [[ "$owner_worktree" != "$WORKTREE_ID"
    || -z "$owner_identity"
    || "${owner_scratch%/*}" != "$REPO_ROOT/artifacts/wave-c-mutations"
    || ! "$scratch_pid" =~ ^[1-9][0-9]*$
    || "$scratch_pid" != "$owner_pid" ]]; then
    echo "Refusing to reclaim Wave C mutation lock with mismatched owner identity." >&2
    return 1
  fi
  if [[ "$owner_pid" =~ ^[1-9][0-9]*$ ]] && kill -0 "$owner_pid" 2>/dev/null; then
    echo "Another Wave C mutation runner (PID $owner_pid) owns '$LOCK_DIR'." >&2
    return 1
  fi

  relative="$(lock_value current.relative)"
  if [[ -n "$relative" ]]; then
    backup="$(lock_value current.backup)"
    original_hash="$(lock_value current.original-hash)"
    mutated_hash="$(lock_value current.mutated-hash)"
    staged="$(lock_value current.staged)"
    backup_name="${backup##*/}"
    if [[ "$relative" == /* || "$relative" == *".."*
      || "${backup%/*}" != "$owner_scratch"
      || ! "$backup_name" =~ ^[0-9]+\.original$
      || ! -f "$backup"
      || ! "$original_hash" =~ ^[0-9a-f]{64}$
      || ! "$mutated_hash" =~ ^[0-9a-f]{64}$ ]]; then
      echo "Refusing to reclaim malformed Wave C mutation recovery metadata." >&2
      return 1
    fi

    target="$REPO_ROOT/$relative"
    if [[ ! -f "$target" ]]; then
      echo "Refusing to restore a stale Wave C mutation whose target is missing." >&2
      return 1
    fi
    target_hash="$(shasum -a 256 "$target" | awk '{print $1}')"
    if [[ "$target_hash" == "$mutated_hash" ]]; then
      if [[ "$(shasum -a 256 "$backup" | awk '{print $1}')" != "$original_hash" ]]; then
        echo "Refusing to restore a stale Wave C mutation from a corrupt backup." >&2
        return 1
      fi
      local restore_path="$target.wave-c-stale-restore-$LOCK_TOKEN"
      cp -p "$backup" "$restore_path"
      mv -f "$restore_path" "$target"
      if [[ "$(shasum -a 256 "$target" | awk '{print $1}')" != "$original_hash" ]]; then
        echo "Stale Wave C mutation restoration did not reproduce the original bytes." >&2
        return 1
      fi
    elif [[ "$target_hash" != "$original_hash" ]]; then
      echo "Refusing to overwrite a source changed after the stale runner exited." >&2
      return 1
    fi

    if [[ "$staged" == "$target.wave-c-mutation-"* ]]; then
      rm -f "$staged"
    fi
  fi

  stale="$LOCK_DIR.stale-$LOCK_TOKEN"
  if ! mv "$LOCK_DIR" "$stale" 2>/dev/null; then
    return 1
  fi
  rm -rf "$stale"
  rm -rf "$owner_scratch"
  echo "Reclaimed stale Wave C mutation lock from PID ${owner_pid:-unknown}."
}

acquire_lock() {
  if ! mkdir "$LOCK_DIR" 2>/dev/null; then
    recover_stale_lock || return 1
    mkdir "$LOCK_DIR" 2>/dev/null || {
      echo "Another Wave C mutation runner acquired '$LOCK_DIR'." >&2
      return 1
    }
  fi

  LOCK_OWNED=1
  printf '%s\n' "$$" >"$LOCK_DIR/owner.pid"
  printf '%s\n' "$WORKTREE_ID" >"$LOCK_DIR/owner.worktree"
  printf '%s\n' "$LOCK_TOKEN" >"$LOCK_DIR/owner.identity"
  printf '%s\n' "$SCRATCH" >"$LOCK_DIR/owner.scratch"
}

cleanup() {
  restore_current
  rm -rf "$SCRATCH"
  if [[ "$LOCK_OWNED" -eq 1
    && "$(lock_value owner.identity)" == "$LOCK_TOKEN" ]]; then
    rm -f \
      "$LOCK_DIR/owner.pid" \
      "$LOCK_DIR/owner.worktree" \
      "$LOCK_DIR/owner.identity" \
      "$LOCK_DIR/owner.scratch"
    rmdir "$LOCK_DIR" 2>/dev/null || true
  fi
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

acquire_lock
mkdir -p "$SCRATCH"

INITIAL_STATUS="$(git status --porcelain=v1)"
COUNT="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$MANIFEST")"
if [[ ! "$COUNT" =~ ^[1-9][0-9]*$ ]]; then
  echo "Mutation manifest must contain at least one mutation." >&2
  exit 1
fi
EXECUTED=0
CANONICAL_OUTPUTS=(
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.Core.UnitTests/Release/net11.0/Maui.Tizen.Core.UnitTests.dll"
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.SourceTests/Release/net11.0/Maui.Tizen.SourceTests.dll"
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.Core.RefPackCompile/Release/net11.0/Maui.Tizen.Core.dll"
  "$REPO_ROOT/artifacts/bin/Maui.Tizen.Controls.RefPackCompile/Release/net11.0/Maui.Tizen.Controls.dll"
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

snapshot_canonical_outputs >"$SCRATCH/canonical-before-mutations.sha256"

for ((index = 0; index < COUNT; index++)); do
  mutation_json="$(python3 -c 'import json,sys; print(json.dumps(json.load(open(sys.argv[1]))[int(sys.argv[2])]))' "$MANIFEST" "$index")"
  mutation_id="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["id"])' "$mutation_json")"
  relative_file="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["file"])' "$mutation_json")"
  project="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["project"])' "$mutation_json")"
  filter="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["filter"])' "$mutation_json")"
  expected="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["expected"])' "$mutation_json")"

  CURRENT_FILE="$REPO_ROOT/$relative_file"
  CURRENT_BACKUP="$SCRATCH/$index.original"
  mutated="$SCRATCH/$index.mutated"
  CURRENT_STAGED="$CURRENT_FILE.wave-c-mutation-$LOCK_TOKEN"
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
  mutated_hash="$(shasum -a 256 "$mutated" | awk '{print $1}')"
  printf '%s\n' "$relative_file" >"$LOCK_DIR/current.relative"
  printf '%s\n' "$CURRENT_BACKUP" >"$LOCK_DIR/current.backup"
  printf '%s\n' "$original_hash" >"$LOCK_DIR/current.original-hash"
  printf '%s\n' "$mutated_hash" >"$LOCK_DIR/current.mutated-hash"
  printf '%s\n' "$CURRENT_STAGED" >"$LOCK_DIR/current.staged"
  cp -p "$mutated" "$CURRENT_STAGED"
  mv -f "$CURRENT_STAGED" "$CURRENT_FILE"
  CURRENT_STAGED=""

  if [[ "${WAVE_C_MUTATION_PAUSE_SECONDS:-0}" != "0" ]]; then
    touch "$SCRATCH/paused"
    sleep "$WAVE_C_MUTATION_PAUSE_SECONDS"
  fi

  set +e
  "$DOTNET" test "$project" -c Release --filter "$filter" \
    -p:ArtifactsDir="$SCRATCH/build/$index/" \
    --logger 'console;verbosity=minimal' >"$log" 2>&1
  status=$?
  set -e

  snapshot_canonical_outputs >"$SCRATCH/canonical-after-$index.sha256"
  if ! cmp -s \
    "$SCRATCH/canonical-before-mutations.sha256" \
    "$SCRATCH/canonical-after-$index.sha256"; then
    echo "Mutation '$mutation_id' changed a canonical output instead of its isolated artifacts tree." >&2
    exit 1
  fi

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

# Every mutation uses the ordinary project graph but redirects its complete artifacts tree. A
# SIGKILL can therefore leave only disposable scratch outputs, never a mutated canonical assembly.
# Rebuild the exact restored sources before leaving, then prove --no-build execution uses them.
"$DOTNET" build tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj \
  -c Release --no-restore --no-incremental -v:minimal >"$SCRATCH/final-core-build.log" 2>&1
"$DOTNET" build tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj \
  -c Release --no-restore --no-incremental -v:minimal >"$SCRATCH/final-source-build.log" 2>&1
"$DOTNET" build tests/Maui.Tizen.Controls.ConsumerCompile/Maui.Tizen.Controls.ConsumerCompile.csproj \
  -c Release --no-restore --no-incremental -v:minimal >"$SCRATCH/final-consumer-build.log" 2>&1

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

printf 'All %s Wave C mutations failed their targeted tests and restored cleanly.\n' "$COUNT"
