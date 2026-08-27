#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Collects the net-tizen Roslyn PublicAPI.{Shipped,Unshipped}.txt inputs from the pinned
    dotnet/maui net11 commit.

.DESCRIPTION
    The net11.0 branch of dotnet/maui no longer ships a buildable net11.0-tizen TFM (the Tizen
    workload manifest for net11 is not yet published -- see eng/baselines.json target.workloadManifest),
    but the source tree still carries the PublicAPI/net-tizen/*.txt analyzer baseline files for
    every project that used to target Tizen. Those text files are the only net11-era Tizen API
    surface record available without a Tizen build, so this script copies them verbatim (source
    text, not a compiled assembly) into eng/api-baselines/net11.0-publicapi.

.PARAMETER PrimaryRoot
    Path to an existing net11 source checkout, produced by Get-MauiSourceSnapshot.ps1. Its
    .mt-snapshot.json provenance marker is validated against eng/baselines.json's pinned commit by
    RECOMPUTING its tree hash from the files currently on disk (not merely reading the marker) --
    a directory that is not a verified, unmodified snapshot of exactly that commit is rejected. If
    omitted, one is downloaded using the commit pinned in eng/baselines.json
    (source.sourceBaseline.commit).
#>
[CmdletBinding()]
param(
    [string]$PrimaryRoot,
    [string]$CacheDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'maui-tizen-migration-cache')
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Baselines = Get-Content (Join-Path $RepoRoot 'eng/baselines.json') -Raw | ConvertFrom-Json
$repository = $Baselines.source.repository -replace '^https://github.com/', ''
$primaryRef = $Baselines.source.sourceBaseline.commit
$primaryLabel = $Baselines.source.sourceBaseline.branch
. (Join-Path $PSScriptRoot 'lib/Snapshot.ps1')

if (-not $PrimaryRoot) {
    $PrimaryRoot = Join-Path $CacheDir "maui-$primaryRef"
    & (Join-Path $PSScriptRoot 'Get-MauiSourceSnapshot.ps1') -Repo $repository -Ref $primaryRef -OutDir $PrimaryRoot
}
else {
    $verification = Test-SnapshotIntegrity -Dir $PrimaryRoot -Repo $repository -Ref $primaryRef
    if (-not $verification.ok) {
        switch ($verification.reason) {
            'wrong-repo-or-ref' {
                throw "-PrimaryRoot '$PrimaryRoot' is a verified snapshot of $($verification.marker.repository)@$($verification.marker.ref), not the pinned $repository@$primaryRef. Refusing to read a mismatched snapshot."
            }
            'tree-modified' {
                throw "-PrimaryRoot '$PrimaryRoot' claims to be a snapshot of $repository@$primaryRef, but its recomputed tree hash ($($verification.computed.treeHash)) does not match its marker ($($verification.marker.treeHash)) -- a file was added, removed, or modified since it was extracted. Refusing to read a directory whose contents no longer match its own provenance record."
            }
            default {
                throw "-PrimaryRoot '$PrimaryRoot' has no valid .mt-snapshot.json provenance marker, so it cannot be verified as $repository@$primaryRef. Refusing to read an unverified directory."
            }
        }
    }
}

# Repo-relative source path -> friendly output directory name (matching the target project names
# used in eng/manifests/source-disposition.json's "package" field where one exists).
$projects = [ordered]@{
    'src/BlazorWebView/src/Maui'      = 'BlazorWebView'
    'src/Controls/Maps/src'           = 'Controls.Maps'
    'src/Controls/src/Core'           = 'Controls.Core'
    'src/Controls/src/Xaml'           = 'Controls.Xaml'
    'src/Core/maps/src'               = 'Core.Maps'
    'src/Core/src'                    = 'Core'
    'src/Essentials/src'              = 'Essentials'
    'src/Graphics/src/Graphics.Skia'  = 'Graphics.Skia'
    'src/Graphics/src/Graphics'       = 'Graphics'
}

$outDir = Join-Path $RepoRoot 'eng/api-baselines/net11.0-publicapi'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$manifestProjects = @()
foreach ($entry in $projects.GetEnumerator()) {
    $srcDir = Join-Path $PrimaryRoot "$($entry.Key)/PublicAPI/net-tizen"
    $shipped = Join-Path $srcDir 'PublicAPI.Shipped.txt'
    $unshipped = Join-Path $srcDir 'PublicAPI.Unshipped.txt'

    if (-not (Test-Path $shipped) -or -not (Test-Path $unshipped)) {
        Write-Warning "Missing PublicAPI/net-tizen files under $($entry.Key) -- skipping"
        continue
    }

    $dstDir = Join-Path $outDir $entry.Value
    New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    Copy-Item $shipped (Join-Path $dstDir 'PublicAPI.Shipped.txt') -Force
    Copy-Item $unshipped (Join-Path $dstDir 'PublicAPI.Unshipped.txt') -Force

    $shippedHash = (Get-FileHash (Join-Path $dstDir 'PublicAPI.Shipped.txt') -Algorithm SHA256).Hash.ToLowerInvariant()
    $unshippedHash = (Get-FileHash (Join-Path $dstDir 'PublicAPI.Unshipped.txt') -Algorithm SHA256).Hash.ToLowerInvariant()

    $manifestProjects += [ordered]@{
        project          = $entry.Value
        sourcePath        = "$($entry.Key)/PublicAPI/net-tizen"
        shippedSha256     = $shippedHash
        unshippedSha256   = $unshippedHash
    }

    Write-Host "Collected $($entry.Value)"
}

$manifest = [ordered]@{
    schemaVersion  = 1
    repository     = $Baselines.source.repository
    sourceRef      = $primaryRef
    sourceRefLabel = $primaryLabel
    projects       = $manifestProjects
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $outDir 'manifest.json') -Encoding utf8

Write-Host "Regenerated $outDir (+ manifest.json) with $($manifestProjects.Count) projects"
