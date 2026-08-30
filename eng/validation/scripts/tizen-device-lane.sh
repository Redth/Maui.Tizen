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
#   TIZEN_DEVICE_SERIAL  sdb serial for this visual target   (default: empty)
#   TIZEN_REQUIRED_SERIALS comma-separated serials for every required visual target
#   TIZEN_DENSITY        declared visual target id           (for example: xhdpi)
#   TIZEN_DEVICE_IMAGE   device/emulator image identifier   (default: unspecified)
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
#   tizen-device-lane.sh launch
#   tizen-device-lane.sh wait-for-agent
#   tizen-device-lane.sh agent-status
#   tizen-device-lane.sh verify-device-profile
#   tizen-device-lane.sh list-baseline-variants
#   tizen-device-lane.sh remote-focus
#   tizen-device-lane.sh lifecycle
#   tizen-device-lane.sh device-assertions
#   tizen-device-lane.sh baselines
#   tizen-device-lane.sh baseline-sidecar <png> <case> <profile> <api> <theme> <density>

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"
TIZEN_PROFILE="${TIZEN_PROFILE:-mobile}"
TIZEN_DEVICE_SERIAL="${TIZEN_DEVICE_SERIAL:-}"
DEVFLOW_HOST_PORT="${DEVFLOW_HOST_PORT:-9223}"
DEVFLOW_DEVICE_PORT="${DEVFLOW_DEVICE_PORT:-9223}"
APP_ID="${APP_ID:-}"
MAUI_TIZEN_RELEASE_NUGET_CONFIG="${MAUI_TIZEN_RELEASE_NUGET_CONFIG:-}"
DEVFLOW_CONVENTIONS_NAMESPACE="org.dotnet.maui.tizen"
DEVFLOW_LEASE_ID=""

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

  # 3. Every declared density/resolution is bound to a distinct target. Reusing one serial while
  # only relabelling output would manufacture several baseline variants from one unchanged device.
  local required_serials="${TIZEN_REQUIRED_SERIALS:-}"
  local serial_count unique_serial_count
  serial_count="$(printf '%s\n' "$required_serials" | tr ',' '\n' | sed '/^$/d' | wc -l | tr -d ' ')"
  unique_serial_count="$(printf '%s\n' "$required_serials" | tr ',' '\n' | sed '/^$/d' | sort -u | wc -l | tr -d ' ')"

  if [[ -z "$TIZEN_DEVICE_SERIAL" || -z "$required_serials" ]]; then
    gate "TIZEN_DEVICE_SERIAL and TIZEN_REQUIRED_SERIALS must be configured."
  elif [[ "$serial_count" -eq 0 || "$serial_count" -ne "$unique_serial_count" ]]; then
    gate "Every required density/resolution must identify a distinct target."
  elif ! printf '%s\n' "$required_serials" | tr ',' '\n' | grep -Fxq "$TIZEN_DEVICE_SERIAL"; then
    gate "TIZEN_DEVICE_SERIAL is not one of the configured required visual targets."
  elif [[ "$tools_ok" == true ]]; then
    local devices
    devices="$(sdb devices 2>/dev/null || true)"
    local missing_serial=false serial
    while IFS= read -r serial; do
      [[ -z "$serial" ]] && continue
      if ! printf '%s\n' "$devices" | awk -v serial="$serial" '$1 == serial && $2 == "device" { found=1 } END { exit !found }'; then
        gate "Configured target '$serial' is not connected with state 'device'."
        missing_serial=true
      fi
    done < <(printf '%s\n' "$required_serials" | tr ',' '\n')

    if [[ "$missing_serial" == false ]]; then
      device_ok=true
      pass "All distinct density/resolution targets are connected"
      printf '%s\n' "$devices" | sed 's/^/        /'
    fi
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

  # MauiTizenValidation defines MAUITIZEN_DEVFLOW so the app compiles the DevFlow agent in. A
  # plain Release build excludes it - AddMauiDevFlowAgent() is conventionally '#if DEBUG' - and the
  # whole lane would then install an app the driver can never talk to.
  info "Building $project for $TIZEN_TFM (validation configuration)"
  if [[ -n "$MAUI_TIZEN_RELEASE_NUGET_CONFIG" ]]; then
    [[ -f "$MAUI_TIZEN_RELEASE_NUGET_CONFIG" ]] \
      || { fail "Release NuGet configuration is missing: $MAUI_TIZEN_RELEASE_NUGET_CONFIG"; exit 1; }
    "$DOTNET" restore "$project" -p:MauiTizenValidation=true \
      --configfile "$MAUI_TIZEN_RELEASE_NUGET_CONFIG" --nologo
    "$DOTNET" build "$project" --no-restore -c Release -f "$TIZEN_TFM" \
      -p:MauiTizenValidation=true --nologo
  else
    "$DOTNET" build "$project" -c Release -f "$TIZEN_TFM" \
      -p:MauiTizenValidation=true --nologo
  fi

  info "Packaging TPK"
  "$DOTNET" build "$project" --no-restore -c Release -f "$TIZEN_TFM" \
    -p:MauiTizenValidation=true -t:Package --nologo
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
  # --fail is essential: without it curl exits 0 on a 4xx/5xx and happily writes the error body to
  # the output file, so a 501 gets saved as a .png and the capture step reports success.
  local path="$1"
  shift
  [[ "$path" == /* ]] || path="/api/v1/$path"

  if [[ -n "$DEVFLOW_LEASE_ID" ]]; then
    curl -sS --fail --max-time 20 "http://127.0.0.1:$DEVFLOW_HOST_PORT$path" \
      -H "X-DevFlow-Lease: $DEVFLOW_LEASE_ID" "$@"
  else
    curl -sS --fail --max-time 20 "http://127.0.0.1:$DEVFLOW_HOST_PORT$path" "$@"
  fi
}

claim_mutation_lease() {
  [[ -n "$DEVFLOW_LEASE_ID" ]] && return 0

  DEVFLOW_LEASE_ID="maui-tizen-$TIZEN_PROFILE-$$"
  local response
  response="$(devflow "/api/v1/agent/lease" \
    -H 'Content-Type: application/json' \
    -d "{\"action\":\"claim\",\"leaseId\":\"$DEVFLOW_LEASE_ID\",\"holderKind\":\"validation\",\"label\":\"Maui.Tizen $TIZEN_PROFILE lane\"}")"

  if ! printf '%s' "$response" | python3 -c "
import json,sys
d=json.load(sys.stdin)
raise SystemExit(0 if d.get('ok') and d.get('allowed') and d.get('youHold') else 1)
"; then
    DEVFLOW_LEASE_ID=""
    fail "Could not claim the DevFlow mutation lease."
    exit 1
  fi
}

release_mutation_lease() {
  [[ -z "$DEVFLOW_LEASE_ID" ]] && return 0
  local lease="$DEVFLOW_LEASE_ID"
  DEVFLOW_LEASE_ID=""
  devflow "/api/v1/agent/lease" \
    -H 'Content-Type: application/json' \
    -d "{\"action\":\"release\",\"leaseId\":\"$lease\"}" >/dev/null
}

list_baseline_variants() {
  python3 -c "
import json
m = json.load(open('$REPO_ROOT/eng/validation/profiles/tizen-profiles.json'))
profile = next(p for p in m['profiles'] if p['id'] == '$TIZEN_PROFILE')
density_filter = '${TIZEN_DENSITY:-}'
if density_filter and density_filter not in profile['densities']:
    raise SystemExit(f\"density '{density_filter}' is not declared for profile '$TIZEN_PROFILE'\")
for theme in profile['themes']:
    for density in profile['densities']:
        if not density_filter or density == density_filter:
            print(f'{theme} {density}')
"
}

# ---------------------------------------------------------------------------
# Launch.
#
# Installing does not start anything. The previous version installed, forwarded and then queried
# the agent, which could only ever have worked if something else had already launched the app.
# ---------------------------------------------------------------------------
cmd_launch() {
  local app="${APP_ID:?APP_ID must be set to launch the application under test}"

  info "Launching $app"
  sdb_cmd shell app_launcher -s "$app"
}

# ---------------------------------------------------------------------------
# Wait for the in-app agent.
#
# The agent binds its port during application startup, so every query issued before then fails.
# Polling with a bounded timeout turns "queried too early" into a clear message instead of an
# intermittent connection error whose cause looks like the tunnel.
# ---------------------------------------------------------------------------
cmd_wait_for_agent() {
  local timeout="${AGENT_TIMEOUT_SECONDS:-60}"
  local waited=0

  info "Waiting up to ${timeout}s for the DevFlow agent"

  while (( waited < timeout )); do
    if devflow "agent/status" >/dev/null 2>&1; then
      pass "Agent responded after ${waited}s"
      return 0
    fi
    sleep 2
    waited=$((waited + 2))
  done

  fail "The DevFlow agent did not respond within ${timeout}s."
  fail "  Confirm the application was built WITH the agent compiled in. AddMauiDevFlowAgent() is"
  fail "  conventionally guarded by '#if DEBUG', so a Release build excludes it entirely; the"
  fail "  device lane builds with -p:MauiTizenValidation=true, which defines MAUITIZEN_DEVFLOW."
  fail "  See docs/validation/device-lane.md."
  exit 1
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

cmd_verify_device_profile() {
  local expected_idiom="phone"
  [[ "$TIZEN_PROFILE" == "tv" ]] && expected_idiom="tv"

  if ! devflow "agent/status" | python3 -c "
import json,sys
d=json.load(sys.stdin)
device=d.get('device') or {}
platform=str(device.get('platform', '')).lower()
actual_type=str(device.get('deviceType', '')).lower()
actual_idiom=str(device.get('idiom', '')).lower()
expected_profile='$TIZEN_PROFILE'
expected_idiom='$expected_idiom'
if platform != 'tizen' or actual_type != expected_profile or actual_idiom != expected_idiom:
    print(f'expected platform=tizen deviceType={expected_profile} idiom={expected_idiom}; '
          f'got platform={platform or \"<empty>\"} deviceType={actual_type or \"<empty>\"} '
          f'idiom={actual_idiom or \"<empty>\"}', file=sys.stderr)
    raise SystemExit(1)
"; then
    fail "The connected target does not match matrix profile '$TIZEN_PROFILE'."
    exit 1
  fi

  pass "Connected target identity matches '$TIZEN_PROFILE'"
}

cmd_verify_visual_target() {
  local expected_density="${TIZEN_DENSITY:?TIZEN_DENSITY must identify the configured visual target}"
  local target
  target="$(python3 -c "
import json
m=json.load(open('$REPO_ROOT/eng/validation/profiles/tizen-profiles.json'))
p=next(p for p in m['profiles'] if p['id'] == '$TIZEN_PROFILE')
t=p.get('visualTargets', {}).get('$expected_density')
if not t:
    raise SystemExit(\"no visual target metrics for $TIZEN_PROFILE/$expected_density\")
print(t['width'], t['height'], t['displayDensity'])
")"
  local expected_width expected_height expected_display_density
  read -r expected_width expected_height expected_display_density <<< "$target"

  if ! devflow "agent/status" | python3 -c "
import json,math,sys
d=json.load(sys.stdin)
device=d.get('device') or {}
actual_width=float(device.get('windowWidth', 0))
actual_height=float(device.get('windowHeight', 0))
actual_density=float(device.get('displayDensity', 0))
expected_width=float('$expected_width')
expected_height=float('$expected_height')
expected_density=float('$expected_display_density')
if (actual_width, actual_height) != (expected_width, expected_height) or not math.isclose(actual_density, expected_density, rel_tol=0, abs_tol=0.01):
    print(f\"visual target '$expected_density' expected {expected_width:g}x{expected_height:g} at density {expected_density:g}; \"
          f\"got {actual_width:g}x{actual_height:g} at density {actual_density:g}\", file=sys.stderr)
    raise SystemExit(1)
"; then
    fail "The connected target does not match visual configuration '$expected_density'."
    exit 1
  fi

  pass "Visual target '$expected_density' has the required effective metrics"
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
  claim_mutation_lease
  trap release_mutation_lease EXIT

  local visited=() key
  for key in Down Down Down Right Up Left; do
    devflow "ui/actions/key" \
      -H 'Content-Type: application/json' \
      -d "{\"key\":\"$key\"}" >/dev/null

    local focused
    focused="$(devflow "ui/elements?selector=%2A" \
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
  claim_mutation_lease
  trap release_mutation_lease EXIT

  sdb_cmd shell app_launcher -s "$app"
  sleep 3
  cmd_agent_status >/dev/null

  local before_pid
  before_pid="$(devflow "agent/status" | python3 -c "
import json,sys
print((json.load(sys.stdin).get('app') or {}).get('processId', ''))
")"
  if [[ -z "$before_pid" ]]; then
    fail "agent/status did not report app.processId before backgrounding."
    exit 1
  fi

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

  local after_pid
  after_pid="$(devflow "agent/status" | python3 -c "
import json,sys
print((json.load(sys.stdin).get('app') or {}).get('processId', ''))
")"

  if [[ -z "$after_pid" ]]; then
    fail "agent/status did not report app.processId after foregrounding."
    exit 1
  elif [[ "$after_pid" != "$before_pid" ]]; then
    fail "Process changed across the lifecycle cycle: before=$before_pid after=$after_pid."
    fail "  The app cold-started instead of resuming in process."
    exit 1
  fi

  pass "Process $before_pid survived the cycle (resume, not cold start)"
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
  claim_mutation_lease
  trap release_mutation_lease EXIT

  # The route is DISCOVERED, not guessed. DevFlow hosts extension routes
  # (AgentOptions.RegisterExtension -> AgentExtension.MapPost), but the URL prefix it composes is
  # its own concern, so the harness confirms the server advertises our extension before calling
  # anything. Calling a composed URL blind is how you end up asserting against a 404.
  local capabilities endpoint
  if ! capabilities="$(devflow "agent/capabilities")"; then
    fail "Could not read agent capabilities."
    exit 1
  fi

  endpoint="$(printf '%s' "$capabilities" | python3 -c "
import json, sys

NAMESPACE = 'org.dotnet.maui.tizen'
ROUTE = '/conventions/run'

try:
    data = json.load(sys.stdin)
except Exception:
    print(''); raise SystemExit

found_route = None
found_namespace = False

def walk(node):
    global found_route, found_namespace
    if isinstance(node, dict):
        if node.get('namespace') == NAMESPACE:
            found_namespace = True
        for value in node.values():
            walk(value)
    elif isinstance(node, list):
        for item in node:
            walk(item)
    elif isinstance(node, str):
        if node.endswith(ROUTE) and NAMESPACE in node:
            found_route = node

walk(data)

if found_route:
    print(found_route)
elif found_namespace:
    # The extension is advertised but no explicit route was published; compose the conventional
    # path. Only reached after confirming the server knows the extension.
    print(f'/api/v1/ext/{NAMESPACE}{ROUTE}')
else:
    print('')
")"

  if [[ -z "$endpoint" ]]; then
    fail "The application does not advertise the '$DEVFLOW_CONVENTIONS_NAMESPACE' DevFlow extension."
    fail "  The app under test must call AddMauiDevFlowAgent() (which registers the extension) and"
    fail "  register an IConventionAssertionProvider. Mapper parity and Essentials coverage need the"
    fail "  Tizen backend executing in-process, so they cannot run anywhere else."
    fail "  See samples/Maui.Tizen.Catalog/README.md."
    exit 1
  fi

  info "Using endpoint '$endpoint'"

  local response
  if ! response="$(devflow "$endpoint" -X POST)"; then
    fail "The on-device conventions endpoint returned an error."
    fail "  A 501 means no IConventionAssertionProvider is registered by the application."
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
# Zero assertions is not a pass: it is indistinguishable from a run that never happened.
raise SystemExit(1 if failed or total == 0 else 0)
"

  pass "On-device conventions passed"
}

cmd_baseline_sidecar() {
  local png="${1:?usage: baseline-sidecar <png> <case> <profile> <api> <theme> <density>}"
  local case_id="${2:?usage: baseline-sidecar <png> <case> <profile> <api> <theme> <density>}"
  local profile="${3:?usage: baseline-sidecar <png> <case> <profile> <api> <theme> <density>}"
  local api_level="${4:?usage: baseline-sidecar <png> <case> <profile> <api> <theme> <density>}"
  local theme="${5:?usage: baseline-sidecar <png> <case> <profile> <api> <theme> <density>}"
  local density="${6:?usage: baseline-sidecar <png> <case> <profile> <api> <theme> <density>}"

  python3 - "$png" "$case_id" "$profile" "$api_level" "$theme" "$density" \
    "$TIZEN_TFM" "${TIZEN_DEVICE_IMAGE:-unspecified}" <<'SIDECAR'
import json, struct, sys, subprocess, datetime, pathlib

png, case_id, profile, api_level, theme, density, target_framework, device_image = sys.argv[1:9]
data = pathlib.Path(png).read_bytes()
# IHDR width/height live at fixed offsets in every PNG.
width, height = struct.unpack('>II', data[16:24])

try:
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
except Exception:
    commit = ''

pathlib.Path(png).with_suffix('.json').write_text(json.dumps({
    'caseId': case_id,
    'profile': profile,
    'apiLevel': api_level,
    'theme': theme,
    'density': density,
    'targetFramework': target_framework,
    'deviceImage': device_image,
    'width': width,
    'height': height,
    'commit': commit,
    'capturedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z'),
}, indent=2) + '\n')
SIDECAR
}

# ---------------------------------------------------------------------------
# Visual baselines: capture AND compare.
#
# An earlier version only captured, so the lane could not fail on a rendering regression - it
# produced images nobody compared to anything.
#
# Screenshots land in the same folder shape as the baselines
# (profile/apiLevel/theme/density/case.png) so the comparison is an unambiguous
# one-to-one mapping rather than a guess. Comparison itself runs host-side through the
# deterministic comparer in Maui.Tizen.TestUtils; see VisualBaselineComparisonTests.
# ---------------------------------------------------------------------------
cmd_baselines() {
  local api_level
  api_level="$(python3 -c "import json;print(json.load(open('$REPO_ROOT/eng/baselines.json'))['target']['tizenFxApiLevel'])")"

  local variants
  variants="$(list_baseline_variants)"

  local cases
  cases="$(python3 -c "
import json
m = json.load(open('$REPO_ROOT/samples/Maui.Tizen.Catalog/catalog-manifest.json'))
print(' '.join(c['id'] for c in m['cases'] if c.get('capturesBaseline') and '$TIZEN_PROFILE' in c['profiles']))
")"

  if [[ -z "$cases" ]]; then
    fail "No baseline cases declared for profile '$TIZEN_PROFILE'. Capturing nothing would let the"
    fail "  comparison pass vacuously."
    exit 1
  fi

  claim_mutation_lease
  trap release_mutation_lease EXIT
  cmd_verify_visual_target

  local theme density out id capture_count=0 variant_count=0
  while read -r theme density; do
    [[ -z "$theme" || -z "$density" ]] && continue
    variant_count=$((variant_count + 1))
    out="$REPO_ROOT/artifacts/screenshots/$TIZEN_PROFILE/$api_level/$theme/$density"
    mkdir -p "$out"
    find "$out" -maxdepth 1 -type f \( -name '*.png' -o -name '*.json' \) -delete

    info "Capturing baselines for $TIZEN_PROFILE/$api_level/$theme/$density"

    for id in $cases; do
      if ! devflow "ui/actions/navigate" \
          -H 'Content-Type: application/json' \
          -d "{\"route\":\"$id\"}" >/dev/null; then
        fail "Could not navigate to '$id'."
        exit 1
      fi

      sleep 1

      if ! devflow "ui/screenshot?format=png" --output "$out/$id.png"; then
        fail "Could not capture '$id'."
        exit 1
      fi

      if [[ ! -s "$out/$id.png" ]]; then
        fail "Capture of '$id' produced an empty file."
        exit 1
      fi

      cmd_baseline_sidecar "$out/$id.png" "$id" "$TIZEN_PROFILE" "$api_level" "$theme" "$density"
      capture_count=$((capture_count + 1))
      echo "        captured $id"
    done
  done <<< "$variants"

  local expected_variants
  expected_variants="$(printf '%s\n' "$variants" | grep -c . | tr -d ' ')"
  if [[ "$variant_count" -ne "$expected_variants" || "$variant_count" -eq 0 ]]; then
    fail "Captured $variant_count of $expected_variants declared visual variants."
    exit 1
  fi

  pass "Captured $capture_count fresh screenshot(s) across $variant_count variant(s)"

  info "Comparing against checked-in baselines"

  # Only the comparison suite. Re-running the whole hosted lane here would spend scarce device
  # time re-proving things that never touch the device.
  MAUI_TIZEN_COMPARE_BASELINES=1 \
  MAUI_TIZEN_SCREENSHOT_PROFILE="$TIZEN_PROFILE" \
  MAUI_TIZEN_SUITES="Maui.Tizen.Validation.Tests" \
    "$REPO_ROOT/eng/validation/run-hosted-validation.sh"
}

case "${1:-}" in
  preflight)    shift; cmd_preflight "$@" ;;
  build)        shift; cmd_build "$@" ;;
  install)      shift; cmd_install "$@" ;;
  forward)      shift; cmd_forward "$@" ;;
  unforward)    shift; cmd_unforward "$@" ;;
  launch)          shift; cmd_launch "$@" ;;
  wait-for-agent)  shift; cmd_wait_for_agent "$@" ;;
  agent-status)    shift; cmd_agent_status "$@" ;;
  verify-device-profile) shift; cmd_verify_device_profile "$@" ;;
  verify-visual-target) shift; cmd_verify_visual_target "$@" ;;
  list-baseline-variants) shift; list_baseline_variants "$@" ;;
  remote-focus) shift; cmd_remote_focus "$@" ;;
  lifecycle)    shift; cmd_lifecycle "$@" ;;
  device-assertions) shift; cmd_device_assertions "$@" ;;
  baselines)         shift; cmd_baselines "$@" ;;
  baseline-sidecar)  shift; cmd_baseline_sidecar "$@" ;;
  *)
    sed -n '2,40p' "$0"
    exit 2
    ;;
esac
