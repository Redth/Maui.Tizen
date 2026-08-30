#!/usr/bin/env bash
#
# build-workload-free.sh — build and validate everything that does NOT require the
# Samsung Tizen workload.
#
# The Samsung .NET 11 workload has not shipped (eng/baselines.json > target.workloadManifest),
# so the net11.0-tizen11.0 projects cannot be restored or built by anyone yet. That is an
# external gate, not something this repository can engineer around.
#
# What this script does is make sure the gate is the ONLY thing standing between us and a
# working build: the SDK pin, central package management, package metadata, MSBuild
# conventions and the workload-independent code are all genuinely exercised here rather
# than sitting untested until Samsung publishes.
#
# Exit code is non-zero on real failure. A missing Tizen workload is NOT a failure for
# this lane - it is reported and expected.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"

# Run with CI semantics by default.
#
# This lane exists to catch problems before they reach CI, which it can only do if it
# applies the same rules. It previously did not: TreatWarningsAsErrors is conditioned on
# ContinuousIntegrationBuild, so a NuGet audit advisory (NU1903 on a vulnerable MSBuild
# package) passed locally and failed in CI. Set MAUI_TIZEN_LOCAL_SEMANTICS=1 to opt out.
if [[ "${MAUI_TIZEN_LOCAL_SEMANTICS:-0}" != "1" ]]; then
  export ContinuousIntegrationBuild=true
fi

pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m  FAIL\033[0m %s\n' "$*"; }
info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
note() { printf '\033[1;33m  GATE\033[0m %s\n' "$*"; }

FAILURES=0
CHECK_LOG_DIR="$REPO_ROOT/artifacts/logs/build-workload-free"
CHECK_LOG="$CHECK_LOG_DIR/check.$$.log"
mkdir -p "$CHECK_LOG_DIR"
trap 'rm -f "$CHECK_LOG"' EXIT

# check <label> <command...> — runs the command, reports, and returns its status so
# callers can branch (e.g. to skip tests after a failed build).
check() {
  local label="$1"; shift
  if "$@" >"$CHECK_LOG" 2>&1; then
    pass "$label"
    rm -f "$CHECK_LOG"
    return 0
  fi
  fail "$label"
  sed 's/^/        /' "$CHECK_LOG" | tail -30
  FAILURES=$((FAILURES + 1))
  rm -f "$CHECK_LOG"
  return 1
}

info "SDK"
"$DOTNET" --version | sed 's/^/  /'

# ---------------------------------------------------------------------------
# 1. JSON well-formedness.
#
# eng/baselines.json is consumed by the import script and the inventory tooling; a
# malformed file there breaks both in confusing ways.
# ---------------------------------------------------------------------------
info "JSON validation"
for f in eng/baselines.json eng/manifests/*.json eng/validation/*.json; do
  [[ -e "$f" ]] || continue
  check "$f is well-formed JSON" python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$f"
done

# ---------------------------------------------------------------------------
# 1b. Solution XML validation.
# ---------------------------------------------------------------------------
info "Solution validation"
check "Maui.Tizen.slnx is valid" "$DOTNET" sln Maui.Tizen.slnx list

# ---------------------------------------------------------------------------
# 2. Baseline consistency.
#
# Directory.Build.props, eng/Validation.Versions-equivalent properties and
# eng/baselines.json all restate parts of the target contract, and they drift silently.
#
# This check used to live here as an inline python snippet. It now lives in
# Maui.Tizen.Validation.Tests.RepositoryContractTests, which asserts the same invariants
# plus TizenManifestApiVersion and SDK band membership, and reports failures with the
# specific property that drifted. Keeping a second copy here meant two things to update
# and two places to disagree.
#
# Run it with ./eng/validation/run-hosted-validation.sh (the hosted-validation CI job).
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# 3. Import reproducibility contract.
#
# The provenance story depends on these files continuing to exist and be executable.
# ---------------------------------------------------------------------------
info "Import tooling"
for f in eng/import/filter-maui-tizen.sh eng/import/normalize-layout.sh eng/import/git-filter-repo eng/import/tizen-paths.txt; do
  check "$f present" test -f "$f"
done
check "filter script is syntactically valid" bash -n eng/import/filter-maui-tizen.sh
check "normalize script is syntactically valid" bash -n eng/import/normalize-layout.sh
check "Tizen workload gate script is syntactically valid" bash -n eng/ci/tizen-workload-gate.sh
check "real Tizen lane script is syntactically valid" bash -n eng/build-tizen.sh
check "Tizen workload transition tests are syntactically valid" bash -n eng/tests/test-ci-tizen-workload-gate.sh
check "release contract tests are syntactically valid" bash -n eng/tests/test-release-contract.sh
check "release contract helper is syntactically valid" \
  python3 -c "import ast; ast.parse(open('eng/release/release-contract.py', encoding='utf-8').read())"
check "Essentials mutation runner is syntactically valid" bash -n eng/tests/run-essentials-negative-controls.sh
check "Essentials mutation lock tests are syntactically valid" bash -n eng/tests/test-essentials-mutation-lock.sh
check "Wave C mutation runner is syntactically valid" bash -n eng/tests/run-wave-c-negative-controls.sh

# ---------------------------------------------------------------------------
# 4. Restore and build the workload-independent projects.
#
# This is the part that genuinely exercises the build configuration.
# ---------------------------------------------------------------------------
info "Workload-independent projects"
WORKLOAD_FREE_PROJECTS=(
  "src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj"
  "src/Maui.Tizen.Essentials.HostVerification/Maui.Tizen.Essentials.HostVerification.csproj"
  "tests/UnitTests/Maui.Tizen.UnitTests.csproj"

  # Verification lanes for the ported backend slice. Neither is a Tizen artifact:
  #
  #   Maui.Tizen.Core.UnitTests   compiles the backend against inert stand-ins for Tizen.NUI
  #                               and EXECUTES tests for the workload-independent behaviour
  #                               (mapper and DI registration, hosting, dispatching, density,
  #                               layout z-index ordering).
  #
  #   Maui.Tizen.Core.RefPackCompile  type-checks every `#if TIZEN` backend source against the
  #                               REAL TizenFX reference assemblies from Samsung.Tizen.Ref.API15,
  #                               and enforces the backend's PublicAPI baseline. Compile-only and
  #                               unpackable, so it cannot become a neutral fallback.
  #
  #   Maui.Tizen.Sample.RefPackCompile  compiles the sample head as its OWN assembly with a
  #                               ProjectReference to the backend, so the sample crosses a real
  #                               package boundary. It used to be folded into the backend lane,
  #                               which merged both into one assembly and meant the boundary was
  #                               never actually exercised - and left PublicAPI ownership
  #                               unverifiable, since either baseline satisfied either assembly.
  "tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj"
  #
  #   Maui.Tizen.Controls.RefPackCompile  compiles the Controls-to-Tizen mapper bridge as its own
  #                               assembly. Separate from the Core lane on purpose: the bridge
  #                               references Microsoft.Maui.Controls and Core must not, so merging
  #                               them would hide the dependency-direction mistake this layer
  #                               exists to avoid.
  "tests/Maui.Tizen.Sample.RefPackCompile/Maui.Tizen.Sample.RefPackCompile.csproj"
  "tests/Maui.Tizen.Controls.RefPackCompile/Maui.Tizen.Controls.RefPackCompile.csproj"
  "tests/Maui.Tizen.Controls.ConsumerCompile/Maui.Tizen.Controls.ConsumerCompile.csproj"
  "tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj"
  "tests/Controls.UnitTests/Maui.Tizen.Controls.UnitTests.csproj"
  "tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj"

  # Essentials verification lanes, mirroring the pair above:
  #
  #   Maui.Tizen.Essentials.RefPackCompile  type-checks the ported Essentials sources against the
  #                                         REAL API15 reference assemblies the product will bind
  #                                         to. Compile-only and unpackable. This is the lane that
  #                                         caught Tizen.Maps being removed from API15.
  #
  #   Maui.Tizen.Essentials.Tests           executes DI/facade/permission/translation tests against
  #                                         the loadable Tizen.NET assemblies via
  #                                         src/Maui.Tizen.Essentials.HostVerification.
  "tests/Maui.Tizen.Essentials.RefPackCompile/Maui.Tizen.Essentials.RefPackCompile.csproj"
  "tests/Maui.Tizen.Essentials.Tests/Maui.Tizen.Essentials.Tests.csproj"
  # Foundation-owned probes.
  "eng/tests/PublicApiOptIn/PublicApiOptIn.csproj"
  "eng/tests/PackReadmeProbe/PackReadmeProbe.csproj"
  "eng/tools/ApiDump/ApiDump.csproj"
  "eng/tools/SourceInventory/SourceInventory.csproj"
  "eng/tools/PackageVerify/PackageVerify.csproj"
  "tests/Migration.Tooling.Tests/Migration.Tooling.Tests.csproj"

  #   Maui.Tizen.BlazorWebView.Tests  the same two roles for the BlazorWebView package: it
  #                               compiles the handler sources and the Blazor sample head
  #                               against the Samsung reference assemblies, and EXECUTES
  #                               tests for registration order, the asset file provider,
  #                               request mapping and the static content cache.
  #                               See docs/blazorwebview.md.
  "tests/Maui.Tizen.BlazorWebView.Tests/Maui.Tizen.BlazorWebView.Tests.csproj"

  #   Maui.Tizen.BlazorWebView.PublicApi  compiles the BlazorWebView sources with the
  #                               PublicApiAnalyzers treated as errors. The shipping project
  #                               carries the analyzer too, but it is workload-gated and so never
  #                               actually runs it; this lane is where the baseline is enforced.
  "tests/Maui.Tizen.BlazorWebView.PublicApi/Maui.Tizen.BlazorWebView.PublicApi.csproj"
)
BUILD_OK=1
for proj in "${WORKLOAD_FREE_PROJECTS[@]}"; do
  check "restore $(basename "$proj")" "$DOTNET" restore "$proj"
  if check "build   $(basename "$proj")" "$DOTNET" build "$proj" --no-restore -c Release; then
    :
  else
    BUILD_OK=0
  fi
done

# ---------------------------------------------------------------------------
# 4b. Package graph probe.
#
# The real consumers of these packages are the net11.0-tizen11.0 projects, and they
# cannot restore at all - the workload gate fires before Restore. Without this probe a
# broken version pin stays invisible until the Samsung workload ships.
#
# It already earned its place: Microsoft.AspNetCore.Components.WebView was pinned to
# MAUI's version stamp, which does not exist for that package.
# ---------------------------------------------------------------------------
info "Package graph"
check "restore package graph probe" "$DOTNET" restore eng/tests/PackageGraphProbe/PackageGraphProbe.csproj

# ---------------------------------------------------------------------------
# 4c. Packing regression.
#
# Proves that a project opting into IsPackable from its own body still receives the
# README <None Pack="true"> item declared in Directory.Build.props, and therefore does
# not fail with NU5039. See eng/tests/PackReadmeProbe for why this is pinned rather than
# assumed - it is the kind of MSBuild evaluation-order question that is easy to reason
# about incorrectly in either direction.
# ---------------------------------------------------------------------------
info "Packing"
if check "pack README probe" "$DOTNET" pack eng/tests/PackReadmeProbe/PackReadmeProbe.csproj --no-restore -c Release; then
  README_NUPKG=""
  for candidate in "$REPO_ROOT"/artifacts/packages/Maui.Tizen.Internal.PackReadmeProbe.*.nupkg; do
    [[ -e "$candidate" ]] || continue
    if [[ -z "$README_NUPKG" || "$candidate" -nt "$README_NUPKG" ]]; then
      README_NUPKG="$candidate"
    fi
  done
  # NOTE: `unzip -l ... | grep -q` is deliberately avoided. Under `set -o pipefail`,
  # grep -q exits on first match and closes the pipe, so unzip can die with SIGPIPE and
  # poison the pipeline's status - reporting a missing README for a package that contains
  # one. That is the same failure that once made the CI provenance check report a present
  # commit as missing; `grep -c` consumes all input, so there is no early close.
  README_COUNT=0
  if [[ -n "$README_NUPKG" ]]; then
    README_COUNT="$(unzip -l "$README_NUPKG" 2>/dev/null | grep -c 'README\.md' || true)"
  fi
  if [[ "$README_COUNT" -gt 0 ]]; then
    pass "packed nupkg contains README.md"
  else
    fail "packed nupkg is missing README.md (NU5039 risk: the README Pack item did not apply)"
    FAILURES=$((FAILURES + 1))
  fi
fi

# ---------------------------------------------------------------------------
# 5. Repository invariant tests.
#
# These are the only tests that can meaningfully run before the workload ships: they
# check that the migration scaffolding is internally consistent rather than testing
# Tizen behaviour that nobody can execute yet.
#
# Skipped when the build failed. `dotnet test --no-build` against missing or stale
# output produces pages of cascading errors that bury the actual first failure.
# ---------------------------------------------------------------------------
info "Repository invariant tests"
if [[ $BUILD_OK -eq 1 ]]; then
  check "unit tests" "$DOTNET" test tests/UnitTests/Maui.Tizen.UnitTests.csproj --no-build -c Release
  check "backend slice tests" "$DOTNET" test tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj --no-build -c Release
  check "controls presentation tests" "$DOTNET" test tests/Controls.UnitTests/Maui.Tizen.Controls.UnitTests.csproj --no-build -c Release
  # Essentials behaviour tests, run against the workload-free host verification harness
  # (src/Maui.Tizen.Essentials.HostVerification), which compiles the same sources the Tizen
  # package will. Anything that P/Invokes into Tizen is out of their reach and is classified
  # in docs/tizen-essentials-service-coverage.md rather than faked into a green test.
  #
  # The self-executing Microsoft.Testing.Platform binary is run directly. `dotnet test` would
  # need the repository-wide dotnet.config MTP opt-in, which would break tests/UnitTests
  # (xunit v2 on VSTest). See that project's csproj for the full reasoning.
  ESSENTIALS_TESTS="artifacts/bin/Maui.Tizen.Essentials.Tests/Release/net11.0/Maui.Tizen.Essentials.Tests"
  ESSENTIALS_RESULTS="$REPO_ROOT/artifacts/test-results/essentials"

  # Assert the test COUNT, not just the exit code.
  #
  # This is not belt-and-braces. Discovery over this assembly walks assembly-level attributes
  # across the loaded closure, and Tizen.NUI's [XmlnsDefinition] constructor P/Invokes and
  # throws off-device. On the xunit v2 runner that aborted discovery part way through a class
  # and SILENTLY DROPPED the remaining tests while still reporting success - it hid 4 tests
  # before it was noticed. v3 handles it, but "the runner exited 0" is demonstrably not
  # sufficient evidence here, so the floor is pinned.
  #
  # The count comes from the runner's JUnit report rather than from scraping its console
  # summary: the first attempt at this parsed stdout and passed locally but failed on the CI
  # runner, because console rendering is not a stable contract. A report file is.
  #
  # Raise this when adding tests; never lower it to make a run go green.
  ESSENTIALS_TESTS_MINIMUM=449

  check "essentials tests" "$ESSENTIALS_TESTS" \
    --report-xunit-junit --report-xunit-junit-filename results.xml \
    --results-directory "$ESSENTIALS_RESULTS"

  check "essentials tests ran at least $ESSENTIALS_TESTS_MINIMUM tests" \
    python3 "$REPO_ROOT/eng/assert-test-count.py" "$ESSENTIALS_RESULTS/results.xml" "$ESSENTIALS_TESTS_MINIMUM"
  check "Wave B source tests" "$DOTNET" test tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj --no-build -c Release
  check "migration tooling tests" "$DOTNET" test tests/Migration.Tooling.Tests/Migration.Tooling.Tests.csproj --no-build -c Release
  check "Essentials negative controls" "$REPO_ROOT/eng/tests/run-essentials-negative-controls.sh"
  check "Essentials mutation lock behavior" "$REPO_ROOT/eng/tests/test-essentials-mutation-lock.sh"
  check "Wave B negative controls" env DOTNET="$DOTNET" "$REPO_ROOT/eng/tests/run-wave-b-negative-controls.sh"
  check "Wave B mutation runner behavior" "$REPO_ROOT/eng/tests/test-wave-b-mutation-runner.sh"
  check "Wave C negative controls" "$REPO_ROOT/eng/tests/run-wave-c-negative-controls.sh"
else
  fail "tests skipped - a preceding build failed (running --no-build now would only add cascading noise)"
  FAILURES=$((FAILURES + 1))
fi

# ---------------------------------------------------------------------------
# 5. Workload detection regressions.
#
# Detection decides whether anything in this repository can build, and it has been wrong
# in both directions during development. These fixtures pin the behaviour.
# ---------------------------------------------------------------------------
info "Workload detection regressions"
if DOTNET="$DOTNET" "$REPO_ROOT/eng/tests/test-workload-detection.sh"; then
  :
else
  fail "workload detection regressions failed"
  FAILURES=$((FAILURES + 1))
fi

# ---------------------------------------------------------------------------
# 5a. CI workload transition regressions.
#
# The external gate must stay informational while both baseline-derived manifest IDs are
# definitively unavailable, then become a mandatory real restore/build/pack lane as soon
# as either ID exists. These tests simulate both states without network or a workload.
# ---------------------------------------------------------------------------
info "Tizen workload CI transition regressions"
if "$REPO_ROOT/eng/tests/test-ci-tizen-workload-gate.sh"; then
  :
else
  fail "Tizen workload CI transition regressions failed"
  FAILURES=$((FAILURES + 1))
fi

# ---------------------------------------------------------------------------
# 5b. Release workflow contract regressions.
# ---------------------------------------------------------------------------
info "Release workflow contract regressions"
if "$REPO_ROOT/eng/tests/test-release-contract.sh"; then
  :
else
  fail "release workflow contract regressions failed"
  FAILURES=$((FAILURES + 1))
fi

# ---------------------------------------------------------------------------
# 5c. Snapshot verification regressions.
#
# eng/scripts/lib/Snapshot.ps1's Test-SnapshotIntegrity is what stands between "we downloaded
# the right dotnet/maui commit" and "we scanned whatever happened to be on disk". These
# fixtures exercise tamper/add/delete scenarios a marker-only (no-recompute) check would
# silently accept, entirely offline (synthetic directories, no network).
# ---------------------------------------------------------------------------
info "Snapshot verification regressions"
if pwsh -NoProfile -File "$REPO_ROOT/eng/tests/test-snapshot-verification.ps1"; then
  :
else
  fail "snapshot verification regressions failed"
  FAILURES=$((FAILURES + 1))
fi
# 5b. BlazorWebView host-side verification.
#
# Unlike the invariant tests, these exercise real backend behaviour: registration order,
# the asset file provider, request mapping and the static content cache. They can run
# because none of that code needs the native NUI WebView.
# ---------------------------------------------------------------------------
#
# Gated on BUILD_OK for the same reason as the invariant tests above: `--no-build` after a
# failed build silently runs whatever assemblies happen to be on disk from an earlier run,
# so a green result here could reflect stale artifacts rather than the current tree.
info "BlazorWebView host-side tests"
if [[ $BUILD_OK -eq 1 ]]; then
  check "blazorwebview tests" "$DOTNET" test tests/Maui.Tizen.BlazorWebView.Tests/Maui.Tizen.BlazorWebView.Tests.csproj --no-build -c Release
else
  fail "blazorwebview tests skipped - a preceding build failed (running --no-build now could pass against stale assemblies)"
  FAILURES=$((FAILURES + 1))
fi

# 7. Parity determinism.
#
# The full suite above is NOT sufficient evidence that parity generation is deterministic. MAUI's
# neutral mappers are mutated at runtime by Controls' RemapForControls, so a parity test can pass
# in the full suite purely because an earlier test already initialized Controls, while failing in a
# fresh process. This runs each parity-sensitive test alone to catch exactly that.
info "Parity isolation checks"
check "parity isolation" ./eng/run-parity-isolation-checks.sh

# ---------------------------------------------------------------------------
# 6. Report the Tizen gate explicitly.
#
# Reported, never silently skipped. CI uses the same detector after Samsung's supported
# installer and refuses to run the real Tizen lane unless it returns exactly "true".
#
# This asks MSBuild rather than parsing `dotnet workload list`. There is one detection
# implementation (the _DetectTizenWorkload target) so there is one thing to get right -
# and the previous shell probe, `dotnet workload list | grep -qi tizen`, matched an
# unrelated `maui-tizen` workload by substring and would have reported the gate as lifted
# while Samsung's workload was still absent.
# ---------------------------------------------------------------------------
info "Tizen workload gate"
WORKLOAD_STATE="$("$DOTNET" msbuild src/Maui.Tizen.Core/Maui.Tizen.Core.csproj \
  -t:ReportTizenWorkload -nologo -v:m 2>/dev/null \
  | grep -oE 'TizenWorkloadAvailable=[a-z]+' | tail -1 | cut -d= -f2 || true)"

if [[ "$WORKLOAD_STATE" == "true" ]]; then
  pass "Samsung Tizen workload is installed - CI will require the real Tizen lane"
elif [[ "$WORKLOAD_STATE" == "false" ]]; then
  note "Samsung Tizen workload is NOT installed."
  note "  net11.0-tizen11.0 cannot be restored or built until Samsung publishes"
  note "  an 11.0.100-band 'Samsung.NET.Sdk.Tizen.Manifest-11.0.100-preview.7' manifest."
  note "  This is expected; see docs/migration.md."
else
  fail "could not determine workload state (ReportTizenWorkload returned '${WORKLOAD_STATE:-<empty>}')"
  FAILURES=$((FAILURES + 1))
fi

echo
if [[ $FAILURES -gt 0 ]]; then
  fail "$FAILURES check(s) failed"
  exit 1
fi
pass "All workload-free checks passed"
