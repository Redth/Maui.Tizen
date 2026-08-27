#!/usr/bin/env bash
#
# Offline regression tests for the CI transition from "manifest unavailable" to the real
# Tizen workload lane. Network, workload installation, and dotnet are replaced with
# deterministic fakes so the workload-free lane can exercise both sides of the gate.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

FAILURES=0
TEMP_ROOT="$(mktemp -d)"
trap 'rm -rf "$TEMP_ROOT"' EXIT

pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
fail() {
  printf '\033[1;31m  FAIL\033[0m %s\n' "$*"
  FAILURES=$((FAILURES + 1))
}

expect_status() {
  local label="$1" expected="$2" actual="$3"
  if [[ "$actual" == "$expected" ]]; then
    pass "$label"
  else
    fail "$label -- expected exit $expected, got $actual"
  fi
}

expect_failure() {
  local label="$1" actual="$2"
  if [[ "$actual" -ne 0 ]]; then
    pass "$label"
  else
    fail "$label -- expected a non-zero exit"
  fi
}

expect_file() {
  local label="$1" path="$2"
  if [[ -f "$path" ]]; then
    pass "$label"
  else
    fail "$label -- missing $path"
  fi
}

expect_no_file() {
  local label="$1" path="$2"
  if [[ ! -e "$path" ]]; then
    pass "$label"
  else
    fail "$label -- unexpected $path"
  fi
}

expect_contains() {
  local label="$1" path="$2" text="$3"
  if grep -Fq -- "$text" "$path"; then
    pass "$label"
  else
    fail "$label -- '$text' not found in $path"
  fi
}

WORKFLOW=".github/workflows/ci.yml"
GATE_JOB="$TEMP_ROOT/tizen-workload-gate.yml"
GATE_SCRIPT="$REPO_ROOT/eng/ci/tizen-workload-gate.sh"
REAL_LANE="$REPO_ROOT/eng/build-tizen.sh"

awk '
  /^  tizen-workload-gate:/ {
    capture = 1
    print
    next
  }
  capture && /^  [A-Za-z0-9_-]+:/ {
    exit
  }
  capture {
    print
  }
' "$WORKFLOW" > "$GATE_JOB"

expect_contains "CI workflow contains the workload gate job" "$GATE_JOB" "tizen-workload-gate:"

if grep -Eq '^[[:space:]]*continue-on-error:' "$GATE_JOB"; then
  fail "CI workflow has no continue-on-error escape"
else
  pass "CI workflow has no continue-on-error escape"
fi

if grep -Eq '^[[:space:]]*if:' "$GATE_JOB"; then
  fail "CI workload gate has no conditionally skipped step"
else
  pass "CI workload gate has no conditionally skipped step"
fi

expect_contains \
  "CI invokes the transition gate unconditionally" \
  "$GATE_JOB" \
  "run: ./eng/ci/tizen-workload-gate.sh"

if grep -Fq "TIZEN_REAL_WORKLOAD_LANE" "$GATE_JOB"; then
  fail "CI workload gate cannot replace the repository's real lane"
else
  pass "CI workload gate cannot replace the repository's real lane"
fi

if grep -Eq 'raw\.githubusercontent\.com/Samsung/Tizen\.NET/[0-9a-f]{40}/workload/scripts/workload-install\.sh' "$GATE_SCRIPT"; then
  pass "Samsung's supported installer is commit-pinned"
else
  fail "Samsung's supported installer is commit-pinned"
fi

expect_contains \
  "post-install verification uses the exact repository detector" \
  "$GATE_SCRIPT" \
  "-t:ReportTizenWorkload"

if grep -Eq 'workload[[:space:]]+list|maui-tizen' "$GATE_SCRIPT"; then
  fail "transition gate does not use substring workload detection"
else
  pass "transition gate does not use substring workload detection"
fi

