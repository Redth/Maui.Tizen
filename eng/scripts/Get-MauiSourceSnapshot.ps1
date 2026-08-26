#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Downloads a deterministic source snapshot of dotnet/maui at a specific commit SHA.

.DESCRIPTION
    Fetches the GitHub codeload tarball for a single commit (no git history, no .git directory)
    and extracts it to -OutDir. This is intentionally NOT a git clone: a full or even a
    blob-filtered clone of dotnet/maui is extremely slow to check out file-by-file, whereas the
    codeload tarball is a single flat download of the tree at that exact commit.

    Requires only network access to github.com -- no Tizen workload, no git.

.PARAMETER Repo
    GitHub "owner/name" of the source repository. Defaults to dotnet/maui.

.PARAMETER Ref
    Commit SHA (or tag) to snapshot. Must be a full commit SHA for reproducible results; tags are
    accepted for the published-baseline case (e.g. 9.0.120) where the tag itself is immutable.

.PARAMETER OutDir
    Directory to extract the snapshot into. Created if missing. If it already contains files and
    -Force is not specified, the download is skipped (idempotent re-runs).

.EXAMPLE
    ./Get-MauiSourceSnapshot.ps1 -Ref ee4d06cde6b49e297631b08426a33fb34f3152ef -OutDir /tmp/maui-net11
#>
[CmdletBinding()]
param(
    [string]$Repo = 'dotnet/maui',
    [Parameter(Mandatory = $true)][string]$Ref,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ((Test-Path $OutDir) -and -not $Force) {
    $existing = Get-ChildItem -Path $OutDir -Force -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Skipping download: '$OutDir' already exists and is non-empty (use -Force to re-download)."
        return
    }
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$tarballPath = Join-Path ([System.IO.Path]::GetTempPath()) "maui-$Ref.tar.gz"

$url = "https://codeload.github.com/$Repo/tar.gz/$Ref"
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $tarballPath -UseBasicParsing

Write-Host "Extracting to $OutDir"
tar -xzf $tarballPath -C $OutDir --strip-components=1
if ($LASTEXITCODE -ne 0) {
    throw "tar extraction failed with exit code $LASTEXITCODE"
}

Remove-Item $tarballPath -Force
Write-Host "Snapshot of $Repo@$Ref ready at $OutDir"
