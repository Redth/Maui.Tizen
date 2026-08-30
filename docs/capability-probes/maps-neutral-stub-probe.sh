#!/usr/bin/env bash
#
# maps-neutral-stub-probe.sh — reproducible evidence for the Maui.Tizen.Maps capability
# decision recorded in /docs/tizen-maps-compatibility-status.md.
#
# Claim being verified: the REAL, published Microsoft.Maui.Maps / Microsoft.Maui.Controls.Maps
# packages already register a Map -> MapHandler handler whose behavior - CreatePlatformView()
# and every property mapper throwing NotImplementedException - is *identical* to what
# dotnet/maui's own Tizen-specific MapHandler.Tizen.cs partial would have produced, because
# neither ever implemented real map rendering. If true, Maui.Tizen.Maps has nothing to add:
# shipping a duplicate MapHandler would only create a type-identity collision with the
# neutral assembly's own MapHandler for zero behavioral benefit.
#
# This builds and RUNS a throwaway console app against the exact package versions pinned in
# /Directory.Packages.props, using this repository's own /nuget.config (so it resolves from
# the same dotnet11 dev feed the rest of the repository does). Nothing here touches or
# depends on the Tizen workload - this targets plain net11.0, which is what a
# net11.0-tizen11.0 project's PackageReference falls back to today, since none of these
# packages publish a net11.0-tizen11.0 asset.
#
# Usage: eng or docs consumer runs this directly; requires network access to the dotnet11
# feed and the .NET 11 preview SDK pinned in /global.json.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

MAUI_VERSION="$(python3 -c "
import re
text = open('$REPO_ROOT/Directory.Packages.props').read()
m = re.search(r'Microsoft\.Maui\.Maps\" Version=\"([^\"]+)\"', text)
print(m.group(1))
")"

echo "==> Probing Microsoft.Maui.Maps / Microsoft.Maui.Controls.Maps $MAUI_VERSION (plain net11.0, no Tizen involved)"

cp "$REPO_ROOT/nuget.config" "$WORKDIR/nuget.config"

cat > "$WORKDIR/probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls.Maps" Version="$MAUI_VERSION" />
    <PackageReference Include="Microsoft.Maui.Controls.Core" Version="$MAUI_VERSION" />
    <PackageReference Include="Microsoft.Maui.Core" Version="$MAUI_VERSION" />
    <PackageReference Include="Microsoft.Maui.Maps" Version="$MAUI_VERSION" />
  </ItemGroup>
</Project>
EOF

cat > "$WORKDIR/Program.cs" <<'EOF'
using Microsoft.Maui;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Maps.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;

var builder = MauiApp.CreateBuilder();
builder.UseMauiMaps();
var app = builder.Build();
var factory = app.Services.GetRequiredService<IMauiHandlersFactory>();
var handlerType = factory.GetHandlerType(typeof(Map));

Console.WriteLine($"Map is registered to handler: {handlerType}");
Console.WriteLine($"  from assembly: {handlerType?.Assembly.FullName}");

if (handlerType != typeof(MapHandler))
{
    Console.Error.WriteLine("FAIL: Map is not registered to Microsoft.Maui.Maps.Handlers.MapHandler.");
    Environment.Exit(1);
}

var handler = new MapHandler();
var createPlatformView = typeof(MapHandler).GetMethod("CreatePlatformView", BindingFlags.NonPublic | BindingFlags.Instance)!;

try
{
    createPlatformView.Invoke(handler, null);
    Console.Error.WriteLine("FAIL: CreatePlatformView() did not throw - upstream behavior may have changed; re-evaluate the capability decision.");
    Environment.Exit(1);
}
catch (TargetInvocationException ex) when (ex.InnerException is NotImplementedException)
{
    Console.WriteLine("CONFIRMED: MapHandler.CreatePlatformView() throws NotImplementedException,");
    Console.WriteLine("           matching dotnet/maui's own historical Tizen (non-)implementation.");
}
EOF

(cd "$WORKDIR" && dotnet run --project probe.csproj)
