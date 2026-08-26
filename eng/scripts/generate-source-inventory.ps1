#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates eng/manifests/source-disposition.json and its Markdown summary.

.DESCRIPTION
    Downloads (or reuses cached) source snapshots for both baselines recorded in
    eng/baselines.json, builds eng/tools/SourceInventory, and runs it to produce a deterministic
    inventory of every Tizen-specific-path file and every shared file containing a Tizen
    conditional-compilation branch or platform-identity reference, validated against
    eng/manifests/source-disposition.schema.json.

    Both baselines are always scanned: a net11-only pass silently under-reports by the Tizen-named
    files that exist only in the last-published (9.0.120) tree, e.g. the legacy top-level
    src/Compatibility/** stack that was deleted upstream before net11.0. See docs/migration.md.

    Does not require a Tizen workload: the scan is a pure file-system walk + text/regex scan.

.PARAMETER PrimaryRoot
    Path to an existing net11 source checkout (sourceBaseline). If omitted, one is downloaded to a
    cache directory under -CacheDir using the commit pinned in eng/baselines.json.

.PARAMETER LegacyRoot
    Path to an existing 9.0.120 source checkout (behaviorBaseline). If omitted, one is downloaded
    the same way.

.PARAMETER CacheDir
    Directory used to cache downloaded source snapshots across repeated runs. Defaults to a
    temp directory so CI runs never accidentally reuse stale state unless they opt in.

.EXAMPLE
    ./generate-source-inventory.ps1
    Downloads both snapshots fresh and regenerates the manifest.

.EXAMPLE
    ./generate-source-inventory.ps1 -PrimaryRoot /tmp/maui-net11 -LegacyRoot /tmp/maui-9.0.120
    Reuses already-downloaded snapshots (fast local iteration).
#>
[CmdletBinding()]
param(
    [string]$PrimaryRoot,
    [string]$LegacyRoot,
    [string]$CacheDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'maui-tizen-migration-cache')
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Baselines = Get-Content (Join-Path $RepoRoot 'eng/baselines.json') -Raw | ConvertFrom-Json

if (-not $PrimaryRoot) {
    $primaryRef = $Baselines.source.sourceBaseline.commit
    $PrimaryRoot = Join-Path $CacheDir "maui-$primaryRef"
    & (Join-Path $PSScriptRoot 'Get-MauiSourceSnapshot.ps1') -Ref $primaryRef -OutDir $PrimaryRoot
}

if (-not $LegacyRoot) {
    $legacyRef = $Baselines.source.behaviorBaseline.commit
    $LegacyRoot = Join-Path $CacheDir "maui-$legacyRef"
    & (Join-Path $PSScriptRoot 'Get-MauiSourceSnapshot.ps1') -Ref $legacyRef -OutDir $LegacyRoot
}

$toolDir = Join-Path $RepoRoot 'eng/tools/SourceInventory'
Write-Host "Building SourceInventory tool"
dotnet build $toolDir -c Release -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for SourceInventory" }

$outJson = Join-Path $RepoRoot 'eng/manifests/source-disposition.json'
$outMd = Join-Path $RepoRoot 'eng/manifests/source-disposition.summary.md'

Write-Host "Running SourceInventory"
& dotnet run --no-build -c Release --project $toolDir -- `
    --baselines (Join-Path $RepoRoot 'eng/baselines.json') `
    --primary-root $PrimaryRoot `
    --legacy-root $LegacyRoot `
    --out $outJson `
    --summary-out $outMd
if ($LASTEXITCODE -ne 0) { throw "SourceInventory tool failed" }

Write-Host "Regenerated:"
Write-Host "  $outJson"
Write-Host "  $outMd"
