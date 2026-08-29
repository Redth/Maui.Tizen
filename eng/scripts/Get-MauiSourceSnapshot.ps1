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

    Integrity/provenance (see eng/scripts/lib/Snapshot.ps1 for the shared implementation):
      - Extraction is atomic: the tarball is expanded into a fresh sibling temp directory and only
        moved into place at -OutDir once fully extracted, so a killed/failed run can never leave a
        half-extracted snapshot at -OutDir for a caller to mistake for a complete one.
      - -OutDir is always fully cleared before extracting into it (whether or not -Force was
        passed to overwrite an existing snapshot) -- a snapshot directory is either absent, or a
        complete, verified extraction of exactly Repo@Ref; it is never a merge of two extractions.
      - A `.mt-snapshot.json` marker (repository, ref, file count, and a tree hash covering every
        extracted file's relative path + content) is written into -OutDir after extraction.
      - On every reuse -- including a cache hit here, AND every caller elsewhere in this
        repository's tooling that accepts a caller-supplied snapshot directory instead of
        downloading its own -- the tree hash is RECOMPUTED from the files currently on disk and
        compared against the marker. A marker that merely claims the right repository/ref is not
        enough: if a file was added, removed, or modified after the marker was written (a partial
        re-extraction, a stray edit, tampering), the recomputed hash will not match, and the
        directory is rejected exactly as if it had no marker at all.

.PARAMETER Repo
    GitHub "owner/name" of the source repository. Defaults to dotnet/maui.

.PARAMETER Ref
    Commit SHA (or tag) to snapshot. Must be a full commit SHA for reproducible results; tags are
    accepted for the published-baseline case (e.g. 9.0.120) where the tag itself is immutable.

.PARAMETER OutDir
    Directory to extract the snapshot into. Created if missing. If it already contains a snapshot
    that recomputes to match -Repo/-Ref exactly, the download is skipped (idempotent re-runs). If
    it contains anything else (no marker, a marker for a different repo/ref, or a marker whose
    claims no longer match the files on disk), the call fails unless -Force is passed, in which
    case the directory is cleared and re-extracted.

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
. (Join-Path $PSScriptRoot 'lib/Snapshot.ps1')

if (Test-Path $OutDir) {
    $verification = Test-SnapshotIntegrity -Dir $OutDir -Repo $Repo -Ref $Ref
    if ($verification.ok) {
        Write-Host "Skipping download: '$OutDir' already contains a verified snapshot of $Repo@$Ref (recomputed tree hash matches)."
        return
    }

    if (-not $Force) {
        switch ($verification.reason) {
            'wrong-repo-or-ref' {
                throw "'$OutDir' contains a snapshot of $($verification.marker.repository)@$($verification.marker.ref), not the requested $Repo@$Ref. Refusing to reuse a mismatched snapshot -- pass -Force to overwrite, or point at a different -OutDir."
            }
            'tree-modified' {
                throw "'$OutDir' claims to be a snapshot of $Repo@$Ref, but its recomputed tree hash ($($verification.computed.treeHash)) does not match its marker ($($verification.marker.treeHash)) -- a file was added, removed, or modified since it was extracted. Refusing to reuse a directory whose contents no longer match its own provenance record -- pass -Force to re-extract, or point at a different -OutDir."
            }
            default {
                throw "'$OutDir' exists but has no valid .mt-snapshot.json provenance marker, so it cannot be verified as $Repo@$Ref. Refusing to trust an arbitrary directory -- pass -Force to overwrite, or point at a different -OutDir."
            }
        }
    }

    Write-Host "Clearing '$OutDir' (-Force, reason: $($verification.reason))"
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

    $marker = New-SnapshotMarker -Dir $OutDir -Repo $Repo -Ref $Ref
    Write-Host "Snapshot of $Repo@$Ref ready at $OutDir ($($marker.fileCount) files, treeHash $($marker.treeHash.Substring(0,12))...)"
}
finally {
    if (Test-Path $tarballPath) { Remove-Item $tarballPath -Force -ErrorAction SilentlyContinue }
    if ($stagingDir -and (Test-Path $stagingDir)) { Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
}
