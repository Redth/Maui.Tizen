#!/bin/bash -e
#
# Mirrors the relevant control flow in Samsung/Tizen.NET's pinned workload-install.sh.
# Its successful writable-path helper returns 1, and the caller intentionally continues
# without errexit. Running this fixture under `bash -e` aborts before the workload marker.
#

ensure_directory() {
  if [[ ! -d "$1" ]]; then
    mkdir -p "$1" || exit 1
  fi
  [[ ! -w "$1" ]] && exit 1
}

ensure_directory "$FAKE_INSTALL_DIRECTORY"
helper_status=$?
printf '%s\n' "$helper_status" > "$FAKE_HELPER_STATUS_LOG"
printf '%s\n' "$*" > "$FAKE_INSTALL_ARGS_LOG"

TMPDIR="$(mktemp -d)"
MANIFEST_NAME="fixture"
MANIFEST_VERSION="fixture"
# Keep this exact line aligned with Samsung's pinned installer; the gate replaces it.
# shellcheck disable=SC2086
curl -s -o $TMPDIR/manifest.zip -L https://www.nuget.org/api/v2/package/$MANIFEST_NAME/$MANIFEST_VERSION
cmp "$TIZEN_VERIFIED_MANIFEST_PACKAGE" "$TMPDIR/manifest.zip"
: > "$FAKE_VERIFIED_COPY_MARKER"
rm -rf "$TMPDIR"

if [[ "${FAKE_INSTALL_FAIL:-0}" == "1" ]]; then
  exit 91
fi

if [[ "${FAKE_INSTALL_NOOP:-0}" != "1" ]]; then
  : > "$FAKE_WORKLOAD_MARKER"
fi
