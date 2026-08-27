#!/usr/bin/env bash
#
# Runs each parity-sensitive test class ALONE in a fresh process.
#
# WHY THIS EXISTS
#
# MAUI's neutral mappers are mutated at runtime: Controls types call RemapForControls() when a MAUI
# host is built, which ADDS keys to static mappers such as FlyoutViewHandler.Mapper. Any snapshot of
# a mapper taken before that happens is missing those keys.
#
# The consequence is that a parity test can pass in the full suite purely because some earlier test
# already initialized Controls, while failing in a fresh process. That is exactly the bug this
# script guards: a green full suite is NOT sufficient evidence that parity generation is
# deterministic, because the full suite hides single-process ordering effects.
#
# Run this in addition to `dotnet test`, not instead of it.
#
# Usage:
#   ./eng/run-parity-isolation-checks.sh

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project="$repo_root/tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj"

# One entry per test that reads a neutral mapper. Each runs in its own process, so nothing else can
# have initialized Controls first.
tests=(
  "Maui.Tizen.SourceTests.WaveCMapperParityTests.ControlsRemapsAreDeterministic"
  "Maui.Tizen.SourceTests.WaveCMapperParityTests.ParityManifestMatchesSource"
  "Maui.Tizen.SourceTests.WaveCMapperParityTests.EveryNeutralMapperKeyIsImplementedOrRecorded"
  "Maui.Tizen.SourceTests.MapperParityTests.ParityManifestMatchesSource"
  "Maui.Tizen.SourceTests.MapperParityTests.EveryNeutralMapperKeyIsImplementedOrRecorded"
  "Maui.Tizen.SourceTests.WaveCNeutralKeyCoverageTests.WaveCLeavesNoNeutralMapperKeyUncovered"
)

echo "==> Building the source-test project once"
dotnet build "$project" -v q --nologo

failed=0

for test in "${tests[@]}"; do
  printf '  %-88s ' "$test"

  if dotnet test "$project" --no-build --nologo -v q --filter "FullyQualifiedName=$test" >/dev/null 2>&1; then
    echo "PASS"
  else
    echo "FAIL"
    failed=1
  fi
done

# The whole-class runs catch a snapshot shared across tests in one class, which the per-test runs
# above cannot see.
classes=(
  "Maui.Tizen.SourceTests.WaveCMapperParityTests"
  "Maui.Tizen.SourceTests.MapperParityTests"
)

for class in "${classes[@]}"; do
  printf '  %-88s ' "$class (whole class)"

  if dotnet test "$project" --no-build --nologo -v q --filter "FullyQualifiedName~$class" >/dev/null 2>&1; then
    echo "PASS"
  else
    echo "FAIL"
    failed=1
  fi
done

if [[ "$failed" -ne 0 ]]; then
  echo
  echo "FAIL: a parity test behaves differently when run alone."
  echo "      That means a mapper snapshot is being taken before the Controls remaps run."
  echo "      Check that every mapper-derived value in NeutralMaui is lazy and routed through"
  echo "      EnsureRemapsBeforeReadingMappers()."
  exit 1
fi

echo
echo "PASS: every parity test is order-independent."
