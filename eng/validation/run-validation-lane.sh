#!/usr/bin/env bash
#
# Wave C compile-verification lane.
#
# The shipping target framework (net11.0-tizen11.0) cannot be restored anywhere until
# Samsung publishes samsung.net.sdk.tizen.manifest-11.0.100. This script builds the same
# sources against the repository's declared behaviourBaseline, net9.0-tizen7.0, so the
# migrated code is checked by a real compiler against real TizenFX and MAUI references.
#
# It provisions an ISOLATED .NET SDK under artifacts/ rather than touching the developer's
# machine-wide dotnet installation, because installing workloads mutates shared state and
# can change how unrelated repositories build.
#
# Usage:
#   ./eng/validation/run-validation-lane.sh
#
# Environment:
#   MAUI_TIZEN_VALIDATION_DOTNET_ROOT   Reuse an existing SDK that already has the
#                                       'tizen' and 'maui-tizen' workloads installed.

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

# Keep these in sync with eng/baselines.json > source.behaviorBaseline.
sdk_channel="9.0"
sdk_band="9.0.100"
# Newest Samsung manifest published for the 9.0.100 band.
tizen_manifest_version="8.0.159"
tizen_manifest_package="samsung.net.sdk.tizen.manifest-${sdk_band}"

dotnet_root="${MAUI_TIZEN_VALIDATION_DOTNET_ROOT:-$repo_root/artifacts/validation-sdk}"
provisioned_marker="$dotnet_root/.maui-tizen-validation-ready"

provision() {
  echo "==> Provisioning isolated SDK for the validation lane at: $dotnet_root"

  mkdir -p "$dotnet_root"

  if [[ ! -x "$dotnet_root/dotnet" ]]; then
    echo "--> Installing .NET SDK $sdk_channel"
    local installer="$dotnet_root/dotnet-install.sh"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    chmod +x "$installer"
    "$installer" --channel "$sdk_channel" --install-dir "$dotnet_root" --no-path
  fi

  # Samsung does not ship the Tizen workload through the in-box workload manifests, so the
  # manifest package is fetched from nuget.org and dropped into the SDK before installing.
  local manifest_dir="$dotnet_root/sdk-manifests/$sdk_band/samsung.net.sdk.tizen/$tizen_manifest_version"
  if [[ ! -f "$manifest_dir/WorkloadManifest.json" ]]; then
    echo "--> Installing Samsung Tizen workload manifest $tizen_manifest_version"
    local staging
    staging="$(mktemp -d)"
    curl -fsSL \
      "https://api.nuget.org/v3-flatcontainer/${tizen_manifest_package}/${tizen_manifest_version}/${tizen_manifest_package}.${tizen_manifest_version}.nupkg" \
      -o "$staging/manifest.nupkg"
    (cd "$staging" && unzip -q manifest.nupkg)
    mkdir -p "$manifest_dir"
    cp "$staging/data/WorkloadManifest.json" "$staging/data/WorkloadManifest.targets" "$manifest_dir/"
    rm -rf "$staging"
  fi

  # Run workload installs from a directory with no global.json so the CLI does not try to
  # resolve the repository's net11 SDK pin.
  local clean_dir="$dotnet_root/.workload-install"
  mkdir -p "$clean_dir"

  echo "--> Installing workloads: maui-tizen, tizen"
  (cd "$clean_dir" && "$dotnet_root/dotnet" workload install maui-tizen tizen --skip-sign-check)

  touch "$provisioned_marker"
}

if [[ ! -f "$provisioned_marker" ]]; then
  provision
fi

export DOTNET_ROOT="$dotnet_root"
export PATH="$dotnet_root:$PATH"
export DOTNET_MULTILEVEL_LOOKUP=0
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "==> Building the Wave C validation lane (net9.0-tizen7.0)"

# cd into eng/validation so the CLI picks up eng/validation/global.json rather than the
# repository root pin.
cd "$script_dir"
exec "$dotnet_root/dotnet" build \
  Maui.Tizen.Controls.Navigation.Validation.csproj \
  -p:EnableTizenValidationLane=true \
  "$@"
