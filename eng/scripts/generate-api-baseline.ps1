#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates the eng/api-baselines/net9.0-tizen7.0 public API surface baseline.

.DESCRIPTION
    Downloads the last-published MAUI NuGet packages that ship a net9.0-tizen7.0 assembly (per
    eng/baselines.json's behaviorBaseline), verifies each against a checked-in trust anchor,
    extracts the Tizen-TFM assembly from each, and runs eng/tools/ApiDump against it. ApiDump reads
    assemblies purely via System.Reflection.Metadata (never loads/executes them), so this requires
    only NuGet network access -- no Tizen workload, emulator, or device.

    Trust model (deliberately NOT trust-on-first-use):
      - eng/api-baselines/net9.0-tizen7.0-package-trust.json is the source of truth for which
        package+version+hash combinations this script will ever accept. Every package this script
        downloads MUST already have a pinned SHA-256 entry there; an unknown package+version pair
        is refused outright rather than silently trusted and recorded. See that file's header
        comment for how its pinned hashes were originally established.
      - After downloading (or reusing a cached copy), the file's SHA-256 is compared against the
        pinned value. Any mismatch -- corruption, a compromised feed, a tampered local cache -- is
        a hard failure. The local cache is purely a performance shortcut for skipping a redundant
        download when the cached bytes already match the pin; it is never itself a trust source.
      - Independently of the hash pin, every package's NuGet signature is verified via
        eng/tools/PackageVerify, which performs REAL package-signature verification (integrity:
        does the current package content still match what was signed; trust: does the signing
        certificate chain to a trusted root) rather than a bare
        System.Security.Cryptography.Pkcs.SignedCms check on the isolated .signature.p7s blob --
        which only proves that blob is internally self-consistent, not that it still matches the
        package around it. This is defense-in-depth alongside the hash pin, not a replacement: the
        two checks fail for different reasons (hash pin catches "this isn't the file we trust";
        signature verification catches "this file's content no longer matches its own signature",
        which would also be caught by the hash pin here, but is the check that generalizes to any
        future package this repository has never seen a pinned hash for).

    Output is cleared and fully regenerated on every run -- never merged with a previous run's
    files, so a renamed/removed source assembly cannot leave a stale dump behind.

.PARAMETER PackageVersion
    NuGet package version to download. Defaults to eng/baselines.json's behaviorBaseline tag
    (9.0.120), which must match the trust anchor file's packageVersion. Override only for
    exploratory/manual runs -- the checked-in baseline should always correspond to the pinned
    version and its own trust anchor.

.PARAMETER CacheDir
    Directory used to cache downloaded .nupkg files across repeated runs.
#>
[CmdletBinding()]
param(
    [string]$PackageVersion,
    [string]$CacheDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'maui-tizen-migration-cache/nuget')
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Baselines = Get-Content (Join-Path $RepoRoot 'eng/baselines.json') -Raw | ConvertFrom-Json

if (-not $PackageVersion) {
    $PackageVersion = $Baselines.source.behaviorBaseline.tag
}

$trustAnchorPath = Join-Path $RepoRoot 'eng/api-baselines/net9.0-tizen7.0-package-trust.json'
if (-not (Test-Path $trustAnchorPath)) {
    throw "Missing trust anchor: $trustAnchorPath. Every package this script downloads must have a pinned SHA-256 there before this script will run at all."
}
$trustAnchor = Get-Content $trustAnchorPath -Raw | ConvertFrom-Json
if ($trustAnchor.packageVersion -ne $PackageVersion) {
    throw "Trust anchor is pinned to version '$($trustAnchor.packageVersion)' but -PackageVersion is '$PackageVersion'. Regenerate $trustAnchorPath for the new version (see its header comment) before changing versions."
}
$trustedHashes = @{}
foreach ($p in $trustAnchor.packages) {
    $trustedHashes[$p.packageId] = $p.nupkgSha256.ToLowerInvariant()
}

# Every Microsoft.Maui.* package that ships a net9.0-tizen7.0 asset at this version, and the
# single assembly filename expected inside it. Microsoft.Maui.Controls (the meta-package) and
# Microsoft.Maui.Controls.Build.Tasks / Microsoft.Maui.Resizetizer (MSBuild task packages, no
# TFM-specific assembly) are intentionally excluded -- there is no Tizen-specific managed
# assembly to dump for them.
$ExpectedAssemblies = [ordered]@{
    'Microsoft.Maui.Core'                   = 'Microsoft.Maui.dll'
    'Microsoft.Maui.Essentials'              = 'Microsoft.Maui.Essentials.dll'
    'Microsoft.Maui.Graphics'                 = 'Microsoft.Maui.Graphics.dll'
    'Microsoft.Maui.Controls.Core'            = 'Microsoft.Maui.Controls.dll'
    'Microsoft.Maui.Controls.Xaml'            = 'Microsoft.Maui.Controls.Xaml.dll'
    'Microsoft.Maui.Controls.Compatibility'   = 'Microsoft.Maui.Controls.Compatibility.dll'
    'Microsoft.Maui.Controls.Maps'            = 'Microsoft.Maui.Controls.Maps.dll'
}
$tfm = 'net9.0-tizen7.0'

foreach ($id in $ExpectedAssemblies.Keys) {
    if (-not $trustedHashes.ContainsKey($id)) {
        throw "'$id' has no pinned hash in $trustAnchorPath -- refusing to download an unpinned package. Add it to the trust anchor (with a hash established the same way as its siblings) before adding it here."
    }
}

function Get-Sha256Hex([string]$path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Download-or-reuse against the PINNED hash, not trust-on-first-use: the cache is purely a
# performance shortcut. A cached file is only reused if it ALREADY matches the pin; otherwise it
# is re-downloaded and the fresh copy is what gets checked against the pin (never blindly trusted
# just because it came from the network either -- see the caller's hash comparison afterward).
function Get-PinnedNupkg([string]$idLower, [string]$version, [string]$cacheDir) {
    $nupkgPath = Join-Path $cacheDir "$idLower.$version.nupkg"

    if (Test-Path $nupkgPath) {
        Write-Host "  found cached $idLower.$version.nupkg (verifying against pin before reuse)"
        return $nupkgPath
    }

    $url = "https://api.nuget.org/v3-flatcontainer/$idLower/$version/$idLower.$version.nupkg"
    Write-Host "  downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $nupkgPath -UseBasicParsing
    return $nupkgPath
}

New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
$extractDir = Join-Path $CacheDir "extract-$PackageVersion"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

$verifyToolDir = Join-Path $RepoRoot 'eng/tools/PackageVerify'
Write-Host "Building PackageVerify tool"
dotnet build $verifyToolDir -c Release -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for PackageVerify" }

$assemblies = @()
$packageManifest = @()

foreach ($entry in $ExpectedAssemblies.GetEnumerator()) {
    $id = $entry.Key
    $expectedAssemblyName = $entry.Value
    $idLower = $id.ToLowerInvariant()
    $expectedHash = $trustedHashes[$id]

    Write-Host "$id"
    $nupkgPath = Get-PinnedNupkg -idLower $idLower -version $PackageVersion -cacheDir $CacheDir
    $actualHash = Get-Sha256Hex $nupkgPath

    if ($actualHash -ne $expectedHash) {
        throw "$id.$PackageVersion.nupkg SHA-256 is $actualHash but the trust anchor pins $expectedHash. Refusing to use this file -- this is either NuGet feed corruption/tampering or a stale local cache; delete '$nupkgPath' and re-run to redownload, or if nuget.org genuinely republished this version's bytes, that is itself a serious integrity problem worth investigating rather than silently accepting."
    }
    Write-Host "  sha256 $actualHash matches pinned trust anchor"

    $verifyOutput = & dotnet run --no-build -c Release --project $verifyToolDir -- $nupkgPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$id.${PackageVersion}: PackageVerify tool itself failed to run: $verifyOutput"
    }
    $verifyResult = $verifyOutput | Select-Object -Last 1 | ConvertFrom-Json
    if (-not $verifyResult.isValid) {
        throw "$id.${PackageVersion}: NuGet package signature verification failed despite matching the pinned hash: $($verifyResult.errors -join '; '). This should not happen for a hash that matches the pin -- investigate before proceeding."
    }
    Write-Host "  signature verified (signed=$($verifyResult.isSigned) type=$($verifyResult.signatureType) valid=$($verifyResult.isValid))"

    $pkgExtractDir = Join-Path $extractDir $idLower
    Expand-Archive -Path $nupkgPath -DestinationPath $pkgExtractDir -Force

    $nuspecPath = Get-ChildItem -Path $pkgExtractDir -Filter '*.nuspec' | Select-Object -First 1
    $repoCommit = $null
    if ($nuspecPath) {
        [xml]$nuspec = Get-Content $nuspecPath.FullName
        $repoNode = $nuspec.package.metadata.repository
        if ($repoNode) { $repoCommit = $repoNode.commit }
    }

    $tfmDir = Join-Path $pkgExtractDir "lib/$tfm"
    $actualAssemblyPath = Join-Path $tfmDir $expectedAssemblyName
    if (-not (Test-Path $actualAssemblyPath)) {
        $found = @()
        if (Test-Path $tfmDir) { $found = (Get-ChildItem -Path $tfmDir -Filter '*.dll' -File | ForEach-Object { $_.Name }) }
        throw "$id.${PackageVersion}: expected lib/$tfm/$expectedAssemblyName, found instead: $($found -join ', ')"
    }

    $assemblyHash = Get-Sha256Hex $actualAssemblyPath

    $packageManifest += [ordered]@{
        packageId               = $id
        version                 = $PackageVersion
        source                  = "https://api.nuget.org/v3-flatcontainer/$idLower/$PackageVersion/$idLower.$PackageVersion.nupkg"
        pinnedNupkgSha256       = $expectedHash
        nupkgSha256             = $actualHash
        signed                  = $verifyResult.isSigned
        signatureType           = $verifyResult.signatureType
        signatureIntegrityAndTrustValid = $verifyResult.isValid
        nuspecRepositoryCommit  = $repoCommit
        tizenTfm                = $tfm
        assembly                = $expectedAssemblyName
        assemblySha256          = $assemblyHash
    }

    $assemblies += $actualAssemblyPath
}

$toolDir = Join-Path $RepoRoot 'eng/tools/ApiDump'
Write-Host "Building ApiDump tool"
dotnet build $toolDir -c Release -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for ApiDump" }

$outDir = Join-Path $RepoRoot 'eng/api-baselines/net9.0-tizen7.0'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Running ApiDump against $($assemblies.Count) assemblies"
& dotnet run --no-build -c Release --project $toolDir -- @assemblies --out $outDir
if ($LASTEXITCODE -ne 0) { throw "ApiDump tool failed" }

# Record output hashes too, not just inputs: a future run that silently changes ApiDump's output
# format (without changing any input) should be visible in a diff of this manifest.
foreach ($pkg in $packageManifest) {
    $dumpPath = Join-Path $outDir ([System.IO.Path]::GetFileNameWithoutExtension($pkg.assembly) + '.json')
    if (Test-Path $dumpPath) {
        $pkg.outputSha256 = Get-Sha256Hex $dumpPath
    }
}

$manifest = [ordered]@{
    schemaVersion   = 1
    packageVersion  = $PackageVersion
    targetFramework = $tfm
    trustAnchor     = 'eng/api-baselines/net9.0-tizen7.0-package-trust.json'
    packages        = $packageManifest
}
$manifestPath = Join-Path $outDir 'manifest.json'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "Regenerated $outDir (+ manifest.json)"
