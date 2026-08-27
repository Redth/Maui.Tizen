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

STAGING="$(mktemp -d)"
trap 'rm -rf "$STAGING"' EXIT

# Copy the working tree, not HEAD: the point is to test what is about to be pushed. artifacts/ is
# excluded because macOS build output confuses an incremental Linux build, and .git because the
# checks never read history.
echo "==> Staging working tree"
rsync -a \
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
  "$IMAGE" \
  bash -lc './eng/build-workload-free.sh'
