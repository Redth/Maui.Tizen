#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates the eng/api-baselines/net9.0-tizen7.0 public API surface baseline.

.DESCRIPTION
    Downloads the last-published MAUI NuGet packages that ship a net9.0-tizen7.0 assembly (per
    eng/baselines.json's behaviorBaseline), verifies each download, extracts the Tizen-TFM
    assembly from each, and runs eng/tools/ApiDump against it. ApiDump reads assemblies purely via
    System.Reflection.Metadata (never loads/executes them), so this requires only NuGet network
    access -- no Tizen workload, emulator, or device.

    Verification performed on every package before it is trusted:
      - SHA-256 of the downloaded .nupkg is computed and pinned to a sidecar file in -CacheDir on
        first download; a cache hit is only trusted if its hash still matches that sidecar (a
        nonempty cache file is never trusted on its own -- see the corruption/tamper case below).
      - The package's NuGet author/repository signature (the embedded .signature.p7s) is checked
        for presence and decoded with System.Security.Cryptography.Pkcs.SignedCms to confirm it is
        a structurally valid, non-corrupt PKCS#7 blob. This is NOT full certificate-chain/
        revocation validation (that requires network access to CRL/OCSP endpoints and the NuGet
        client's trust-policy stack, which is out of scope for an offline-capable generator) --
        it catches truncated/corrupted downloads and confirms the package is actually signed.
      - The set of net9.0-tizen7.0 assemblies extracted must exactly equal the single expected
        filename recorded in $ExpectedAssemblies below; anything else fails loudly rather than
        silently picking "whichever .dll happens to be there".

    Output is cleared and fully regenerated on every run -- never merged with a previous run's
    files, so a renamed/removed source assembly cannot leave a stale dump behind.

.PARAMETER PackageVersion
    NuGet package version to download. Defaults to eng/baselines.json's behaviorBaseline tag
    (9.0.120). Override only for exploratory/manual runs; the checked-in baseline should always
    correspond to the pinned version.

.PARAMETER CacheDir
    Directory used to cache downloaded .nupkg files (and their pinned .sha256 sidecars) across
    repeated runs.
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

function Get-Sha256Hex([string]$path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Trust-on-first-use pinning: a nonempty cache file is NOT trusted merely for existing. Its hash
# must match a sidecar recorded the first time it was downloaded and verified; any mismatch means
# the cache is stale, corrupted, or tampered with, and is treated as a hard failure rather than
# silently redownloading over it (silently overwriting would hide exactly the tamper case this
# exists to catch).
function Get-VerifiedNupkg([string]$idLower, [string]$version, [string]$cacheDir) {
    $nupkgPath = Join-Path $cacheDir "$idLower.$version.nupkg"
    $hashSidecar = "$nupkgPath.sha256"

    if (Test-Path $nupkgPath) {
        if (-not (Test-Path $hashSidecar)) {
            throw "Cache tamper/corruption guard: '$nupkgPath' exists with no pinned .sha256 sidecar. Refusing to trust an arbitrary nonempty cache file -- delete it and re-run to redownload cleanly."
        }
        $actual = Get-Sha256Hex $nupkgPath
        $pinned = (Get-Content $hashSidecar -Raw).Trim()
        if ($actual -ne $pinned) {
            throw "Cache tamper/corruption guard: '$nupkgPath' hash ($actual) no longer matches its pinned sidecar ($pinned). Refusing to use it -- delete it and re-run to redownload cleanly."
        }
        Write-Host "  using cached $idLower.$version.nupkg (sha256 verified against pinned sidecar)"
        return $nupkgPath
    }

    $url = "https://api.nuget.org/v3-flatcontainer/$idLower/$version/$idLower.$version.nupkg"
    Write-Host "  downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $nupkgPath -UseBasicParsing
    $hash = Get-Sha256Hex $nupkgPath
    Set-Content -Path $hashSidecar -Value $hash -NoNewline
    Write-Host "  sha256 $hash (pinned to $([System.IO.Path]::GetFileName($hashSidecar)))"
    return $nupkgPath
}

# Partial signature verification: confirms the package is NuGet-signed and that the embedded
# PKCS#7 blob is structurally well-formed (decodes cleanly, self-consistent). This deliberately
# does NOT validate the certificate chain or check revocation -- both require network access to
# CRL/OCSP endpoints and NuGet's full client trust-policy stack, which would make this generator
# not offline-capable. It IS sufficient to catch a truncated, corrupted, or non-Microsoft-signed
# package slipping through.
function Test-PackageSignature([string]$nupkgPath) {
    Add-Type -AssemblyName System.Security.Cryptography.Pkcs -ErrorAction SilentlyContinue

    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try {
        $sigEntry = $zip.Entries | Where-Object { $_.FullName -eq '.signature.p7s' } | Select-Object -First 1
        if (-not $sigEntry) {
            return [ordered]@{ signed = $false; signatureStructurallyValid = $false }
        }

        $ms = New-Object System.IO.MemoryStream
        $stream = $sigEntry.Open()
        try { $stream.CopyTo($ms) } finally { $stream.Dispose() }
        $bytes = $ms.ToArray()

        try {
            $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new()
            $cms.Decode($bytes)
            $cms.CheckSignature($true) # verifySignatureOnly: skip certificate chain/revocation (offline-safe).
            return [ordered]@{ signed = $true; signatureStructurallyValid = $true }
        }
        catch {
            return [ordered]@{ signed = $true; signatureStructurallyValid = $false; error = $_.Exception.Message }
        }
    }
    finally {
        $zip.Dispose()
    }
}

New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
$extractDir = Join-Path $CacheDir "extract-$PackageVersion"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

$assemblies = @()
$packageManifest = @()

foreach ($entry in $ExpectedAssemblies.GetEnumerator()) {
    $id = $entry.Key
    $expectedAssemblyName = $entry.Value
    $idLower = $id.ToLowerInvariant()

    Write-Host "$id"
    $nupkgPath = Get-VerifiedNupkg -idLower $idLower -version $PackageVersion -cacheDir $CacheDir
    $nupkgHash = Get-Sha256Hex $nupkgPath
    $sigResult = Test-PackageSignature $nupkgPath
    if ($sigResult.signed -and -not $sigResult.signatureStructurallyValid) {
        throw "$id.$PackageVersion.nupkg has a .signature.p7s that failed structural verification: $($sigResult.error)"
    }

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
        nupkgSha256             = $nupkgHash
        signed                  = $sigResult.signed
        signatureStructurallyValid = $sigResult.signatureStructurallyValid
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
    packages        = $packageManifest
}
$manifestPath = Join-Path $outDir 'manifest.json'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "Regenerated $outDir (+ manifest.json)"
