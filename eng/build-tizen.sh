#!/usr/bin/env bash
#
# Real Tizen workload lane.
#
# This script is intentionally separate from build-workload-free.sh. It must run only
# after Samsung's supported installer has made the matching workload available, and every
# restore, build, or pack command is allowed to fail the caller directly.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"
PACKAGE_VERSION="${PackageVersion:-${PACKAGE_VERSION:-}}"
PACKAGE_OUTPUT_PATH="${MAUI_TIZEN_PACKAGE_OUTPUT_PATH:-$REPO_ROOT/artifacts/packages}"

if [[ "${MAUI_TIZEN_LOCAL_SEMANTICS:-0}" != "1" ]]; then
  export ContinuousIntegrationBuild=true
fi

# Every shipping project is explicit. Maui.Tizen.Build.Tasks is workload-independent, but it
# ships with the Tizen packages and must be built and packed by the same single release invocation.
SHIPPING_PROJECTS=(
  "src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj"
  "src/Diagnostics/Maui.Tizen.DevFlow.Agent/Maui.Tizen.DevFlow.Agent.csproj"
  "src/Maui.Tizen.Core/Maui.Tizen.Core.csproj"
  "src/Maui.Tizen.Controls/Maui.Tizen.Controls.csproj"
  "src/Maui.Tizen.Essentials/Maui.Tizen.Essentials.csproj"
  "src/Maui.Tizen.BlazorWebView/Maui.Tizen.BlazorWebView.csproj"
  "src/Maui.Tizen.Maps/Maui.Tizen.Maps.csproj"
  "src/Maui.Tizen.Graphics/Maui.Tizen.Graphics.csproj"
)

MSBUILD_ARGS=()
if [[ -n "$PACKAGE_VERSION" ]]; then
  "$REPO_ROOT/eng/release/release-contract.py" validate-version --version "$PACKAGE_VERSION"
  MSBUILD_ARGS+=("-p:Version=$PACKAGE_VERSION" "-p:PackageVersion=$PACKAGE_VERSION")
fi
if [[ -n "${SOURCE_COMMIT:-}" ]]; then
  MSBUILD_ARGS+=("-p:RepositoryCommit=$SOURCE_COMMIT")
fi

mkdir -p "$PACKAGE_OUTPUT_PATH"

echo "==> Restore shipping projects"
for project in "${SHIPPING_PROJECTS[@]}"; do
  if [[ "$project" == "src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj" \
      && "${MAUI_TIZEN_BUILD_TASKS_ALREADY_BUILT:-false}" == "true" ]]; then
    echo "  reuse   $project"
    continue
  fi
  echo "  restore $project"
  "$DOTNET" restore "$project" ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"}
done

echo "==> Build shipping projects"
for project in "${SHIPPING_PROJECTS[@]}"; do
  if [[ "$project" == "src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj" \
      && "${MAUI_TIZEN_BUILD_TASKS_ALREADY_BUILT:-false}" == "true" ]]; then
    echo "  reuse   $project"
    continue
  fi
  echo "  build   $project"
  "$DOTNET" build "$project" --no-restore -c Release ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"}
done

echo "==> Pack shipping projects exactly once"
for project in "${SHIPPING_PROJECTS[@]}"; do
  echo "  pack    $project"
  "$DOTNET" pack "$project" --no-restore --no-build -c Release \
    "-p:PackageOutputPath=$PACKAGE_OUTPUT_PATH" ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"}
done

echo "All shipping package restore/build/pack checks passed."
