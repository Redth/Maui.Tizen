#!/usr/bin/env bash
#
# run-linux-checks.sh — run the workload-free lane inside a Linux container.
#
# The Tizen build tasks rasterize images through SkiaSharp, whose native library resolution
# differs per operating system. A macOS-only run therefore cannot tell you whether the Linux CI
# agent will pass: that gap is exactly how "The type initializer for 'SkiaSharp.SKData' threw an
# exception" reached CI green-on-macOS.
#
# This script runs the same checks CI runs, on Linux, from a clean copy of the working tree.
#
#   eng/run-linux-checks.sh              # host architecture
#   eng/run-linux-checks.sh linux/amd64  # force x64, matching the GitHub-hosted agent
#
# Requires Docker. The NuGet cache is shared through a named volume so repeat runs are quick.
#
# REPOSITORY IDENTITY INSIDE THE CONTAINER.
#
# The lane packs three packages and asserts each carries a <repository commit="..."> stamp naming
# the revision being built. Inside the container there is no git history to read: the staged copy
# is a working tree, not a clone, and in a git WORKTREE the .git entry is not even a directory -
# it is a one-line file pointing at a path under the main repository's .git/worktrees, which does
# not exist in the container. Copying it therefore produces something that looks like a repository
# and behaves like a corrupt one, so `git rev-parse HEAD` fails and the documented Linux command
# could never finish a run.
#
# The revision is resolved and VERIFIED here on the host instead, and passed in:
#
#   MAUI_TIZEN_SOURCE_REVISION        the full 40-character commit id
#   MAUI_TIZEN_SOURCE_REVISION_STATE  'clean' when every package input matches that commit,
#                                     'dirty' when it does not
#
# The container lane refuses to claim provenance for a 'dirty' state unless the local-validation
# override is set, exactly as it would on the host. Passing the revision without the state would
# just move the false claim across the container boundary.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLATFORM="${1:-}"
BASE_IMAGE="mcr.microsoft.com/dotnet/sdk:11.0-preview"
# Tagged per platform: an image built for arm64 cannot satisfy --platform linux/amd64, and a
# shared tag would silently look "already built" and then fail to run.
IMAGE="maui-tizen-linux-checks:11.0-preview${PLATFORM:+-$(printf '%s' "$PLATFORM" | tr '/' '-')}"

PLATFORM_ARGS=()
if [[ -n "$PLATFORM" ]]; then
  PLATFORM_ARGS=(--platform "$PLATFORM")
fi

# ---------------------------------------------------------------------------
# Resolve and verify the revision the container will stamp its packages with.
# ---------------------------------------------------------------------------
if ! SOURCE_REVISION="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null)" || [[ ! "$SOURCE_REVISION" =~ ^[0-9a-f]{40}$ ]]; then
  echo "run-linux-checks: '$REPO_ROOT' is not a usable git working tree, so the container run" >&2
  echo "  would have no verifiable source revision to stamp its packages with." >&2
  exit 1
fi

if "$REPO_ROOT/eng/check-package-inputs-clean.sh" "$REPO_ROOT"; then
  SOURCE_REVISION_STATE="clean"
  echo "==> Source revision $SOURCE_REVISION (package inputs clean)"
else
  SOURCE_REVISION_STATE="dirty"
  echo "==> Source revision $SOURCE_REVISION (package inputs DIRTY)"
  if [[ "${MAUI_TIZEN_ALLOW_DIRTY_PROVENANCE:-0}" != "1" ]]; then
    echo "    The container lane will refuse to claim provenance for these packages." >&2
    echo "    Commit the package inputs, or set MAUI_TIZEN_ALLOW_DIRTY_PROVENANCE=1 to validate" >&2
    echo "    locally without claiming it." >&2
  fi
fi

STAGING="$(mktemp -d)"
trap 'rm -rf "$STAGING"' EXIT

# Copy the working tree, not HEAD: the point is to test what is about to be pushed. artifacts/ is
# excluded because macOS build output confuses an incremental Linux build.
#
# .git is excluded in BOTH spellings on purpose. '.git/' matches a directory only, which is what a
# normal clone has - but this repository is developed in git worktrees, where .git is a FILE, and
# the directory-only pattern let that file through. The result was worse than excluding it: git
# inside the container found a repository pointer, followed it to a path that does not exist, and
# failed in a way that read as a broken checkout rather than a missing exclusion.
echo "==> Staging working tree"
rsync -a \
  --exclude '.git' \
  --exclude '.git/' \
  --exclude 'artifacts/' \
  --exclude 'bin/' \
  --exclude 'obj/' \
  "$REPO_ROOT"/ "$STAGING"/

# The SDK image lacks python3 and unzip, both of which the checks use; the GitHub-hosted
# runner has both. Installing them closes a gap between the container and CI rather than
# papering over one - a missing tool otherwise surfaces as a failing assertion about the
# repository instead of about the environment.
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  echo "==> Building $IMAGE"
  docker build ${PLATFORM_ARGS[@]+"${PLATFORM_ARGS[@]}"} -t "$IMAGE" - <<DOCKERFILE
FROM $BASE_IMAGE
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 unzip \
 && rm -rf /var/lib/apt/lists/*
DOCKERFILE
fi

docker volume create maui-tizen-nuget >/dev/null

echo "==> Running workload-free checks on Linux${PLATFORM:+ ($PLATFORM)}"
docker run --rm \
  ${PLATFORM_ARGS[@]+"${PLATFORM_ARGS[@]}"} \
  -v "$STAGING:/src" \
  -v maui-tizen-nuget:/root/.nuget/packages \
  -w /src \
  -e DOTNET_NOLOGO=1 \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e ContinuousIntegrationBuild=true \
  -e "MAUI_TIZEN_SOURCE_REVISION=$SOURCE_REVISION" \
  -e "MAUI_TIZEN_SOURCE_REVISION_STATE=$SOURCE_REVISION_STATE" \
  -e "MAUI_TIZEN_ALLOW_DIRTY_PROVENANCE=${MAUI_TIZEN_ALLOW_DIRTY_PROVENANCE:-0}" \
  "$IMAGE" \
  bash -lc './eng/build-workload-free.sh'
