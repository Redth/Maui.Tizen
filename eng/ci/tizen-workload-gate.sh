#!/usr/bin/env bash
#
# Fail-closed transition from an unavailable external workload to the real Tizen lane.
#
# Only a definitive 404 for both baseline-derived manifest IDs is informational success.
# Once either package exists, Samsung's supported installer and eng/build-tizen.sh become
# mandatory; any probe, install, detection, restore, build, or pack failure fails the job.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

CURL="${CURL:-curl}"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
NUGET_FLAT_CONTAINER_BASE="${NUGET_FLAT_CONTAINER_BASE:-https://api.nuget.org/v3-flatcontainer}"
TIZEN_REAL_WORKLOAD_LANE="${TIZEN_REAL_WORKLOAD_LANE:-$REPO_ROOT/eng/build-tizen.sh}"

# Pin Samsung's official installer so the gate does not execute a moving main-branch
# script. Passing the manifest version discovered below means this pin does not need a
# generated version-map entry for the new SDK band.
TIZEN_WORKLOAD_INSTALLER_URL="${TIZEN_WORKLOAD_INSTALLER_URL:-https://raw.githubusercontent.com/Samsung/Tizen.NET/a435a549085fdf8bb16de1ae4370f7c98236631c/workload/scripts/workload-install.sh}"

SUMMARY_FILE="${GITHUB_STEP_SUMMARY:-/dev/null}"

append_summary() {
  printf '%s\n' "$@" >> "$SUMMARY_FILE"
}

fail() {
  local message="$*"
  echo "::error::$message" >&2
  append_summary ":x: **${message}**"
  exit 1
}

BAND="$("$PYTHON" -c "import json; print(json.load(open('eng/baselines.json'))['target']['sdkBand'])")"
FEATURE_BAND="${BAND%%-*}"

PUBLISHED_ID=""
PUBLISHED_BAND=""
PUBLISHED_VERSION=""

probe_manifest() {
  local band="$1"
  local id="samsung.net.sdk.tizen.manifest-${band}"
  local response_file http_code version

  response_file="$(mktemp)"
  if ! http_code="$("$CURL" --silent --show-error --location \
      --output "$response_file" --write-out '%{http_code}' \
      "${NUGET_FLAT_CONTAINER_BASE}/${id}/index.json")"; then
    rm -f "$response_file"
    echo "::error::Failed to query ${id} on nuget.org." >&2
    return 2
  fi

  echo "  ${id} -> HTTP ${http_code}"

  case "$http_code" in
    200)
      if ! version="$("$PYTHON" - "$response_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    versions = json.load(stream).get("versions")

if not isinstance(versions, list) or not versions or not isinstance(versions[-1], str):
    raise SystemExit("manifest index has no usable versions")

print(versions[-1])
PY
      )"; then
        rm -f "$response_file"
        echo "::error::Could not read a workload version from ${id}." >&2
        return 2
      fi

      rm -f "$response_file"
      PUBLISHED_ID="$id"
      PUBLISHED_BAND="$band"
      PUBLISHED_VERSION="$version"
      return 0
      ;;
    404)
      rm -f "$response_file"
      return 1
      ;;
    *)
      rm -f "$response_file"
      echo "::error::Unexpected HTTP ${http_code} while querying ${id}; availability is unknown." >&2
      return 2
      ;;
  esac
}

append_summary "## Tizen workload gate" ""

if probe_manifest "$BAND"; then
  :
else
  status=$?
  if [[ $status -ne 1 ]]; then
    fail "The Samsung manifest probe failed; refusing to report an unavailable-manifest success."
  fi

  if [[ "$FEATURE_BAND" != "$BAND" ]]; then
    if probe_manifest "$FEATURE_BAND"; then
      :
    else
      status=$?
      if [[ $status -ne 1 ]]; then
        fail "The Samsung manifest probe failed; refusing to report an unavailable-manifest success."
      fi
    fi
  fi
fi

