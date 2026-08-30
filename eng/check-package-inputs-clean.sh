#!/usr/bin/env bash
#
# check-package-inputs-clean.sh — is every input to the shippable packages committed?
#
# The workload-free lane packs three packages and then asserts that each one carries a
# <repository commit="..."> stamp naming HEAD. That assertion is only true if the sources that
# were packed ARE HEAD. With an uncommitted edit under src/Maui.Tizen.Build.Tasks the lane packed
# the working tree and stamped it with the commit of a tree that does not contain the edit - a
# package that points confidently at the wrong sources, which is worse than one with no stamp at
# all because nothing looks wrong.
#
# So the stamp is gated on this check rather than assumed.
#
# SCOPE. Only files that can change what the three packages CONTAIN:
#
#   src/Maui.Tizen.Build.Tasks/    the tasks package's sources, targets and packing rules
#   src/Maui.Tizen.Templates/      the template package's content and packing rules
#   eng/tests/PackReadmeProbe/     the third package the lane packs
#   Directory.Build.props/.targets, Directory.Packages.props, eng/Maui.props, eng/targets/
#                                  shared metadata, versions and the README pack item, all of
#                                  which land in every nuspec
#   global.json, nuget.config      the SDK and the feeds that decide what is compiled and against
#                                  what
#   README.md                      packed verbatim into every package
#
# Everything else is deliberately out of scope. Editing docs, tests or CI configuration cannot
# change a package's bytes, and blocking on those would train people to reach for the override -
# which is how a fail-closed check stops being one. Build OUTPUT is out of scope for a different
# reason: artifacts/ is git-ignored, and `git status --porcelain` does not report ignored files, so
# a tree full of generated packages and binaries still reports clean.
#
# Usage:  eng/check-package-inputs-clean.sh [repo-root]
#
# Exit codes:
#   0  every package input matches HEAD
#   1  at least one package input is modified, staged, deleted or untracked
#   2  the check could not be performed (not a git working tree, git unavailable)
#
# Exit 2 is deliberately distinct from exit 1: "dirty" and "cannot tell" call for different
# decisions, and a caller that collapses them either blocks a clean container run or accepts an
# unverified one.

set -euo pipefail

REPO_ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"

PACKAGE_INPUT_PATHS=(
  "src/Maui.Tizen.Build.Tasks"
  "src/Maui.Tizen.Templates"
  "eng/tests/PackReadmeProbe"
  "eng/Maui.props"
  "eng/targets"
  "Directory.Build.props"
  "Directory.Build.targets"
  "Directory.Packages.props"
  "global.json"
  "nuget.config"
  "README.md"
)

if ! command -v git >/dev/null 2>&1; then
  echo "check-package-inputs-clean: git is not available, so package input cleanliness cannot be determined." >&2
  exit 2
fi

if ! git -C "$REPO_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
  echo "check-package-inputs-clean: '$REPO_ROOT' has no usable git metadata, so package input cleanliness cannot be determined." >&2
  exit 2
fi

# --untracked-files=normal reports a new file inside a scoped directory, which matters: an
# uncommitted NEW source file changes the tasks assembly just as much as an edit to a tracked one.
DIRTY="$(git -C "$REPO_ROOT" status --porcelain --untracked-files=normal -- "${PACKAGE_INPUT_PATHS[@]}")"

if [[ -n "$DIRTY" ]]; then
  echo "check-package-inputs-clean: these package inputs do not match HEAD:"
  printf '%s\n' "$DIRTY" | sed 's/^/  /'
  exit 1
fi

exit 0