BAND="$(python3 -c "import json; print(json.load(open('eng/baselines.json'))['target']['sdkBand'])")"
FEATURE_BAND="${BAND%%-*}"
FULL_ID="samsung.net.sdk.tizen.manifest-${BAND}"
FEATURE_ID="samsung.net.sdk.tizen.manifest-${FEATURE_BAND}"

FAKE_CURL="$TEMP_ROOT/curl"
FAKE_DOTNET="$TEMP_ROOT/dotnet"
FAKE_BUILD="$TEMP_ROOT/build-tizen"
FAKE_DOTNET_ROOT="$TEMP_ROOT/dotnet-root"
INSTALL_MARKER="$TEMP_ROOT/workload-installed"
BUILD_MARKER="$TEMP_ROOT/tizen-built"
INSTALL_ARGS_LOG="$TEMP_ROOT/install-args.log"
CURL_LOG="$TEMP_ROOT/curl.log"
mkdir -p "$FAKE_DOTNET_ROOT"

cat > "$FAKE_CURL" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

output=""
write_out=""
url=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output)
      output="$2"
      shift 2
      ;;
    -w|--write-out)
      write_out="$2"
      shift 2
      ;;
    --fail|--silent|--show-error|--location)
      shift
      ;;
    *)
      url="$1"
      shift
      ;;
  esac
done

printf '%s\n' "$url" >> "$FAKE_CURL_LOG"

case "$url" in
  */workload-install.sh)
    cat > "$output" <<'INSTALLER'
#!/usr/bin/env bash
set -euo pipefail

printf '%s\n' "$*" > "$FAKE_INSTALL_ARGS_LOG"

if [[ "${FAKE_INSTALL_FAIL:-0}" == "1" ]]; then
  exit 91
fi

if [[ "${FAKE_INSTALL_NOOP:-0}" != "1" ]]; then
  : > "$FAKE_WORKLOAD_MARKER"
fi
INSTALLER
    exit 0
    ;;
  *"/${FAKE_FULL_ID}/index.json")
    status="$FAKE_FULL_STATUS"
    ;;
  *"/${FAKE_FEATURE_ID}/index.json")
    status="$FAKE_FEATURE_STATUS"
    ;;
  *)
    echo "unexpected fake curl URL: $url" >&2
    exit 64
    ;;
esac

if [[ "$status" == "200" ]]; then
  printf '{"versions":["11.0.0-transition-test.1"]}\n' > "$output"
else
  : > "$output"
fi

if [[ -n "$write_out" ]]; then
  printf '%s' "$status"
fi
SH

cat > "$FAKE_DOTNET" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" != "msbuild" ]]; then
  echo "unexpected fake dotnet command: $*" >&2
  exit 64
fi

if [[ -f "$FAKE_WORKLOAD_MARKER" ]]; then
  echo "TizenWorkloadAvailable=true"
else
  echo "TizenWorkloadAvailable=false"
fi
SH

cat > "$FAKE_BUILD" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

if [[ "${FAKE_BUILD_FAIL:-0}" == "1" ]]; then
  exit 92
fi

: > "$FAKE_BUILD_MARKER"
SH

chmod +x "$FAKE_CURL" "$FAKE_DOTNET" "$FAKE_BUILD"
cp "$FAKE_DOTNET" "$FAKE_DOTNET_ROOT/dotnet"
chmod +x "$FAKE_DOTNET_ROOT/dotnet"

CASE_FULL_STATUS=404
CASE_FEATURE_STATUS=404
CASE_INSTALL_FAIL=0
CASE_INSTALL_NOOP=0
CASE_BUILD_FAIL=0
LAST_STATUS=0
LAST_SUMMARY=""
LAST_OUTPUT=""

