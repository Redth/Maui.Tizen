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

# ---------------------------------------------------------------------------
# Package output for this lane.
#
# ISOLATED, and emptied on every run.
#
# Packing used to land in the shared $(PackageOutputPath) (artifacts/packages/), which every
# other pack in the repository also writes to, and the README probe then picked its package
# with `ls -t | head -1`. That reads whatever happened to be newest: a package from a
# different branch, from before a fix, or from a partially completed run. The check would
# then pass or fail on the strength of an artifact this run did not produce - and because
# the version string never changes, the file name gives nothing away.
#
# A private directory that starts empty means every assertion below is about THIS run's
# output, and the provenance check that follows can hold the packages to the current commit.
# ---------------------------------------------------------------------------
PACK_OUTPUT="$REPO_ROOT/artifacts/packages/workload-free"
rm -rf "$PACK_OUTPUT"
mkdir -p "$PACK_OUTPUT"

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
  "src/Maui.Tizen.Templates/Maui.Tizen.Templates.csproj"
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

  # Foundation-owned probes.
  "eng/tests/PublicApiOptIn/PublicApiOptIn.csproj"
  "eng/tests/PackReadmeProbe/PackReadmeProbe.csproj"
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
if check "pack README probe" "$DOTNET" pack eng/tests/PackReadmeProbe/PackReadmeProbe.csproj --no-restore -c Release "-p:PackageOutputPath=$PACK_OUTPUT"; then
  README_NUPKG="$(ls -t "$PACK_OUTPUT"/Maui.Tizen.Internal.PackReadmeProbe.*.nupkg 2>/dev/null | head -1 || true)"
  # Read the archive with python3 rather than unzip. python3 is already a hard dependency
  # of this script, whereas unzip is not present in the dotnet/sdk container images - and a
  # missing unzip is indistinguishable from a missing README, so this check reported a
  # present README as absent. That is precisely the failure mode the original note here
  # warned about (a present thing reported missing), reintroduced through the tool rather
  # than the pipeline. A read error now fails loudly and separately instead.
  README_COUNT=0
  README_PROBE_STATUS=0
  if [[ -n "$README_NUPKG" ]]; then
    README_COUNT="$(python3 -c "
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    print(sum(1 for n in archive.namelist() if n.rsplit('/', 1)[-1] == 'README.md'))
" "$README_NUPKG" 2>/dev/null)" || README_PROBE_STATUS=$?
  fi
  if [[ "$README_PROBE_STATUS" -ne 0 || -z "$README_COUNT" ]]; then
    # Counted as a failure. It previously was not: this branch reported FAIL and left
    # $FAILURES untouched, so a probe that could not read the package printed a red line
    # and the script still exited 0 - the exact "reported but not enforced" shape this
    # lane exists to avoid.
    fail "could not read '$README_NUPKG' to verify its README (the package may be fine; the probe failed)"
    FAILURES=$((FAILURES + 1))
  elif [[ "$README_COUNT" -gt 0 ]]; then
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
# 5b. Packable shape.
#
# Maui.Tizen.Build.Tasks and Maui.Tizen.Templates both ship content in unusual places
# (buildTransitive/ and content/ rather than lib/), which NuGet will happily pack wrong
# without complaining. Packing here means the layout is validated on every run rather
# than at publish time; the package-content tests above then assert the entries.
# ---------------------------------------------------------------------------
info "Package shape"
for proj in "src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj" "src/Maui.Tizen.Templates/Maui.Tizen.Templates.csproj"; do
  check "pack    $(basename "$proj")" "$DOTNET" pack "$proj" --no-build -c Release "-p:PackageOutputPath=$PACK_OUTPUT"
done

# ---------------------------------------------------------------------------
# 5c. Provenance of the packages just produced.
#
# Every package carries a <repository commit="..."> stamp, and consumers use it to find the
# sources a binary came from. A stamp that does not match the commit being built is worse
# than none: it points confidently at the wrong tree.
#
# The isolated output directory above is what makes this checkable - in a shared directory
# the newest matching file may be from another branch entirely, and the assertion would be
# about that package instead. Here every .nupkg present was produced by this run, so all of
# them must name this HEAD.
#
# It also catches the case where the stamp goes missing altogether (SourceLink failing to
# resolve the git directory in a worktree or a shallow CI clone, say), because a package
# with no repository element fails the comparison rather than skipping it.
# ---------------------------------------------------------------------------
info "Package provenance"
if HEAD_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null)" && [[ -n "$HEAD_COMMIT" ]]; then
  check "packed nuspec repository commit is $HEAD_COMMIT" env "MAUI_TIZEN_HEAD=$HEAD_COMMIT" "MAUI_TIZEN_PACK_OUTPUT=$PACK_OUTPUT" python3 - <<'PY'
import glob, os, re, sys, zipfile

expected = os.environ["MAUI_TIZEN_HEAD"]
output = os.environ["MAUI_TIZEN_PACK_OUTPUT"]

packages = sorted(
    p for p in glob.glob(os.path.join(output, "*.nupkg"))
    if not p.endswith(".symbols.nupkg")
)

if not packages:
    sys.exit(f"no packages were produced in '{output}'")

problems = []
for package in packages:
    with zipfile.ZipFile(package) as archive:
        names = [n for n in archive.namelist() if n.endswith(".nuspec")]
        if not names:
            problems.append(f"{os.path.basename(package)}: no .nuspec")
            continue
        nuspec = archive.read(names[0]).decode("utf-8-sig")

    match = re.search(r'<repository\b[^>]*\bcommit="([0-9a-f]+)"', nuspec)
    if not match:
        problems.append(f"{os.path.basename(package)}: no repository commit stamp")
    elif match.group(1) != expected:
        problems.append(f"{os.path.basename(package)}: stamped {match.group(1)}, expected {expected}")

if problems:
    sys.exit("stale or missing provenance:\n  " + "\n  ".join(problems))

print(f"{len(packages)} package(s) stamped with {expected}")
PY
else
  fail "could not resolve HEAD, so package provenance could not be checked"
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
