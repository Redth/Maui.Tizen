#!/usr/bin/env bash
#
# compatibility-collision-probe.sh — reproducible evidence for the Maui.Tizen.Compatibility
# capability decision recorded in /docs/tizen-maps-compatibility-status.md.
#
# Claim being verified: none of the compatibility-renderer base infrastructure
# (VisualElementRenderer<TElement>, ViewHandlerDelegator<TElement>, ItemTemplateAdaptor)
# exists in the REAL, published, plain-net11.0 Microsoft.Maui.Controls.Core package. Unlike
# Maps (see maps-neutral-stub-probe.sh), there is no neutral "*.Standard.cs" fallback for
# these types - they only ever existed inside a platform-specific compilation upstream. That
# means a Tizen ListView/TableView/Frame compatibility renderer cannot be built today against
# any public net11 contract: it would need Maui.Tizen.Controls's own Tizen-specific
# ItemTemplateAdaptor (src/Maui.Tizen.Controls/Core/Handlers/Items/Tizen/ItemTemplateAdaptor.cs)
# and a from-scratch VisualElementRenderer/ViewHandlerDelegator, none of which compile yet
# (Phase 2, not started - see docs/migration.md). This corroborates
# src/Maui.Tizen.Compatibility/README.md's own "provisional, likely deleted" assessment with
# concrete, reproducible evidence rather than leaving it as an unverified guess.
#
# This builds a throwaway console app against the exact package version pinned in
# /Directory.Packages.props, using this repository's own /nuget.config. Plain net11.0 only -
# no Tizen workload involved.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

MAUI_VERSION="$(python3 -c "
import re
text = open('$REPO_ROOT/Directory.Packages.props').read()
m = re.search(r'Microsoft\.Maui\.Controls\.Core\" Version=\"([^\"]+)\"', text)
print(m.group(1))
")"

echo "==> Probing Microsoft.Maui.Controls.Core $MAUI_VERSION (plain net11.0, no Tizen involved)"

cp "$REPO_ROOT/nuget.config" "$WORKDIR/nuget.config"

cat > "$WORKDIR/probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls.Core" Version="$MAUI_VERSION" />
    <PackageReference Include="Microsoft.Maui.Core" Version="$MAUI_VERSION" />
  </ItemGroup>
</Project>
EOF

cat > "$WORKDIR/Program.cs" <<'EOF'
using System;

int failures = 0;

void CheckAbsent(string assemblyQualifiedName, string label)
{
    var t = Type.GetType(assemblyQualifiedName);
    if (t is null)
    {
        Console.WriteLine($"CONFIRMED ABSENT: {label}");
    }
    else
    {
        Console.Error.WriteLine($"FAIL: {label} unexpectedly FOUND ({t}) - re-evaluate the capability decision, a neutral fallback may now exist.");
        failures++;
    }
}

void CheckPresent(string assemblyQualifiedName, string label)
{
    var t = Type.GetType(assemblyQualifiedName);
    if (t is not null)
    {
        Console.WriteLine($"present (as expected, just a sanity check the assembly loaded): {label} -> {t}");
    }
    else
    {
        Console.Error.WriteLine($"FAIL: {label} not found - the probe itself may be broken (assembly failed to load).");
        failures++;
    }
}

// The controls (bindable objects) DO exist in the neutral assembly - only the renderer/
// adaptor infrastructure that would make them actually draw anything is missing. These are
// sanity checks that the probe is loading the right assembly at all.
CheckPresent("Microsoft.Maui.Controls.ListView, Microsoft.Maui.Controls", "ListView (control)");
CheckPresent("Microsoft.Maui.Controls.TableView, Microsoft.Maui.Controls", "TableView (control)");
CheckPresent("Microsoft.Maui.Controls.Frame, Microsoft.Maui.Controls", "Frame (control)");

// The renderer/adaptor infrastructure a Tizen compatibility renderer would need to derive
// from or implement against. None of these have a neutral/no-platform fallback upstream.
CheckAbsent("Microsoft.Maui.Controls.Handlers.Compatibility.VisualElementRenderer`1, Microsoft.Maui.Controls", "VisualElementRenderer<TElement>");
CheckAbsent("Microsoft.Maui.Controls.Handlers.Compatibility.ViewHandlerDelegator`1, Microsoft.Maui.Controls", "ViewHandlerDelegator<TElement> (internal)");
CheckAbsent("Microsoft.Maui.Controls.Handlers.Items.ItemTemplateAdaptor, Microsoft.Maui.Controls", "ItemTemplateAdaptor");

Environment.Exit(failures > 0 ? 1 : 0);
EOF

(cd "$WORKDIR" && dotnet run --project probe.csproj)