run_gate() {
  local name="$1"

  rm -f "$INSTALL_MARKER" "$BUILD_MARKER" "$INSTALL_ARGS_LOG" "$CURL_LOG"
  LAST_SUMMARY="$TEMP_ROOT/${name}.summary"
  LAST_OUTPUT="$TEMP_ROOT/${name}.out"
  : > "$LAST_SUMMARY"

  set +e
  env \
    CURL="$FAKE_CURL" \
    DOTNET="$FAKE_DOTNET" \
    DOTNET_ROOT="$FAKE_DOTNET_ROOT" \
    GITHUB_STEP_SUMMARY="$LAST_SUMMARY" \
    NUGET_FLAT_CONTAINER_BASE="https://example.test/v3-flatcontainer" \
    TIZEN_WORKLOAD_INSTALLER_URL="https://example.test/workload-install.sh" \
    TIZEN_REAL_WORKLOAD_LANE="$FAKE_BUILD" \
    FAKE_FULL_ID="$FULL_ID" \
    FAKE_FEATURE_ID="$FEATURE_ID" \
    FAKE_FULL_STATUS="$CASE_FULL_STATUS" \
    FAKE_FEATURE_STATUS="$CASE_FEATURE_STATUS" \
    FAKE_CURL_LOG="$CURL_LOG" \
    FAKE_WORKLOAD_MARKER="$INSTALL_MARKER" \
    FAKE_BUILD_MARKER="$BUILD_MARKER" \
    FAKE_INSTALL_ARGS_LOG="$INSTALL_ARGS_LOG" \
    FAKE_INSTALL_FAIL="$CASE_INSTALL_FAIL" \
    FAKE_INSTALL_NOOP="$CASE_INSTALL_NOOP" \
    FAKE_BUILD_FAIL="$CASE_BUILD_FAIL" \
    "$GATE_SCRIPT" > "$LAST_OUTPUT" 2>&1
  LAST_STATUS=$?
  set -e
}

CASE_FULL_STATUS=404
CASE_FEATURE_STATUS=404
CASE_BUILD_FAIL=1
run_gate unavailable
expect_status "unavailable manifests are informational green" 0 "$LAST_STATUS"
expect_no_file "unavailable state does not install a workload" "$INSTALL_MARKER"
expect_no_file "unavailable state does not run a fake Tizen lane" "$BUILD_MARKER"
expect_contains "unavailable summary is explicit" "$LAST_SUMMARY" "Blocked on an external dependency"
expect_contains "preview manifest ID is derived from baselines" "$CURL_LOG" "$FULL_ID/index.json"
expect_contains "feature-band fallback ID is derived from baselines" "$CURL_LOG" "$FEATURE_ID/index.json"

CASE_FULL_STATUS=200
CASE_FEATURE_STATUS=404
CASE_BUILD_FAIL=0
run_gate available-preview
expect_status "available preview manifest completes the real lane" 0 "$LAST_STATUS"
expect_file "available preview manifest invokes Samsung installer" "$INSTALL_MARKER"
expect_file "available preview manifest requires the real lane" "$BUILD_MARKER"
expect_contains "preview install pins the discovered version" "$INSTALL_ARGS_LOG" "--version 11.0.0-transition-test.1"
if grep -Fq -- "--dotnet-target-version-band" "$INSTALL_ARGS_LOG"; then
  fail "preview install lets Samsung derive the preview target band"
else
  pass "preview install lets Samsung derive the preview target band"
fi
expect_contains "available success is reported only after the lane" "$LAST_SUMMARY" "real Tizen restore/build/pack lane passed"

CASE_FULL_STATUS=404
CASE_FEATURE_STATUS=200
run_gate available-feature-band
expect_status "available feature-band fallback completes the real lane" 0 "$LAST_STATUS"
expect_file "feature-band fallback invokes Samsung installer" "$INSTALL_MARKER"
expect_file "feature-band fallback requires the real lane" "$BUILD_MARKER"
expect_contains \
  "feature-band install targets the manifest ID that was probed" \
  "$INSTALL_ARGS_LOG" \
  "--dotnet-target-version-band $FEATURE_BAND"

