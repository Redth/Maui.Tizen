#!/usr/bin/env bash
#
# tizen-device-lane.sh - drive the Samsung-hosted / self-hosted Tizen validation lane.
#
# SCOPE AND BOUNDARIES
#
# This script is repository-owned and contains NO secrets, hostnames, serials, account names or
# any other personal or organisational infrastructure. Everything environment-specific arrives
# through the variables documented below, which a runner supplies. That is deliberate: a device
# lane that hard-codes one person's emulator is a device lane nobody else can run.
#
# It is also expected to be UNAVAILABLE most of the time. Two independent things gate it:
#
#   1. The Samsung workload manifest 'samsung.net.sdk.tizen.manifest-11.0.100' is unpublished
#      (eng/baselines.json > target.workloadManifest). Until it ships, nothing here can build.
#   2. Device/emulator infrastructure is not attached to ordinary pull requests.
#
# `preflight` reports both as structured output and exits 0. Callers decide whether a missing lane
# is tolerable - it is for pull requests, and it is not for a release. See docs/validation/ci.md.
#
# ENVIRONMENT
#
#   TIZEN_PROFILE        mobile | tv                       (default: mobile)
#   TIZEN_DEVICE_SERIAL  sdb serial; empty = sole target    (default: empty)
#   TIZEN_TFM            target framework                   (default: from eng/baselines.json)
#   DEVFLOW_HOST_PORT    host side of the sdb tunnel        (default: 9223)
#   DEVFLOW_DEVICE_PORT  in-app agent port                  (default: 9223)
#   APP_ID               Tizen application id to launch
#
# USAGE
#
#   tizen-device-lane.sh preflight
#   tizen-device-lane.sh build <project>
#   tizen-device-lane.sh install <tpk>
#   tizen-device-lane.sh forward | unforward
#   tizen-device-lane.sh agent-status
#   tizen-device-lane.sh remote-focus
#   tizen-device-lane.sh lifecycle
#   tizen-device-lane.sh pack
#   tizen-device-lane.sh device-assertions
#   tizen-device-lane.sh baselines

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"
TIZEN_PROFILE="${TIZEN_PROFILE:-mobile}"
TIZEN_DEVICE_SERIAL="${TIZEN_DEVICE_SERIAL:-}"
DEVFLOW_HOST_PORT="${DEVFLOW_HOST_PORT:-9223}"
DEVFLOW_DEVICE_PORT="${DEVFLOW_DEVICE_PORT:-9223}"
APP_ID="${APP_ID:-}"

pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m  FAIL\033[0m %s\n' "$*"; }
gate() { printf '\033[1;33m  GATE\033[0m %s\n' "$*"; }
info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

# Reads the pinned target framework from the single source of truth rather than duplicating it.
default_tfm() {
  python3 -c "import json;print(json.load(open('eng/baselines.json'))['target']['targetFramework'])"
}
TIZEN_TFM="${TIZEN_TFM:-$(default_tfm)}"

sdb_cmd() {
  if [[ -n "$TIZEN_DEVICE_SERIAL" ]]; then
    sdb -s "$TIZEN_DEVICE_SERIAL" "$@"
  else
    sdb "$@"
  fi
}

# Emits a key=value line that a workflow can read via $GITHUB_OUTPUT.
emit() {
  printf '%s\n' "$1"
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    printf '%s\n' "$1" >> "$GITHUB_OUTPUT"
  fi
}

cmd_preflight() {
  local workload_ok=false tools_ok=false device_ok=false

  info "Tizen device lane preflight"
  echo "  profile=$TIZEN_PROFILE tfm=$TIZEN_TFM"

  # 1. Samsung workload. The 'maui-tizen' workload is NOT sufficient: in the MAUI manifest it only
  #    declares "extends": ["maui-blazor"] and carries no Tizen platform packs. Checking for it
  #    instead of samsung.net.sdk.tizen is the classic false positive here.
  if "$DOTNET" workload list 2>/dev/null | grep -qiE '^\s*tizen\b'; then
    workload_ok=true
    pass "Samsung Tizen workload is installed"
  else
    gate "Samsung Tizen workload is NOT installed."
    gate "  'samsung.net.sdk.tizen.manifest-11.0.100' is unpublished; see docs/validation/blockers.md."
    gate "  Note: the 'maui-tizen' workload does not provide Tizen platform packs."
  fi

  # 2. Samsung tooling.
  if command -v sdb >/dev/null 2>&1 && command -v tizen >/dev/null 2>&1; then
    tools_ok=true
    pass "Tizen Studio tooling (sdb, tizen) is on PATH"
  else
    gate "Tizen Studio tooling is missing (need both 'sdb' and 'tizen' on PATH)."
  fi

  # 3. Attached target.
  if [[ "$tools_ok" == true ]] && sdb devices 2>/dev/null | grep -qE 'device\s*$|device\b'; then
    device_ok=true
    pass "A Tizen target is attached"
    sdb devices | sed 's/^/        /'
  else
    gate "No Tizen emulator or device is attached."
  fi

  emit "workload_available=$workload_ok"
  emit "tooling_available=$tools_ok"
  emit "device_available=$device_ok"

  if [[ "$workload_ok" == true && "$tools_ok" == true && "$device_ok" == true ]]; then
    emit "lane_available=true"
    pass "Device lane is fully available"
  else
    emit "lane_available=false"
    gate "Device lane is unavailable. This is expected for ordinary pull requests and is a"
    gate "hard failure only for a release; see docs/validation/ci.md."
  fi

  # Always succeeds. The caller decides whether an unavailable lane is acceptable.
  return 0
}

require_lane() {
  if ! "$DOTNET" workload list 2>/dev/null | grep -qiE '^\s*tizen\b'; then
    fail "Samsung Tizen workload is required for '$1'. Run preflight for details."
    exit 1
  fi
}

cmd_build() {
  local project="${1:?usage: build <project>}"
  require_lane build

  info "Building $project for $TIZEN_TFM"
  "$DOTNET" build "$project" -c Release -f "$TIZEN_TFM" --nologo

  info "Packaging TPK"
  "$DOTNET" build "$project" -c Release -f "$TIZEN_TFM" -t:Package --nologo
}

cmd_install() {
  local tpk="${1:?usage: install <tpk>}"
  info "Installing $tpk"
  sdb_cmd install "$tpk"
}

cmd_forward() {
  # A fixed device port plus an sdb tunnel makes emulator and physical device identical from the
  # driver's point of view. See TizenAgentConnection for the same contract in code.
  info "Forwarding tcp:$DEVFLOW_HOST_PORT -> tcp:$DEVFLOW_DEVICE_PORT"
  sdb_cmd forward "tcp:$DEVFLOW_HOST_PORT" "tcp:$DEVFLOW_DEVICE_PORT"
}

cmd_unforward() {
  # Teardown must be unconditional: a leaked forward silently captures the next job's traffic.
  info "Removing forward tcp:$DEVFLOW_HOST_PORT"
  sdb_cmd forward --remove "tcp:$DEVFLOW_HOST_PORT" || true
}

devflow() {
  curl -sS --max-time 20 "http://127.0.0.1:$DEVFLOW_HOST_PORT/api/v1/$1" "${@:2}"
}

cmd_agent_status() {
  info "Querying the DevFlow agent"
  if ! devflow "agent/status"; then
    fail "The DevFlow agent did not respond on port $DEVFLOW_HOST_PORT."
    fail "  Confirm the app was built with AddMauiDevFlowAgent() and that 'forward' ran."
    exit 1
  fi
  echo
  info "Capabilities"
  devflow "agent/capabilities"
  echo
}

# ---------------------------------------------------------------------------
# TV remote focus traversal.
#
# Focus order is the single most common TV-specific defect and it is invisible to every
# host-side check: the app builds, renders and passes unit tests while being unusable with a
# remote. Driving real key events through DevFlow is the only way to observe it.
# ---------------------------------------------------------------------------
cmd_remote_focus() {
  if [[ "$TIZEN_PROFILE" != "tv" ]]; then
    gate "remote-focus applies to the tv profile only (TIZEN_PROFILE=$TIZEN_PROFILE)."
    return 0
  fi

  info "Remote focus traversal"

  local visited=() key
  for key in Down Down Down Right Up Left; do
    devflow "ui/actions/key" \
      -H 'Content-Type: application/json' \
      -d "{\"key\":\"$key\"}" >/dev/null

    local focused
    focused="$(devflow "ui/elements?strategy=type&value=*&limit=200" \
      | python3 -c "
import json,sys
try:
    data = json.load(sys.stdin)
except Exception:
    print(''); raise SystemExit
elements = data if isinstance(data, list) else data.get('elements', [])
focused = [e for e in elements if e.get('isFocused')]
print(focused[0].get('automationId') or focused[0].get('id') if focused else '')
")"

    if [[ -z "$focused" ]]; then
      fail "After '$key' no element reports focus. A TV screen with nothing focused is"
      fail "  unreachable by remote."
      exit 1
    fi

    echo "        $key -> $focused"
    visited+=("$focused")
  done

  # Focus must actually move. A screen where every press lands on the same element is a focus trap.
  local unique
  unique="$(printf '%s\n' "${visited[@]}" | sort -u | wc -l | tr -d ' ')"
  if [[ "$unique" -lt 2 ]]; then
    fail "Focus never moved across ${#visited[@]} key presses - this is a focus trap."
    exit 1
  fi

  pass "Focus traversal moved across $unique distinct elements"
}

# ---------------------------------------------------------------------------
# Lifecycle: a real background/foreground cycle.
#
# The previous version used `app_launcher -k`, which TERMINATES the application; relaunching it
# afterwards is a cold start. That tests process startup, not suspend/resume - and suspend/resume
# is where Tizen apps actually lose state or fail to re-attach their renderer, because the process
# survives and the surface does not.
#
# Backgrounding is done by bringing the home application to the foreground, which is what pressing
# Home does. The home application id is profile-specific, so it is supplied rather than assumed.
# ---------------------------------------------------------------------------
cmd_lifecycle() {
  local app="${APP_ID:?APP_ID must be set for the lifecycle harness}"
  local home="${HOME_APP_ID:-}"

  if [[ -z "$home" ]]; then
    fail "HOME_APP_ID must be set so the app can be backgrounded rather than killed."
    fail "  It is profile-specific (mobile and TV ship different home applications), so there"
    fail "  is no safe default. Terminating the app instead would make this a cold-start test."
    exit 1
  fi

  info "Lifecycle: launch, background via Home, foreground"

  sdb_cmd shell app_launcher -s "$app"
  sleep 3
  cmd_agent_status >/dev/null

  # Write a marker into the running app so resume can be distinguished from a cold start:
  # a restarted process would have lost it.
  local marker="lifecycle-$(date +%s)"
  devflow "storage/preferences" \
    -H 'Content-Type: application/json' \
    -d "{\"key\":\"devflow.lifecycle.marker\",\"value\":\"$marker\"}" >/dev/null || true

  info "Backgrounding via $home"
  sdb_cmd shell app_launcher -s "$home"
  sleep 3

  # The app must still be running, just not foreground. A terminated app is a lifecycle failure,
  # not a successful background.
  if ! sdb_cmd shell app_launcher -r | grep -q "$app"; then
    fail "'$app' is no longer running after backgrounding. It was terminated rather than suspended."
    exit 1
  fi
  pass "App is still running in the background"

  info "Returning to the foreground"
  sdb_cmd shell app_launcher -s "$app"
  sleep 3

  if ! devflow "agent/status" >/dev/null; then
    fail "The agent did not respond after resume. The app failed to re-attach."
    exit 1
  fi

  # Handler re-attachment: the visual tree must be walkable again. An app that resumes with a
  # detached renderer answers /agent/status but returns an empty tree.
  local elements
  elements="$(devflow "ui/tree?depth=3" | python3 -c "
import json,sys
try:
    d = json.load(sys.stdin)
except Exception:
    print(0); raise SystemExit
roots = d if isinstance(d, list) else d.get('elements', d.get('roots', []))
print(len(roots))
")"

  if [[ "${elements:-0}" -lt 1 ]]; then
    fail "The visual tree is empty after resume; handlers did not re-attach."
    exit 1
  fi
  pass "Visual tree re-attached after resume ($elements root element(s))"

  local restored
  restored="$(devflow "storage/preferences?key=devflow.lifecycle.marker" | python3 -c "
import json,sys
try:
    print(json.load(sys.stdin).get('value',''))
except Exception:
    print('')
" || true)"

  if [[ "$restored" == "$marker" ]]; then
    pass "In-process state survived the cycle (this was a resume, not a cold start)"
  else
    fail "State did not survive: expected '$marker', got '${restored:-<empty>}'."
    fail "  The app was restarted rather than resumed, so suspend/resume was not exercised."
    exit 1
  fi
}

# ---------------------------------------------------------------------------
# Shipping packages.
#
# Only produced where the workload exists. The release gates in ReleaseReadinessTests require
# every declared package-content contract to have a matching .nupkg.
# ---------------------------------------------------------------------------
cmd_pack() {
  require_lane pack

  info "Packing shipping packages"
  "$DOTNET" pack "$REPO_ROOT/Maui.Tizen.slnx" -c Release --nologo
}

# ---------------------------------------------------------------------------
# On-device assertions.
#
# These MUST execute inside the deployed application. Running the hosted validation script on the
# controller would load no Tizen backend, so the mapper-parity and Essentials suites would skip
# exactly as they do on any hosted runner - a device lane that validated nothing.
#
# The in-app agent exposes them through a DevFlow extension namespace; results come back as JSON.
# ---------------------------------------------------------------------------
cmd_device_assertions() {
  info "Running on-device conventions inside the deployed app"

  local response
  if ! response="$(devflow "extensions/maui-tizen/conventions/run" -X POST)"; then
    fail "The on-device conventions endpoint did not respond."
    fail "  The app must register the Maui.Tizen DevFlow conventions extension; without it the"
    fail "  mapper-parity and Essentials suites cannot run anywhere, because they need the Tizen"
    fail "  backend executing in-process. See docs/validation/device-lane.md."
    exit 1
  fi

  echo "$response" | python3 -c "
import json,sys
d = json.load(sys.stdin)
failed = d.get('failed', [])
total = d.get('total', 0)
skipped = d.get('skipped', [])
print(f'        total={total} failed={len(failed)} skipped={len(skipped)}')
for f in failed:
    print(f'        FAIL {f}')
for s in skipped:
    print(f'        SKIP {s}')
# A run that asserted nothing is a failure: it is indistinguishable from a passing run.
raise SystemExit(1 if failed or total == 0 else 0)
"

  pass "On-device conventions passed"
}

# ---------------------------------------------------------------------------
# Visual baselines.
#
# Screenshots are captured on the device and pulled back to the controller; the comparison itself
# runs host-side with the deterministic comparer in Maui.Tizen.TestUtils.
# ---------------------------------------------------------------------------
cmd_baselines() {
  local out="$REPO_ROOT/artifacts/screenshots/$TIZEN_PROFILE"
  mkdir -p "$out"

  info "Capturing visual baselines for $TIZEN_PROFILE"

  local cases
  cases="$(python3 -c "
import json
m = json.load(open('$REPO_ROOT/samples/Maui.Tizen.Catalog/catalog-manifest.json'))
print(' '.join(c['id'] for c in m['cases'] if c.get('capturesBaseline') and '$TIZEN_PROFILE' in c['profiles']))
")"

  if [[ -z "$cases" ]]; then
    gate "No baseline cases declared for profile '$TIZEN_PROFILE'."
    return 0
  fi

  local id
  for id in $cases; do
    devflow "ui/actions/navigate" \
      -H 'Content-Type: application/json' \
      -d "{\"route\":\"$id\"}" >/dev/null

    # Settle before capturing; an in-flight animation is the classic source of a flaky baseline.
    sleep 1

    if devflow "ui/screenshot?format=png" --output "$out/$id.png"; then
      echo "        captured $id"
    else
      fail "Could not capture '$id'."
      exit 1
    fi
  done

  pass "Captured $(echo "$cases" | wc -w | tr -d ' ') screenshot(s) into artifacts/screenshots/$TIZEN_PROFILE"
}

case "${1:-}" in
  preflight)    shift; cmd_preflight "$@" ;;
  build)        shift; cmd_build "$@" ;;
  install)      shift; cmd_install "$@" ;;
  forward)      shift; cmd_forward "$@" ;;
  unforward)    shift; cmd_unforward "$@" ;;
  agent-status) shift; cmd_agent_status "$@" ;;
  remote-focus) shift; cmd_remote_focus "$@" ;;
  lifecycle)    shift; cmd_lifecycle "$@" ;;
  pack)              shift; cmd_pack "$@" ;;
  device-assertions) shift; cmd_device_assertions "$@" ;;
  baselines)         shift; cmd_baselines "$@" ;;
  *)
    sed -n '2,40p' "$0"
    exit 2
    ;;
esac
