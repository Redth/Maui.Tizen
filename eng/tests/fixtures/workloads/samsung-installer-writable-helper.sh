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

if [[ "${FAKE_INSTALL_FAIL:-0}" == "1" ]]; then
  exit 91
fi

if [[ "${FAKE_INSTALL_NOOP:-0}" != "1" ]]; then
  : > "$FAKE_WORKLOAD_MARKER"
fi
