#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates a release API baseline from a complete Maui.Tizen package set.

.DESCRIPTION
    This is the release-baseline path for the standalone package identities. The historical
    net9.0-tizen7.0 Microsoft.Maui baseline is an upstream behavioral reference and is
    intentionally rejected by the release verifier.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesDirectory,

    [Parameter(Mandatory)]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [string]$ReleaseManifest,

    [string]$OutputDirectory,

    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$PackagesDirectory = (Resolve-Path $PackagesDirectory).Path
$ReleaseManifest = (Resolve-Path $ReleaseManifest).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $RepoRoot "eng/api-baselines/$PackageVersion"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$BaselineRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'eng/api-baselines'))
$PathComparison = if ($IsWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
if (-not $OutputDirectory.StartsWith(
    $BaselineRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
    $PathComparison)) {
    throw "OutputDirectory must be a child of '$BaselineRoot'."
}
if (Test-Path $OutputDirectory) {
    $resolvedOutput = (Resolve-Path $OutputDirectory).Path
    if (-not $resolvedOutput.StartsWith(
        $BaselineRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
        $PathComparison)) {
        throw "Existing OutputDirectory resolves outside '$BaselineRoot'."
    }
}

& (Join-Path $RepoRoot 'eng/release/release-contract.py') validate-version --version $PackageVersion
if ($LASTEXITCODE -ne 0) { throw "Invalid release baseline version '$PackageVersion'." }

$expectedIds = Get-ChildItem (Join-Path $RepoRoot 'eng/validation/package-contents') -Filter '*.contract.txt' |
    ForEach-Object { $_.BaseName -replace '\.contract$', '' } |
    Sort-Object
$packages = Get-ChildItem $PackagesDirectory -Filter '*.nupkg' |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
    Sort-Object Name

if (-not $packages) {
    throw "No .nupkg files were found in '$PackagesDirectory'."
}
$reviewedManifest = Get-Content $ReleaseManifest -Raw | ConvertFrom-Json
if ($reviewedManifest.version -ne $PackageVersion) {
    throw "Release manifest version '$($reviewedManifest.version)' does not match '$PackageVersion'."
}
if ([string]$reviewedManifest.source.commit -notmatch '^[0-9a-f]{40}$') {
    throw 'Release manifest has no valid source commit.'
}
$reviewedPackages = @{}
foreach ($entry in $reviewedManifest.packages) {
    $packageFile = @($entry.files | Where-Object { $_.kind -eq 'package' })
    if ($packageFile.Count -ne 1 -or $entry.version -ne $PackageVersion) {
        throw "Release manifest entry '$($entry.id)' is incomplete."
    }
    if ($reviewedPackages.ContainsKey($entry.id)) {
        throw "Release manifest repeats package '$($entry.id)'."
    }
    $reviewedPackages[$entry.id] = $packageFile[0]
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "maui-tizen-release-api-$([guid]::NewGuid().ToString('N'))"
$staging = "$OutputDirectory.staging-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    $seenIds = @{}
    $assemblies = @()
    $packageManifest = @()
    $msbuildManifest = @()

    foreach ($package in $packages) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
            if ($nuspecEntries.Count -ne 1) {
                throw "'$($package.Name)' does not contain exactly one root nuspec."
            }
            $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
            try {
                [xml]$nuspec = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }

            $id = [string]$nuspec.package.metadata.id
            $version = [string]$nuspec.package.metadata.version
            if ($id -notin $expectedIds) {
                throw "Unexpected package '$id' in release baseline input."
            }
            if ($version -ne $PackageVersion) {
                throw "Package '$id' has version '$version', expected '$PackageVersion'."
            }
            if ($seenIds.ContainsKey($id)) {
                throw "Release baseline input repeats package '$id'."
            }
            $reviewed = $reviewedPackages[$id]
            if (-not $reviewed) {
                throw "Package '$id' is absent from the reviewed release manifest."
            }
            $packageHash = (Get-FileHash $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $reviewedHash = ([string]$reviewed.sha256).ToLowerInvariant()
            if (
                $reviewed.filename -ne $package.Name -or
                $reviewedHash -notmatch '^[0-9a-f]{64}$' -or
                $reviewedHash -ne $packageHash
            ) {
                throw "Package '$id' does not match the reviewed release manifest bytes."
            }
            $seenIds[$id] = $true

            foreach ($entry in $archive.Entries) {
                $normalized = $entry.FullName.Replace('\', '/')
                if ($normalized -match '^lib/[^/]+/[^/]+\.dll$') {
                    $assemblyName = [System.IO.Path]::GetFileName($normalized)
                    $assemblyPath = Join-Path $temporary "$id/$assemblyName"
                    New-Item -ItemType Directory -Path (Split-Path $assemblyPath -Parent) -Force | Out-Null
                    $input = $entry.Open()
                    $output = [System.IO.File]::Create($assemblyPath)
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                        $input.Dispose()
                    }
                    $assemblies += $assemblyPath
                    $packageManifest += [ordered]@{
                        packageId       = $id
                        version         = $version
                        nupkgSha256     = $packageHash
                        assembly        = $assemblyName
                        assemblySha256  = (Get-FileHash $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                }
                elseif ($normalized -match '^(build|buildMultiTargeting|buildTransitive)/.+\.(props|targets)$') {
                    if ($normalized.Split('/') -contains '..') {
                        throw "MSBuild package path escapes its package: '$normalized'."
                    }
                    $baselineFile = "msbuild/$id/$normalized"
                    $destination = Join-Path $staging $baselineFile
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    $input = $entry.Open()
                    $output = [System.IO.File]::Create($destination)
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                        $input.Dispose()
                    }
                    $msbuildManifest += [ordered]@{
                        packageId    = $id
                        packagePath  = $normalized
                        baselineFile = $baselineFile
                        sha256       = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    $missing = @($expectedIds | Where-Object { -not $seenIds.ContainsKey($_) })
    if ($missing) {
        throw "Release baseline input is missing package(s): $($missing -join ', ')."
    }
    $unexpectedManifestIds = @($reviewedPackages.Keys | Where-Object { $_ -notin $expectedIds })
    if ($unexpectedManifestIds) {
        throw "Release manifest contains unexpected package(s): $($unexpectedManifestIds -join ', ')."
    }
    if (-not $assemblies) {
        throw 'Release baseline input contains no lib/*/*.dll assemblies.'
    }
    $assemblyNames = @($packageManifest | ForEach-Object { $_.assembly })
    if (($assemblyNames | Sort-Object -Unique).Count -ne $assemblyNames.Count) {
        throw 'Release baseline package assemblies do not have unique filenames.'
    }

    $tool = Join-Path $RepoRoot 'eng/tools/ApiDump/ApiDump.csproj'
    & $DotNet build $tool -c Release
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed for ApiDump.' }
    & $DotNet run --no-build -c Release --project $tool -- @assemblies --out $staging
    if ($LASTEXITCODE -ne 0) { throw 'ApiDump failed.' }

    foreach ($entry in $packageManifest) {
        $dump = Join-Path $staging ([System.IO.Path]::GetFileNameWithoutExtension($entry.assembly) + '.json')
        if (-not (Test-Path $dump)) {
            throw "ApiDump did not produce '$dump'."
        }
        $entry.outputSha256 = (Get-FileHash $dump -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    $baselines = Get-Content (Join-Path $RepoRoot 'eng/baselines.json') -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        schemaVersion     = 1
        baselineKind      = 'standalone-release'
        dumpSchemaVersion = 2
        packageVersion    = $PackageVersion
        targetFramework   = $baselines.target.targetFramework
        sourceCommit      = $reviewedManifest.source.commit
        sourceManifestSha256 = (Get-FileHash $ReleaseManifest -Algorithm SHA256).Hash.ToLowerInvariant()
        packages          = @($packageManifest | Sort-Object packageId, assembly)
        msbuildFiles      = @($msbuildManifest | Sort-Object packageId, packagePath)
    }
    $manifest | ConvertTo-Json -Depth 10 |
        Set-Content (Join-Path $staging 'manifest.json') -Encoding utf8

    $backup = "$OutputDirectory.backup-$([guid]::NewGuid().ToString('N'))"
    if (Test-Path $OutputDirectory) {
        Move-Item -LiteralPath $OutputDirectory -Destination $backup
    }
    try {
        Move-Item -LiteralPath $staging -Destination $OutputDirectory
    }
    catch {
        if (Test-Path $backup) {
            Move-Item -LiteralPath $backup -Destination $OutputDirectory
        }
        throw
    }
    if (Test-Path $backup) {
        Remove-Item $backup -Recurse -Force
    }

    Write-Host "Generated standalone release API baseline: $OutputDirectory"
}
finally {
    if (Test-Path $temporary) {
        Remove-Item $temporary -Recurse -Force
    }
    if (Test-Path $staging) {
        Remove-Item $staging -Recurse -Force
    }
}
