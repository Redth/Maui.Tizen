#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates the eng/api-baselines/net9.0-tizen7.0 public API surface baseline.

.DESCRIPTION
    Downloads the last-published MAUI NuGet packages that ship a net9.0-tizen7.0 assembly (per
    eng/baselines.json's behaviorBaseline), extracts the Tizen-TFM assembly from each, and runs
    eng/tools/ApiDump against it. ApiDump reads assemblies purely via System.Reflection.Metadata
    (never loads/executes them), so this requires only NuGet network access -- no Tizen workload,
    emulator, or device.

.PARAMETER PackageVersion
    NuGet package version to download. Defaults to eng/baselines.json's behaviorBaseline tag
    (9.0.120). Override only for exploratory/manual runs; the checked-in baseline should always
    correspond to the pinned version.

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

# Every Microsoft.Maui.* package that ships a net9.0-tizen7.0 asset at this version.
# Microsoft.Maui.Controls (the meta-package) and Microsoft.Maui.Controls.Build.Tasks /
# Microsoft.Maui.Resizetizer (MSBuild task packages, no TFM-specific assembly) are intentionally
# excluded -- there is no Tizen-specific managed assembly to dump for them.
$packageIds = @(
    'Microsoft.Maui.Core',
    'Microsoft.Maui.Essentials',
    'Microsoft.Maui.Graphics',
    'Microsoft.Maui.Controls.Core',
    'Microsoft.Maui.Controls.Xaml',
    'Microsoft.Maui.Controls.Compatibility',
    'Microsoft.Maui.Controls.Maps'
)
$tfm = 'net9.0-tizen7.0'

New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
$extractDir = Join-Path $CacheDir "extract-$PackageVersion"
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

$assemblies = @()
$packageManifest = @()

foreach ($id in $packageIds) {
    $idLower = $id.ToLowerInvariant()
    $nupkgPath = Join-Path $CacheDir "$idLower.$PackageVersion.nupkg"
    if (-not (Test-Path $nupkgPath)) {
        $url = "https://api.nuget.org/v3-flatcontainer/$idLower/$PackageVersion/$idLower.$PackageVersion.nupkg"
        Write-Host "Downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $nupkgPath -UseBasicParsing
    }

    $pkgExtractDir = Join-Path $extractDir $idLower
    if (Test-Path $pkgExtractDir) { Remove-Item $pkgExtractDir -Recurse -Force }
    Expand-Archive -Path $nupkgPath -DestinationPath $pkgExtractDir -Force

    $nuspecPath = Get-ChildItem -Path $pkgExtractDir -Filter '*.nuspec' | Select-Object -First 1
    $repoCommit = $null
    if ($nuspecPath) {
        [xml]$nuspec = Get-Content $nuspecPath.FullName
        $repoNode = $nuspec.package.metadata.repository
        if ($repoNode) { $repoCommit = $repoNode.commit }
    }

    $tfmDir = Join-Path $pkgExtractDir "lib/$tfm"
    $dlls = @()
    if (Test-Path $tfmDir) {
        $dlls = Get-ChildItem -Path $tfmDir -Filter '*.dll' -File | Where-Object { $_.DirectoryName -eq (Resolve-Path $tfmDir).Path }
    }

    $packageManifest += [ordered]@{
        packageId        = $id
        version           = $PackageVersion
        tizenTfm          = $tfm
        hasTizenAssembly  = $dlls.Count -gt 0
        assemblies        = @($dlls | ForEach-Object { $_.Name })
        nuspecRepositoryCommit = $repoCommit
        source            = "https://api.nuget.org/v3-flatcontainer/$idLower/$PackageVersion/$idLower.$PackageVersion.nupkg"
    }

    if ($dlls.Count -eq 0) {
        Write-Host "  $id : no $tfm assembly published at $PackageVersion (skipped)"
        continue
    }

    $assemblies += $dlls.FullName
}

$toolDir = Join-Path $RepoRoot 'eng/tools/ApiDump'
Write-Host "Building ApiDump tool"
dotnet build $toolDir -c Release -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for ApiDump" }

$outDir = Join-Path $RepoRoot 'eng/api-baselines/net9.0-tizen7.0'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Running ApiDump against $($assemblies.Count) assemblies"
& dotnet run --no-build -c Release --project $toolDir -- @assemblies --out $outDir
if ($LASTEXITCODE -ne 0) { throw "ApiDump tool failed" }

$manifest = [ordered]@{
    schemaVersion   = 1
    generatedAtUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    packageVersion  = $PackageVersion
    targetFramework = $tfm
    packages        = $packageManifest
}
$manifestPath = Join-Path $outDir 'manifest.json'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "Regenerated $outDir (+ manifest.json)"
