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

if [[ "${MAUI_TIZEN_LOCAL_SEMANTICS:-0}" != "1" ]]; then
  export ContinuousIntegrationBuild=true
fi

# Every current product project that imports eng/targets/TizenPackage.props is listed
# explicitly. The transition regression test rejects a newly added Tizen project that is
# not added here, including a future sample or package project.
TIZEN_PROJECTS=(
  "src/Diagnostics/Maui.Tizen.DevFlow.Agent/Maui.Tizen.DevFlow.Agent.csproj"
  "src/Maui.Tizen.Core/Maui.Tizen.Core.csproj"
  "src/Maui.Tizen.Controls/Maui.Tizen.Controls.csproj"
  "src/Maui.Tizen.Essentials/Maui.Tizen.Essentials.csproj"
  "src/Maui.Tizen.BlazorWebView/Maui.Tizen.BlazorWebView.csproj"
  "src/Maui.Tizen.Maps/Maui.Tizen.Maps.csproj"
  "src/Maui.Tizen.Graphics/Maui.Tizen.Graphics.csproj"
)

echo "==> Restore net11.0-tizen11.0 projects"
for project in "${TIZEN_PROJECTS[@]}"; do
  echo "  restore $project"
  "$DOTNET" restore "$project"
done

echo "==> Build net11.0-tizen11.0 projects"
for project in "${TIZEN_PROJECTS[@]}"; do
  echo "  build   $project"
  "$DOTNET" build "$project" --no-restore -c Release
done

echo "==> Pack net11.0-tizen11.0 projects"
for project in "${TIZEN_PROJECTS[@]}"; do
  echo "  pack    $project"
  "$DOTNET" pack "$project" --no-restore --no-build -c Release
done

echo "All Tizen workload restore/build/pack checks passed."
