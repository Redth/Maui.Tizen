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

    Integrity/provenance:
      - Extraction is atomic: the tarball is expanded into a fresh sibling temp directory and only
        moved into place at -OutDir once fully extracted, so a killed/failed run can never leave a
        half-extracted snapshot at -OutDir for a caller to mistake for a complete one.
      - -OutDir is always fully cleared before extracting into it (whether or not -Force was
        passed to overwrite an existing snapshot) -- a snapshot directory is either absent, or a
        complete, verified extraction of exactly Repo@Ref; it is never a merge of two extractions.
      - A `.mt-snapshot.json` marker (repository, ref, file count, and a manifest hash covering
        every extracted file's relative path + content hash) is written into -OutDir after
        extraction and is what -Repo/-Ref get validated against on every subsequent call,
        including by callers that pass an existing -OutDir without -Force: if the marker is
        missing or does not match the requested Repo/Ref, the directory is rejected rather than
        silently treated as already-populated (this is also what generate-source-inventory.ps1 and
        fetch-net11-publicapi-inputs.ps1 check when a caller supplies -PrimaryRoot/-LegacyRoot
        directly instead of letting this script download one).

.PARAMETER Repo
    GitHub "owner/name" of the source repository. Defaults to dotnet/maui.

.PARAMETER Ref
    Commit SHA (or tag) to snapshot. Must be a full commit SHA for reproducible results; tags are
    accepted for the published-baseline case (e.g. 9.0.120) where the tag itself is immutable.

.PARAMETER OutDir
    Directory to extract the snapshot into. Created if missing. If it already contains a valid
    .mt-snapshot.json marker matching -Repo/-Ref, the download is skipped (idempotent re-runs). If
    it contains anything else (no marker, or a marker for a different repo/ref), the call fails
    unless -Force is passed, in which case the directory is cleared and re-extracted.

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

function Get-SnapshotMarkerPath([string]$dir) { Join-Path $dir '.mt-snapshot.json' }

function Test-SnapshotMarker([string]$dir, [string]$repo, [string]$ref) {
    $markerPath = Get-SnapshotMarkerPath $dir
    if (-not (Test-Path $markerPath)) { return $false }
    try {
        $marker = Get-Content $markerPath -Raw | ConvertFrom-Json
    }
    catch {
        return $false
    }
    return $marker.repository -eq $repo -and $marker.ref -eq $ref
}

function New-SnapshotMarker([string]$dir, [string]$repo, [string]$ref) {
    # Streams every file directly through one incremental SHA-256 (path + content), rather than
    # invoking Get-FileHash per file (its per-call pipeline/process overhead makes a ~30k-file
    # tree like dotnet/maui impractically slow). This still hashes every byte of every file --
    # it is just done as one continuous stream instead of thousands of separate cmdlet calls.
    $files = [System.IO.Directory]::EnumerateFiles($dir, '*', [System.IO.SearchOption]::AllDirectories) |
        Sort-Object -Culture 'invariant'

    $incremental = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $fileCount = 0
    foreach ($f in $files) {
        $rel = [System.IO.Path]::GetRelativePath($dir, $f).Replace('\', '/')
        $incremental.AppendData([System.Text.Encoding]::UTF8.GetBytes("$rel`n"))
        $stream = [System.IO.File]::OpenRead($f)
        try {
            $buffer = New-Object byte[] 1048576
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                if ($read -eq $buffer.Length) {
                    $incremental.AppendData($buffer)
                }
                else {
                    $incremental.AppendData($buffer, 0, $read)
                }
            }
        }
        finally {
            $stream.Dispose()
        }
        $fileCount++
    }
    $treeHashBytes = $incremental.GetHashAndReset()
    $treeHash = [System.BitConverter]::ToString($treeHashBytes).Replace('-', '').ToLowerInvariant()

    $marker = [ordered]@{
        repository = $repo
        ref        = $ref
        fileCount  = $fileCount
        treeHash   = $treeHash
    }
    $marker | ConvertTo-Json | Set-Content -Path (Get-SnapshotMarkerPath $dir) -Encoding utf8
    return $marker
}

if (Test-Path $OutDir) {
    if (Test-SnapshotMarker -dir $OutDir -repo $Repo -ref $Ref) {
        Write-Host "Skipping download: '$OutDir' already contains a verified snapshot of $Repo@$Ref."
        return
    }

    if (-not $Force) {
        $markerPath = Get-SnapshotMarkerPath $OutDir
        if (Test-Path $markerPath) {
            $marker = Get-Content $markerPath -Raw | ConvertFrom-Json
            throw "'$OutDir' contains a snapshot of $($marker.repository)@$($marker.ref), not the requested $Repo@$Ref. Refusing to reuse a mismatched snapshot -- pass -Force to overwrite, or point at a different -OutDir."
        }
        throw "'$OutDir' exists but has no .mt-snapshot.json provenance marker, so it cannot be verified as $Repo@$Ref. Refusing to trust an arbitrary directory -- pass -Force to overwrite, or point at a different -OutDir."
    }

    Write-Host "Clearing '$OutDir' (-Force)"
    Remove-Item -Path $OutDir -Recurse -Force
}

$outParent = Split-Path -Parent $OutDir
if ($outParent) {
    New-Item -ItemType Directory -Path $outParent -Force | Out-Null
}
# NOTE: -OutDir itself is deliberately NOT created here. Move-Item's destination-exists behavior
# is to move the source *inside* an existing directory rather than rename onto it, which would
# silently nest the extracted tree one level deeper than every path in this repo's tooling
# expects. Only the parent is ensured; -OutDir is created by the Move-Item below.

$tarballPath = Join-Path ([System.IO.Path]::GetTempPath()) "maui-$Ref-$([System.Guid]::NewGuid().ToString('N')).tar.gz"
$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "maui-$Ref-$([System.Guid]::NewGuid().ToString('N'))"

try {
    $url = "https://codeload.github.com/$Repo/tar.gz/$Ref"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $tarballPath -UseBasicParsing

    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
    Write-Host "Extracting to staging directory"
    tar -xzf $tarballPath -C $stagingDir --strip-components=1
    if ($LASTEXITCODE -ne 0) {
        throw "tar extraction failed with exit code $LASTEXITCODE"
    }

    # Atomic hand-off: only now, with a fully-extracted staging directory in hand, do we touch
    # -OutDir. A failure anywhere above leaves -OutDir exactly as it was (absent, in this branch).
    # -OutDir must not exist yet (see the note above) -- Move-Item then renames staging -> OutDir
    # directly instead of nesting it one level deeper.
    Move-Item -Path $stagingDir -Destination $OutDir
    $stagingDir = $null # moved; nothing left to clean up.

    $marker = New-SnapshotMarker -dir $OutDir -repo $Repo -ref $Ref
    Write-Host "Snapshot of $Repo@$Ref ready at $OutDir ($($marker.fileCount) files, treeHash $($marker.treeHash.Substring(0,12))...)"
}
finally {
    if (Test-Path $tarballPath) { Remove-Item $tarballPath -Force -ErrorAction SilentlyContinue }
    if ($stagingDir -and (Test-Path $stagingDir)) { Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
}
