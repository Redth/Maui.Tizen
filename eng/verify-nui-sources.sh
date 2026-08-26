#!/usr/bin/env bash
#
# verify-nui-sources.sh — compile-check the NUI sources without the Samsung Tizen workload.
#
# The net11.0-tizen11.0 projects cannot be restored or built until Samsung publishes
# 'samsung.net.sdk.tizen.manifest-11.0.100' (eng/baselines.json > target.workloadManifest).
# That gate stops the Tizen projects from building, but it does not have to stop the NUI
# sources from being TYPE-CHECKED: TizenFX ships reference assemblies as an ordinary NuGet
# package, and Tizen.UIExtensions ships a plain managed assembly. Both can be referenced
# directly from a neutral net11.0 compilation.
#
# This is compile checking only. It produces no shippable artifact, it is not a neutral
# fallback for the product, and it is not a substitute for device tests. What it does is
# stop the NUI half of the backend from rotting silently while the workload is unavailable.
#
# It has already earned its keep: it is how the call to
# Tizen.NUI.LongPressGestureDetector.SetMinimumHoldingTime inherited from dotnet/maui was
# found to reference a method that does not exist in TizenFX. See
# docs/tizen-gesture-support-matrix.md.
#
# Requires network access on first run. Exits 0 and reports a skip if the packages cannot
# be fetched, so it is safe to run in an offline environment.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-dotnet}"
WORK="${TMPDIR:-/tmp}/maui-tizen-nui-verify"

# Pinned to eng/baselines.json > target.referencePack and Directory.Packages.props.
# API13 is used rather than API15 because it is the newest TizenFX package that publishes
# ref/net6.0 assemblies consumable from a neutral compilation; the gesture, window and
# popup surfaces this backend uses are identical in both.
TIZEN_REF_PKG="tizen.net.api13"
TIZEN_REF_VER="13.0.0.19198"
UIEXT_PKG="tizen.uiextensions.nui"
UIEXT_VER="0.9.2"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
skip() { printf '\033[1;33m  SKIP\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m  FAIL\033[0m %s\n' "$*"; }

fetch() {
  local pkg="$1" ver="$2" dest="$3"
  [[ -d "$dest" ]] && return 0
  local url="https://api.nuget.org/v3-flatcontainer/${pkg}/${ver}/${pkg}.${ver}.nupkg"
  mkdir -p "$dest"
  curl -fsSL "$url" -o "$dest/pkg.zip" && unzip -q -o "$dest/pkg.zip" -d "$dest"
}

info "Reference assemblies"
mkdir -p "$WORK"
if ! fetch "$TIZEN_REF_PKG" "$TIZEN_REF_VER" "$WORK/tizenref" ||
   ! fetch "$UIEXT_PKG" "$UIEXT_VER" "$WORK/uiext"; then
  skip "could not download TizenFX / Tizen.UIExtensions packages (offline?); NUI sources not checked"
  exit 0
fi

TIZEN_REF_DIR="$WORK/tizenref/ref/net6.0"
UIEXT_DLL="$WORK/uiext/lib/net6.0-tizen7.0/Tizen.UIExtensions.NUI.dll"

for f in "$TIZEN_REF_DIR/Tizen.NUI.dll" "$UIEXT_DLL"; do
  if [[ ! -f "$f" ]]; then
    skip "expected assembly missing: $f; NUI sources not checked"
    exit 0
  fi
done
pass "TizenFX $TIZEN_REF_VER and Tizen.UIExtensions.NUI $UIEXT_VER available"

info "Compile-checking Controls platform sources"
PROJ_DIR="$WORK/proj"
mkdir -p "$PROJ_DIR"

# The harness pins the same SDK as the repository but must not inherit the repository's
# Directory.Build.* conventions, hence its own directory outside the tree.
cp global.json "$PROJ_DIR/global.json"
cp nuget.config "$PROJ_DIR/nuget.config"
printf '<Project />\n' > "$PROJ_DIR/Directory.Build.props"
printf '<Project />\n' > "$PROJ_DIR/Directory.Build.targets"

MAUI_VER="$(python3 -c "
import re,sys
props=open('Directory.Packages.props').read()
m=re.search(r'PackageVersion Include=\"Microsoft.Maui.Controls\" Version=\"([^\"]+)\"',props)
sys.stdout.write(m.group(1) if m else '')")"

if [[ -z "$MAUI_VER" ]]; then
  fail "could not read the Microsoft.Maui.Controls version from Directory.Packages.props"
  exit 1
fi

cat > "$PROJ_DIR/NuiCompileCheck.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- NU1701/MSB3277 are expected: Tizen.UIExtensions targets net6.0-tizen7.0 and still
         carries .NET 6-era Microsoft.Maui.Graphics references. Neither affects type checking.

         CA1416 is expected and is the whole point: TizenFX and Tizen.UIExtensions annotate
         their surface with [SupportedOSPlatform("tizen7.0")], so calling it from a neutral
         compilation is "unsupported" by definition. The real project targets
         net11.0-tizen11.0 where the annotation is satisfied. Suppressing it here does not
         weaken the product build, which never sees this file. -->
    <NoWarn>\$(NoWarn);CS1591;NU1701;MSB3277;CA1416</NoWarn>
    <SourceRoot>$REPO_ROOT/src/Maui.Tizen.Controls/Core/Platform/</SourceRoot>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="$MAUI_VER" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Tizen.NUI"><HintPath>$TIZEN_REF_DIR/Tizen.NUI.dll</HintPath></Reference>
    <Reference Include="Tizen.NUI.Components"><HintPath>$TIZEN_REF_DIR/Tizen.NUI.Components.dll</HintPath></Reference>
    <Reference Include="Tizen.UIExtensions.NUI"><HintPath>$UIEXT_DLL</HintPath></Reference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="\$(SourceRoot)Alerts/*.cs" />
    <Compile Include="\$(SourceRoot)Gestures/*.cs" />
    <Compile Include="\$(SourceRoot)Nui/*.cs" />
    <Compile Include="\$(SourceRoot)TizenControlsServiceCollectionExtensions.cs" />
  </ItemGroup>
</Project>
PROJ

if "$DOTNET" build "$PROJ_DIR/NuiCompileCheck.csproj" -v:q --nologo > "$WORK/build.log" 2>&1; then
  pass "Controls platform sources (including Core/Platform/Nui) type-check against TizenFX"
else
  fail "NUI sources do not compile"
  sed 's/^/        /' "$WORK/build.log" | tail -40
  exit 1
fi
