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
check() {
  local label="$1"; shift
  if "$@" >/tmp/mt-check.$$ 2>&1; then
    pass "$label"
  else
    fail "$label"
    sed 's/^/        /' /tmp/mt-check.$$ | tail -30
    FAILURES=$((FAILURES + 1))
  fi
  rm -f /tmp/mt-check.$$
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
if not gj["sdk"]["version"].startswith(band.rsplit('.', 1)[0]):
    sys.exit(f"global.json SDK '{gj['sdk']['version']}' does not match declared band '{band}'")
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
  "tests/UnitTests/Maui.Tizen.UnitTests.csproj"
)
for proj in "${WORKLOAD_FREE_PROJECTS[@]}"; do
  check "restore $(basename "$proj")" "$DOTNET" restore "$proj"
  check "build   $(basename "$proj")" "$DOTNET" build "$proj" --no-restore -c Release
done

# ---------------------------------------------------------------------------
# 5. Repository invariant tests.
#
# These are the only tests that can meaningfully run before the workload ships: they
# check that the migration scaffolding is internally consistent rather than testing
# Tizen behaviour that nobody can execute yet.
# ---------------------------------------------------------------------------
info "Repository invariant tests"
check "unit tests" "$DOTNET" test tests/UnitTests/Maui.Tizen.UnitTests.csproj --no-build -c Release

# ---------------------------------------------------------------------------
# 6. Report the Tizen gate explicitly.
#
# Reported, never silently skipped. If this ever starts saying "available", the Tizen
# lane should be promoted to required.
# ---------------------------------------------------------------------------
info "Tizen workload gate"
if "$DOTNET" workload list 2>/dev/null | grep -qi tizen; then
  pass "Samsung Tizen workload is installed - the Tizen lane can now be made required"
else
  note "Samsung Tizen workload is NOT installed."
  note "  net11.0-tizen11.0 cannot be restored or built until Samsung publishes"
  note "  'samsung.net.sdk.tizen.manifest-11.0.100'. This is expected; see docs/migration.md."
fi

echo
if [[ $FAILURES -gt 0 ]]; then
  fail "$FAILURES check(s) failed"
  exit 1
fi
pass "All workload-free checks passed"
