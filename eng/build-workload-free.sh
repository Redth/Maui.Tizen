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
# check <label> <command...> — runs the command, reports, and returns its status so
# callers can branch (e.g. to skip tests after a failed build).
check() {
  local label="$1"; shift
  if "$@" >/tmp/mt-check.$$ 2>&1; then
    pass "$label"
    rm -f /tmp/mt-check.$$
    return 0
  fi
  fail "$label"
  sed 's/^/        /' /tmp/mt-check.$$ | tail -30
  FAILURES=$((FAILURES + 1))
  rm -f /tmp/mt-check.$$
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
for f in eng/baselines.json eng/manifests/*.json; do
  [[ -e "$f" ]] || continue
  check "$f is well-formed JSON" python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$f"
done

# ---------------------------------------------------------------------------
# 2. Baseline consistency.
#
# Directory.Build.props and eng/baselines.json both declare the target framework.
# They drift silently and the symptom (API baselines generated for a different
# platform version) is expensive to diagnose, so check it cheaply here.
# ---------------------------------------------------------------------------
info "Baseline consistency"
check "Directory.Build.props TFM matches eng/baselines.json" python3 - <<'PY'
import json, re, sys

baselines = json.load(open("eng/baselines.json"))
expected = baselines["target"]["targetFramework"]

props = open("Directory.Build.props").read()
version = re.search(r"<TizenPlatformVersion>([^<]+)</TizenPlatformVersion>", props)
dotnet  = re.search(r"<DotNetVersion>([^<]+)</DotNetVersion>", props)
if not version or not dotnet:
    sys.exit("could not read TFM properties from Directory.Build.props")

actual = f"net{dotnet.group(1)}-tizen{version.group(1)}"
if actual != expected:
    sys.exit(f"Directory.Build.props builds '{actual}' but eng/baselines.json declares '{expected}'")

band = baselines["target"]["sdkBand"]
gj = json.load(open("global.json"))
sdk = gj["sdk"]["version"]
if not sdk.startswith(band):
    sys.exit(f"global.json SDK '{sdk}' is not in the declared band '{band}'")
if sdk == band:
    sys.exit(
        f"global.json SDK '{sdk}' is a bare band, not a resolvable SDK version. "
        "actions/setup-dotnet cannot install it. Pin a concrete version within the band.")
PY

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
  #   Maui.Tizen.Core.RefPackCompile  type-checks every `#if TIZEN` source, and the sample
  #                               head, against the REAL TizenFX reference assemblies from
  #                               Samsung.Tizen.Ref.API15. It is compile-only and unpackable,
  #                               so it cannot become a neutral fallback for the product.
  "tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj"
  "tests/Maui.Tizen.Core.UnitTests/Maui.Tizen.Core.UnitTests.csproj"

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
  ESSENTIALS_TESTS_MINIMUM=196

  check "essentials tests" "$ESSENTIALS_TESTS" \
    --report-xunit-junit --report-xunit-junit-filename results.xml \
    --results-directory "$ESSENTIALS_RESULTS"

  check "essentials tests ran at least $ESSENTIALS_TESTS_MINIMUM tests" \
    python3 "$REPO_ROOT/eng/assert-test-count.py" "$ESSENTIALS_RESULTS/results.xml" "$ESSENTIALS_TESTS_MINIMUM"
else
  fail "unit tests skipped - a preceding build failed (running --no-build now would only add cascading noise)"
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
# 6. Report the Tizen gate explicitly.
#
# Reported, never silently skipped. If this ever starts saying "available", the Tizen
# lane should be promoted to required.
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
  | grep -oE 'TizenWorkloadAvailable=[a-z]+' | head -1 | cut -d= -f2 || true)"

if [[ "$WORKLOAD_STATE" == "true" ]]; then
  pass "Samsung Tizen workload is installed - the Tizen lane can now be made required"
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