if [[ -z "$PUBLISHED_ID" ]]; then
  append_summary \
    ":hourglass: **Blocked on an external dependency -- this is expected.**" \
    "" \
    "Neither \`samsung.net.sdk.tizen.manifest-${BAND}\` nor" \
    "\`samsung.net.sdk.tizen.manifest-${FEATURE_BAND}\` is published on nuget.org." \
    "" \
    "The real \`net11.0-tizen11.0\` lane was not run because its supported workload" \
    "is unavailable; no neutral target framework or success-shaped fallback was used." \
    "See \`docs/migration.md\` and \`eng/baselines.json\`."
  echo "Samsung's .NET 11 Tizen manifest is not published; external gate remains informational."
  exit 0
fi

append_summary \
  ":white_check_mark: **Samsung published \`${PUBLISHED_ID}\` version \`${PUBLISHED_VERSION}\`.**" \
  "" \
  "Installing it through Samsung's supported workload installer, then running the real" \
  "\`net11.0-tizen11.0\` restore/build/pack lane. Any failure below fails this job."

TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TEMP_DIR"' EXIT

INSTALLER="$TEMP_DIR/workload-install.sh"
if ! "$CURL" --fail --silent --show-error --location \
    "$TIZEN_WORKLOAD_INSTALLER_URL" --output "$INSTALLER"; then
  fail "Failed to download Samsung's supported Tizen workload installer."
fi
bash -n "$INSTALLER"

DOTNET_INSTALL_DIR="${DOTNET_ROOT:-}"
if [[ -z "$DOTNET_INSTALL_DIR" ]]; then
  DOTNET_INSTALL_DIR="$("$PYTHON" - "$DOTNET" <<'PY'
import os
import shutil
import sys

command = shutil.which(sys.argv[1])
if command is None:
    raise SystemExit(f"dotnet command not found: {sys.argv[1]}")
print(os.path.dirname(os.path.realpath(command)))
PY
  )"
fi

[[ -x "$DOTNET_INSTALL_DIR/dotnet" ]] \
  || fail "Resolved .NET installation does not contain an executable dotnet host: ${DOTNET_INSTALL_DIR}/dotnet"

# Samsung's installer temporarily replaces a global.json in its working directory.
# Run it in an isolated directory with a copy of this repository's SDK pin so a failed
# install cannot disturb the checkout and cannot select a different installed SDK.
INSTALL_WORK_DIR="$TEMP_DIR/install"
mkdir -p "$INSTALL_WORK_DIR"
cp "$REPO_ROOT/global.json" "$INSTALL_WORK_DIR/global.json"

INSTALLER_ARGS=(
  --version "$PUBLISHED_VERSION"
  --dotnet-install-dir "$DOTNET_INSTALL_DIR"
)

# For a stable feature-band fallback package, -t makes Samsung's installer use the same
# manifest ID the probe found. For a preview-specific package, auto-detection is required:
# the installer's explicit -t path intentionally uses the stable feature-band ID.
if [[ "$PUBLISHED_BAND" == "$FEATURE_BAND" && "$PUBLISHED_BAND" != "$BAND" ]]; then
  INSTALLER_ARGS+=(--dotnet-target-version-band "$PUBLISHED_BAND")
fi

(
  cd "$INSTALL_WORK_DIR"
  DOTNET_ROOT="$DOTNET_INSTALL_DIR" bash -e "$INSTALLER" "${INSTALLER_ARGS[@]}"
)

if ! DETECTION_OUTPUT="$("$DOTNET" msbuild src/Maui.Tizen.Core/Maui.Tizen.Core.csproj \
    -t:ReportTizenWorkload -nologo -v:m 2>&1)"; then
  printf '%s\n' "$DETECTION_OUTPUT" >&2
  fail "The workload installer returned success, but repository workload detection failed."
fi

WORKLOAD_STATE="$(printf '%s\n' "$DETECTION_OUTPUT" \
  | grep -oE 'TizenWorkloadAvailable=(true|false)' \
  | tail -1 \
  | cut -d= -f2)"

[[ "$WORKLOAD_STATE" == "true" ]] \
  || fail "The workload installer returned success, but the expected Samsung manifest is still unavailable."

DOTNET="$DOTNET" "$TIZEN_REAL_WORKLOAD_LANE"

append_summary "" ":white_check_mark: **The real Tizen restore/build/pack lane passed.**"
