#!/usr/bin/env bash
#
# run-hosted-validation.sh - run every validation suite that needs no Tizen workload.
#
# WHY THIS EXISTS RATHER THAN `dotnet test`
#
# The validation suites use xunit v3, which runs on Microsoft.Testing.Platform. The .NET 10+ SDK
# removed VSTest support for that platform, so `dotnet test` refuses to run them unless global.json
# opts into the Microsoft.Testing.Platform runner - and that opt-in would simultaneously break
# tests/UnitTests, which is still xunit v2 on VSTest.
#
# Rather than force one of those two migrations as a side effect of adding a validation lane, the v3
# suites are executed directly. They are self-hosting executables, so this is a supported and
# entirely ordinary way to run them.
#
# Once tests/UnitTests moves to xunit v3, add
#   "test": { "runner": "Microsoft.Testing.Platform" }
# to global.json and this script can collapse to a single `dotnet test`.
# RepositoryContractTests.TestRunnerSplit_IsRecordedRatherThanAssumed guards that transition.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
RESULTS_DIR="${RESULTS_DIR:-$REPO_ROOT/artifacts/test-results}"

# Suites are listed explicitly rather than globbed. A suite that silently stops being discovered is
# indistinguishable from one that passes.
SUITES=(
  "tests/Maui.Tizen.Validation.Tests/Maui.Tizen.Validation.Tests.csproj"
  "tests/Maui.Tizen.Build.Tests/Maui.Tizen.Build.Tests.csproj"
  "tests/Maui.Tizen.Conventions.Tests/Maui.Tizen.Conventions.Tests.csproj"
  "tests/Maui.Tizen.DevFlow.Tests/Maui.Tizen.DevFlow.Tests.csproj"
  "tests/Maui.Tizen.Consumer.Tests/Maui.Tizen.Consumer.Tests.csproj"
)

# Run with CI semantics by default, mirroring eng/build-workload-free.sh.
#
# TreatWarningsAsErrors is conditioned on ContinuousIntegrationBuild, so without this a locally
# green run can still fail in CI. That happened: xUnit1051 (async calls should flow
# TestContext.Current.CancellationToken) is a warning locally and an error in CI, and the first
# push failed on it. Set MAUI_TIZEN_LOCAL_SEMANTICS=1 to opt out.
if [[ "${MAUI_TIZEN_LOCAL_SEMANTICS:-0}" != "1" ]]; then
  export ContinuousIntegrationBuild=true
fi

pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m  FAIL\033[0m %s\n' "$*"; }
info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

FAILURES=0

mkdir -p "$RESULTS_DIR"

info "SDK"
"$DOTNET" --version | sed 's/^/  /'

info "Building validation suites ($CONFIGURATION)"
for suite in "${SUITES[@]}"; do
  if ! "$DOTNET" build "$suite" -c "$CONFIGURATION" --nologo -v q >/tmp/mt-build.$$ 2>&1; then
    fail "build $(basename "$suite" .csproj)"
    sed 's/^/        /' /tmp/mt-build.$$ | tail -40
    FAILURES=$((FAILURES + 1))
  else
    pass "build $(basename "$suite" .csproj)"
  fi
  rm -f /tmp/mt-build.$$
done

if [[ $FAILURES -gt 0 ]]; then
  fail "$FAILURES suite(s) failed to build"
  exit 1
fi

info "Running validation suites"
for suite in "${SUITES[@]}"; do
  name="$(basename "$suite" .csproj)"

  # Ask MSBuild where the binary is rather than guessing a path. The repository sets a custom
  # BaseOutputPath (artifacts/bin/<Project>/), so a hard-coded bin/<config>/<tfm> path would break.
  target_path="$("$DOTNET" msbuild "$suite" -getProperty:TargetPath -p:Configuration="$CONFIGURATION" -v:q 2>/dev/null | tail -1 | tr -d '\r')"
  binary="${target_path%.dll}"

  if [[ ! -x "$binary" ]]; then
    fail "$name: no runnable test binary at '$binary'"
    FAILURES=$((FAILURES + 1))
    continue
  fi

  if "$binary" -result-trx "$RESULTS_DIR/$name.trx" >/tmp/mt-test.$$ 2>&1; then
    pass "$name"
    # Surface skips even on success: a suite that quietly skips everything is the failure mode
    # this whole lane is designed to avoid.
    grep -E '\[SKIP\]' -A 1 /tmp/mt-test.$$ | sed 's/^/        /' || true
    grep -E 'Total:' /tmp/mt-test.$$ | sed 's/^/        /' || true
  else
    fail "$name"
    sed 's/^/        /' /tmp/mt-test.$$ | tail -60
    FAILURES=$((FAILURES + 1))
  fi
  rm -f /tmp/mt-test.$$
done

echo
if [[ $FAILURES -gt 0 ]]; then
  fail "$FAILURES validation suite(s) failed"
  exit 1
fi

pass "All hosted validation suites passed"
info "Results: $RESULTS_DIR"