CASE_FULL_STATUS=200
CASE_FEATURE_STATUS=404
CASE_BUILD_FAIL=1
run_gate available-build-fails
expect_failure "available manifest cannot hide a real-lane failure" "$LAST_STATUS"
expect_file "installer ran before the real-lane failure" "$INSTALL_MARKER"
expect_no_file "failed real lane never reports success" "$BUILD_MARKER"

CASE_INSTALL_FAIL=1
CASE_BUILD_FAIL=0
run_gate available-install-fails
expect_failure "available manifest cannot hide an installer failure" "$LAST_STATUS"
expect_no_file "failed installer does not run the real lane" "$BUILD_MARKER"

CASE_INSTALL_FAIL=0
CASE_INSTALL_NOOP=1
run_gate available-installer-noop
expect_failure "installer success without the exact manifest fails closed" "$LAST_STATUS"
expect_no_file "failed post-install detection does not run the real lane" "$BUILD_MARKER"

CASE_INSTALL_NOOP=0
CASE_FULL_STATUS=500
run_gate probe-error
expect_failure "an indeterminate manifest probe is not reported unavailable" "$LAST_STATUS"
expect_no_file "probe failure does not install a workload" "$INSTALL_MARKER"
expect_no_file "probe failure does not run the real lane" "$BUILD_MARKER"

# The real lane itself must propagate each dotnet phase and cover every current Tizen
# project, so the gate cannot be fail-closed while a nested script masks a package failure.
LANE_DOTNET="$TEMP_ROOT/lane-dotnet"
LANE_LOG="$TEMP_ROOT/lane.log"

cat > "$LANE_DOTNET" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

phase="${1:-}"
project="${2:-}"
printf '%s %s\n' "$phase" "$project" >> "$FAKE_DOTNET_LOG"

if [[ "$phase" == "${FAKE_DOTNET_FAIL_ON:-}" ]]; then
  exit 93
fi
SH
chmod +x "$LANE_DOTNET"

TIZEN_PROJECT_COUNT=0
while IFS= read -r project; do
  if grep -Eq 'TizenPackage\.props|<IsTizenProject>true</IsTizenProject>' "$project"; then
    relative="${project#./}"
    TIZEN_PROJECT_COUNT=$((TIZEN_PROJECT_COUNT + 1))
    if grep -Fq "\"$relative\"" "$REAL_LANE"; then
      pass "real lane includes $relative"
    else
      fail "real lane includes $relative"
    fi
  fi
done < <(find src samples -name '*.csproj' -type f | sort)

if [[ "$TIZEN_PROJECT_COUNT" -gt 0 ]]; then
  pass "real lane has at least one actual Tizen project"
else
  fail "real lane has at least one actual Tizen project"
fi

: > "$LANE_LOG"
set +e
env DOTNET="$LANE_DOTNET" FAKE_DOTNET_LOG="$LANE_LOG" "$REAL_LANE" > "$TEMP_ROOT/lane-success.out" 2>&1
LANE_STATUS=$?
set -e
expect_status "real lane succeeds when all dotnet phases succeed" 0 "$LANE_STATUS"

for phase in restore build pack; do
  count="$(grep -c "^${phase} " "$LANE_LOG" || true)"
  if [[ "$count" -eq "$TIZEN_PROJECT_COUNT" ]]; then
    pass "real lane runs $phase for every Tizen project"
  else
    fail "real lane runs $phase for every Tizen project -- expected $TIZEN_PROJECT_COUNT, got $count"
  fi
done

for phase in restore build pack; do
  : > "$LANE_LOG"
  set +e
  env \
    DOTNET="$LANE_DOTNET" \
    FAKE_DOTNET_LOG="$LANE_LOG" \
    FAKE_DOTNET_FAIL_ON="$phase" \
    "$REAL_LANE" > "$TEMP_ROOT/lane-${phase}-failure.out" 2>&1
  LANE_STATUS=$?
  set -e
  expect_failure "real lane propagates $phase failures" "$LANE_STATUS"
done

exit "$FAILURES"
