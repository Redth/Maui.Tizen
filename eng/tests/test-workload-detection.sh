#!/usr/bin/env bash
#
# test-workload-detection.sh — regression tests for Samsung Tizen workload detection.
#
# Detection decides whether the entire repository can build, so getting it wrong is
# expensive in both directions:
#
#   false negative — reports "missing" on a machine where the workload IS installed, so
#                    the Tizen build silently never runs and nobody notices
#   false positive — reports "installed" when it is not, so the explanatory
#                    MAUITIZEN0001 gate is skipped and users get a raw NETSDK1013
#
# Both have already happened during development, which is why these are pinned:
#
#   * The original probe constructed sdk-manifests/$(NETCoreSdkVersion)/samsung.net.sdk.tizen/.
#     The real layout is sdk-manifests/<feature-band>/<id>/<version>/, and the band is not
#     the SDK version — an 11.0.100-preview.7.26381.103 SDK uses band 11.0.100-preview.6.
#     It could never have matched.
#   * A later attempt used an item glob in a property value. During evaluation `@(x)` is
#     not expanded, so the property held the literal string "@(x)" — always truthy, so
#     every machine reported "installed".
#   * The shell probe used `dotnet workload list | grep -i tizen`, which matches an
#     unrelated `maui-tizen` workload by substring.
#
# Fixtures live in eng/tests/fixtures/workloads/ and are driven through the real MSBuild
# detection target, so these test the shipping logic rather than a copy of it.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"
PROJECT="src/Maui.Tizen.Core/Maui.Tizen.Core.csproj"
FIXTURES="$SCRIPT_DIR/fixtures/workloads"

pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m  FAIL\033[0m %s\n' "$*"; }

FAILURES=0

# detect <dotnet-root> -> prints true|false
detect() {
  local root="$1"
  "$DOTNET" msbuild "$PROJECT" -t:ReportTizenWorkload -nologo -v:m -p:NetCoreRoot="$root" 2>/dev/null \
    | grep -oE 'TizenWorkloadAvailable=[a-z]+' | head -1 | cut -d= -f2
}

expect() {
  local label="$1" fixture="$2" want="$3" got
  got="$(detect "$FIXTURES/$fixture/")"
  if [[ "$got" == "$want" ]]; then
    pass "$label (got $got)"
  else
    fail "$label — expected $want, got '${got:-<empty>}'"
    FAILURES=$((FAILURES + 1))
  fi
}

# Current layout: sdk-manifests/<band>/samsung.net.sdk.tizen/<version>/WorkloadManifest.json
expect "current manifest layout is detected"        present-versioned      true

# Legacy layout, no version directory. Still supported so an older SDK does not
# silently regress to "missing". Also a preview.6 band, proving detection tolerates
# drift within the .NET 11 line - this SDK ships manifests under both preview.6 and
# preview.7, so pinning an exact preview segment would be a false negative.
expect "legacy manifest layout is detected"         present-legacy         true

# THE SUBSTRING TRAP. This fixture contains a `maui-tizen` manifest and a
# `microsoft.net.sdk.maui` manifest, but no `samsung.net.sdk.tizen`. Anything matching
# "tizen" loosely reports true here and skips the gate.
expect "maui-tizen alone is NOT Samsung's workload"  absent-maui-tizen-only false

# THE WRONG-BAND TRAP. Samsung workloads ARE installed here - but for .NET 9 and .NET 10.
# They cannot satisfy net11.0-tizen11.0. An unrestricted `sdk-manifests/*` glob reports
# true, lifts the gate, and the build then fails much later with an unrelated-looking
# error about missing Tizen reference packs.
expect "old-band workloads do NOT satisfy net11"     absent-old-band-only   false

# Nothing installed at all.
expect "empty manifest root reports missing"        absent-empty           false

# An explicit override must win over detection, which is how CI pins behaviour.
GOT="$("$DOTNET" msbuild "$PROJECT" -t:ReportTizenWorkload -nologo -v:m \
        -p:NetCoreRoot="$FIXTURES/absent-empty/" -p:TizenWorkloadAvailable=true 2>/dev/null \
        | grep -oE 'TizenWorkloadAvailable=[a-z]+' | head -1 | cut -d= -f2)"
if [[ "$GOT" == "true" ]]; then
  pass "explicit -p:TizenWorkloadAvailable=true overrides detection"
else
  fail "explicit override ignored — got '${GOT:-<empty>}'"
  FAILURES=$((FAILURES + 1))
fi

# ---------------------------------------------------------------------------
# End-to-end: the gate must actually be what the user sees.
#
# Static checks that the gate is hooked to the right targets are necessary but not
# sufficient - it has silently stopped firing twice while still looking correctly wired.
# These run the real commands and assert on the real output.
# ---------------------------------------------------------------------------
PROJ_OUTPUT_CHECK() {
  local cmd="$1" out
  out="$("$DOTNET" "$cmd" "$PROJECT" 2>&1 || true)"

  if ! grep -q "MAUITIZEN0001" <<<"$out"; then
    fail "dotnet $cmd did not surface the MAUITIZEN0001 gate"
    FAILURES=$((FAILURES + 1))
    return
  fi

  # The gate exists precisely so these never reach the user.
  if grep -qE "NETSDK1013|NETSDK1139" <<<"$out"; then
    fail "dotnet $cmd leaked a raw SDK error (NETSDK1013/NETSDK1139) past the gate"
    FAILURES=$((FAILURES + 1))
    return
  fi

  pass "dotnet $cmd reports MAUITIZEN0001 and no raw SDK error"
}

PROJ_OUTPUT_CHECK build
PROJ_OUTPUT_CHECK restore

# Target framework inference must be real, not the SDK's "_" / "v0.0" fallback.
#
# That fallback is what you get when TargetFramework is assigned in Directory.Build.targets
# instead of props: the value looks right, but everything keyed off the identifier
# evaluates against a framework that does not exist.
TFI="$("$DOTNET" msbuild "$PROJECT" -p:TizenWorkloadAvailable=true -getProperty:TargetFrameworkIdentifier 2>/dev/null | tr -d '[:space:]')"
TPI="$("$DOTNET" msbuild "$PROJECT" -p:TizenWorkloadAvailable=true -getProperty:TargetPlatformIdentifier 2>/dev/null | tr -d '[:space:]')"

if [[ "$TFI" == ".NETCoreApp" && "$TPI" == "tizen" ]]; then
  pass "target framework inference resolves (identifier=$TFI platform=$TPI)"
else
  fail "target framework inference is broken — identifier='$TFI' platform='$TPI' (expected .NETCoreApp / tizen)"
  FAILURES=$((FAILURES + 1))
fi

exit $FAILURES
