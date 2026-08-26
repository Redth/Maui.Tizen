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
# Lifecycle.
#
# Suspend/resume is where Tizen apps most often lose state or fail to re-attach their renderer.
# ---------------------------------------------------------------------------
cmd_lifecycle() {
  local app="${APP_ID:?APP_ID must be set for the lifecycle harness}"

  info "Lifecycle: launch, background, foreground"

  sdb_cmd shell app_launcher -s "$app"
  sleep 3

  cmd_agent_status >/dev/null

  # Home moves the app to the background without terminating it.
  sdb_cmd shell app_launcher -k "$app" || true
  sleep 2

  sdb_cmd shell app_launcher -s "$app"
  sleep 3

  if ! devflow "agent/status" >/dev/null; then
    fail "The agent did not respond after resume. The app failed to re-attach."
    exit 1
  fi

  pass "App survived a background/foreground cycle"
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
  *)
    sed -n '2,40p' "$0"
    exit 2
    ;;
esac
