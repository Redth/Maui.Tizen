#!/usr/bin/env bash
#
# evaluate-release-gate.sh - decide whether the Tizen device lane satisfies a release.
#
# The decision lives here, not inline in YAML, so it can be tested exhaustively. The previous
# version was a block of shell inside the workflow and contained a hole that no test could have
# caught: it only inspected the matrix job's *result*, so a matrix that ran with every device step
# skipped (lane_available=false) reported success and the release gate passed. A device lane that
# did nothing was indistinguishable from one that passed.
#
# Usage:
#   evaluate-release-gate.sh --required <true|false> \
#                            --release-validation <true|false|empty> \
#                            --lab-enabled <true|false> \
#                            --matrix-result <success|failure|cancelled|skipped> \
#                            --consumer-result <success|failure|cancelled|skipped> \
#                            --required-profiles "mobile-mdpi mobile-hdpi mobile-xhdpi tv-fhd tv-uhd" \
#                            --results-dir <dir>
#
# <results-dir> holds one file per required visual target, named
# 'device-result-<profile>-<density>.txt', written by the device job. Each must contain
# 'lane_available=true' and 'status=pass'. Reporting through
# artifacts rather than job outputs is deliberate: matrix job outputs collapse to a single
# last-writer-wins value, so a passing profile could mask a failing one.
#
# Exit codes: 0 = release may proceed, 1 = blocked.

set -euo pipefail

REQUIRED=false
RELEASE_VALIDATION=false
LAB_ENABLED=false
MATRIX_RESULT=skipped
CONSUMER_RESULT=skipped
REQUIRED_PROFILES=""
RESULTS_DIR=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --required)          REQUIRED="$2"; shift 2 ;;
    --release-validation) RELEASE_VALIDATION="${2:-}"; shift 2 ;;
    --lab-enabled)       LAB_ENABLED="$2"; shift 2 ;;
    --matrix-result)     MATRIX_RESULT="$2"; shift 2 ;;
    --consumer-result)   CONSUMER_RESULT="$2"; shift 2 ;;
    --required-profiles) REQUIRED_PROFILES="$2"; shift 2 ;;
    --results-dir)       RESULTS_DIR="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

fail() { echo "BLOCKED: $*"; }
ok()   { echo "OK: $*"; }
gate_output() {
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    echo "gate_passed=$1" >> "$GITHUB_OUTPUT"
  fi
}

# Fail closed for callers. Informational runs and failures must not produce a success-shaped output.
gate_output false

# Not a release: the lane is informational and must never block an ordinary pull request.
if [[ "$REQUIRED" != "true" ]]; then
  ok "not a release run; device lane result '$MATRIX_RESULT' is informational"
  exit 0
fi

if [[ "$LAB_ENABLED" != "true" ]]; then
  fail "a release requires the Tizen device lane, but no device lab is attached."
  fail "  Set the TIZEN_DEVICE_LAB_ENABLED repository variable and register a self-hosted"
  fail "  runner labelled 'tizen'. See docs/validation/device-lane.md."
  exit 1
fi

# A skipped or cancelled matrix is a hard block. Treating it as anything else is how a release
# ships with no device validation at all.
if [[ "$MATRIX_RESULT" != "success" ]]; then
  fail "the device matrix did not succeed (result: $MATRIX_RESULT)."
  exit 1
fi

if [[ "$CONSUMER_RESULT" != "success" ]]; then
  fail "the real-package consumer restore did not succeed on a workload-equipped runner (result: $CONSUMER_RESULT)."
  exit 1
fi

if [[ -z "$REQUIRED_PROFILES" ]]; then
  fail "no required profiles were supplied, so nothing would be checked."
  exit 1
fi

if [[ -z "$RESULTS_DIR" || ! -d "$RESULTS_DIR" ]]; then
  fail "results directory '$RESULTS_DIR' does not exist, so no profile can be verified."
  exit 1
fi

BLOCKED=0

for profile in $REQUIRED_PROFILES; do
  result_file="$RESULTS_DIR/device-result-$profile.txt"

  if [[ ! -f "$result_file" ]]; then
    fail "profile '$profile' produced no result file. The job did not run to completion."
    BLOCKED=1
    continue
  fi

  lane_available="$(grep -E '^lane_available=' "$result_file" | tail -1 | cut -d= -f2 || true)"
  status="$(grep -E '^status=' "$result_file" | tail -1 | cut -d= -f2 || true)"

  # The hole the review found: this is the check that makes a fully-skipped device job fail.
  if [[ "$lane_available" != "true" ]]; then
    fail "profile '$profile' reported lane_available='${lane_available:-<missing>}'."
    fail "  Its device steps were skipped, so nothing was validated on hardware."
    BLOCKED=1
    continue
  fi

  if [[ "$status" != "pass" ]]; then
    fail "profile '$profile' reported status='${status:-<missing>}'."
    BLOCKED=1
    continue
  fi

  ok "profile '$profile' validated on hardware"
done

if [[ $BLOCKED -ne 0 ]]; then
  fail "release blocked."
  exit 1
fi

ok "every required profile was validated on hardware; release may proceed"
if [[ "$RELEASE_VALIDATION" == "true" ]]; then
  gate_output true
fi
exit 0
