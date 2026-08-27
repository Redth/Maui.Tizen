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
    Path to an existing net11 source checkout (sourceBaseline), produced by
    Get-MauiSourceSnapshot.ps1. Its .mt-snapshot.json provenance marker is validated against
    eng/baselines.json's pinned commit by RECOMPUTING its tree hash from the files currently on
    disk (not merely reading the marker) -- a directory that is not a verified, unmodified snapshot
    of exactly that commit is rejected rather than silently scanned. If omitted, one is downloaded
    to a cache directory under -CacheDir.

.PARAMETER LegacyRoot
    Path to an existing 9.0.120 source checkout (behaviorBaseline), validated the same way. If
    omitted, one is downloaded the same way.

.PARAMETER CacheDir
    Directory used to cache downloaded source snapshots across repeated runs. Defaults to a
    temp directory so CI runs never accidentally reuse stale state unless they opt in.

.EXAMPLE
    ./generate-source-inventory.ps1
    Downloads both snapshots fresh and regenerates the manifest.

.EXAMPLE
    ./generate-source-inventory.ps1 -PrimaryRoot /tmp/maui-net11 -LegacyRoot /tmp/maui-9.0.120
    Reuses already-downloaded, provenance-verified snapshots (fast local iteration).
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
$repository = $Baselines.source.repository -replace '^https://github.com/', ''
. (Join-Path $PSScriptRoot 'lib/Snapshot.ps1')

# A caller-supplied root must be a verified snapshot of exactly the pinned ref -- an arbitrary
# directory (an unrelated checkout, a stale snapshot from a different commit, a directory that
# merely looks right, or a directory whose marker is right but whose files have since been added,
# removed, or modified) is rejected rather than silently scanned, which would generate a manifest
# that looks legitimate but describes the wrong tree. Test-SnapshotIntegrity RECOMPUTES the tree
# hash from the files currently on disk; it never trusts the marker's claims on their own.
function Assert-VerifiedSnapshot([string]$dir, [string]$repo, [string]$ref, [string]$label) {
    $verification = Test-SnapshotIntegrity -Dir $dir -Repo $repo -Ref $ref
    if ($verification.ok) {
        return
    }

    switch ($verification.reason) {
        'wrong-repo-or-ref' {
            throw "-$label '$dir' is a verified snapshot of $($verification.marker.repository)@$($verification.marker.ref), not the pinned $repo@$ref. Refusing to scan a mismatched snapshot."
        }
        'tree-modified' {
            throw "-$label '$dir' claims to be a snapshot of $repo@$ref, but its recomputed tree hash ($($verification.computed.treeHash)) does not match its marker ($($verification.marker.treeHash)) -- a file was added, removed, or modified since it was extracted. Refusing to scan a directory whose contents no longer match its own provenance record."
        }
        default {
            throw "-$label '$dir' has no valid .mt-snapshot.json provenance marker (produced by Get-MauiSourceSnapshot.ps1), so it cannot be verified as $repo@$ref. Refusing to scan an unverified directory."
        }
    }
}

$primaryRef = $Baselines.source.sourceBaseline.commit
$legacyRef = $Baselines.source.behaviorBaseline.commit

if (-not $PrimaryRoot) {
    $PrimaryRoot = Join-Path $CacheDir "maui-$primaryRef"
    & (Join-Path $PSScriptRoot 'Get-MauiSourceSnapshot.ps1') -Repo $repository -Ref $primaryRef -OutDir $PrimaryRoot
}
else {
    Assert-VerifiedSnapshot -dir $PrimaryRoot -repo $repository -ref $primaryRef -label 'PrimaryRoot'
}

if (-not $LegacyRoot) {
    $LegacyRoot = Join-Path $CacheDir "maui-$legacyRef"
    & (Join-Path $PSScriptRoot 'Get-MauiSourceSnapshot.ps1') -Repo $repository -Ref $legacyRef -OutDir $LegacyRoot
}
else {
    Assert-VerifiedSnapshot -dir $LegacyRoot -repo $repository -ref $legacyRef -label 'LegacyRoot'
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
