#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Shared source-snapshot provenance verification, used by Get-MauiSourceSnapshot.ps1 and every
    script that consumes a snapshot it produced.

.DESCRIPTION
    A ".mt-snapshot.json" marker records the repository, ref, file count, and a streamed SHA-256
    hash over every file's relative path + content ("tree hash") for a downloaded source snapshot.

    Critically, verifying a snapshot means RECOMPUTING that tree hash from the files currently on
    disk and comparing it to what the marker claims -- not just reading the marker and trusting it.
    A marker is just a JSON file sitting next to the snapshot; nothing stops a file under the
    snapshot directory from being added, deleted, or modified after the marker was written (a
    partial re-extraction, a stray edit, disk corruption, or deliberate tampering). Checking only
    "does a marker exist with the right repository/ref" would accept any of those silently. Every
    caller in this repository's tooling -- whether it just extracted a fresh snapshot, is reusing
    a cached one, or received a directory path from a human -- runs this same recompute-and-compare
    check before trusting the directory's contents.

    Dot-source this file to get Test-SnapshotIntegrity, New-SnapshotMarker, and
    Get-SnapshotMarkerPath:
        . (Join-Path $PSScriptRoot 'lib/Snapshot.ps1')
#>

function Get-SnapshotMarkerPath {
    param([Parameter(Mandatory = $true)][string]$Dir)
    Join-Path $Dir '.mt-snapshot.json'
}

function Get-SnapshotCanonicalRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Dir,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $relativePath = [System.IO.Path]::GetRelativePath($Dir, $Path)
    $directorySeparator = [System.IO.Path]::DirectorySeparatorChar
    $alternateSeparator = [System.IO.Path]::AltDirectorySeparatorChar

    if ($directorySeparator -eq $alternateSeparator) {
        return $relativePath
    }

    $segments = $relativePath.Split(
        [char[]]@($directorySeparator, $alternateSeparator),
        [System.StringSplitOptions]::None)
    return [string]::Join('/', $segments)
}

function Add-SnapshotLengthPrefix {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.IncrementalHash]$Hash,
        [Parameter(Mandatory = $true)]
        [int64]$Length
    )

    $bytes = [byte[]]::new(8)
    for ($index = $bytes.Length - 1; $index -ge 0; $index--) {
        $bytes[$index] = [byte]($Length -band 0xff)
        $Length = $Length -shr 8
    }
    $Hash.AppendData($bytes)
}

<#
.SYNOPSIS
    Computes the deterministic (fileCount, treeHash) pair for every file currently under -Dir.

.DESCRIPTION
    Streams every file directly through one incremental SHA-256 (relative path + content), rather
    than invoking Get-FileHash per file (its per-call pipeline/process overhead makes a ~30k-file
    tree like dotnet/maui impractically slow). This still hashes every byte of every file -- it is
    just done as one continuous stream instead of thousands of separate cmdlet calls. The marker
    file itself (.mt-snapshot.json) is excluded from the hash, since it does not exist yet on first
    write and its own content depends on the hash being computed -- including it would be circular.

    Each entry's relative path length and content length are appended as big-endian fixed-width
    prefixes before the path/content bytes themselves. Relative paths are formed from OS path
    segments joined with "/", so Windows separators are canonicalized without changing a literal
    backslash in a legal POSIX filename. A plain "path\n<content>path2\n<content2>..."
    concatenation is not strictly injective for filenames that legally contain an embedded
    newline byte (POSIX allows this): a different (path, content) partition of the exact same byte
    stream could in principle hash identically. Length-prefixing makes the encoding of the full
    file list unambiguous regardless of what bytes appear in a path or a file's content.
#>
function Get-SnapshotTreeHash {
    param([Parameter(Mandatory = $true)][string]$Dir)

    $markerPath = [System.IO.Path]::GetFullPath((Get-SnapshotMarkerPath $Dir))
    $files = [System.Collections.Generic.SortedDictionary[string, string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($file in [System.IO.Directory]::EnumerateFiles(
            $Dir, '*', [System.IO.SearchOption]::AllDirectories)) {
        $fullPath = [System.IO.Path]::GetFullPath($file)
        if ([string]::Equals($fullPath, $markerPath, [System.StringComparison]::Ordinal)) {
            continue
        }

        $relativePath = Get-SnapshotCanonicalRelativePath -Dir $Dir -Path $fullPath
        $files.Add($relativePath, $fullPath)
    }

    $incremental = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $fileCount = 0
    foreach ($entry in $files.GetEnumerator()) {
        $relBytes = [System.Text.Encoding]::UTF8.GetBytes($entry.Key)
        Add-SnapshotLengthPrefix -Hash $incremental -Length $relBytes.Length
        $incremental.AppendData($relBytes)
        Add-SnapshotLengthPrefix -Hash $incremental -Length ([System.IO.FileInfo]::new($entry.Value).Length)
        $stream = [System.IO.File]::OpenRead($entry.Value)
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

    return [ordered]@{ fileCount = $fileCount; treeHash = $treeHash }
}

<#
.SYNOPSIS
    Writes a .mt-snapshot.json provenance marker for -Dir, computing its tree hash fresh.
#>
function New-SnapshotMarker {
    param(
        [Parameter(Mandatory = $true)][string]$Dir,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Ref
    )

    $computed = Get-SnapshotTreeHash -Dir $Dir
    $marker = [ordered]@{
        repository = $Repo
        ref        = $Ref
        fileCount  = $computed.fileCount
        treeHash   = $computed.treeHash
    }
    $marker | ConvertTo-Json | Set-Content -Path (Get-SnapshotMarkerPath $Dir) -Encoding utf8
    return $marker
}

<#
.SYNOPSIS
    Verifies that -Dir is a complete, unmodified snapshot of -Repo@-Ref.

.DESCRIPTION
    Reads the .mt-snapshot.json marker (if present) and RECOMPUTES the tree hash from the files
    currently on disk, then requires repository, ref, fileCount, AND treeHash to all match. A
    marker claiming the right repo/ref with a stale or tampered treeHash is rejected exactly like
    a missing marker -- the marker's claims are only as good as what they can be checked against.

    Returns an ordered hashtable: { ok = <bool>; reason = <string>; marker = <parsed marker or $null> }.
    Never throws for a verification failure (a missing/mismatched marker, or a tree that no longer
    matches it) -- callers decide what that means (offer -Force, hard-fail, etc.). Only throws for
    genuinely exceptional conditions (e.g. -Dir does not exist).
#>
function Test-SnapshotIntegrity {
    param(
        [Parameter(Mandatory = $true)][string]$Dir,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Ref
    )

    if (-not (Test-Path $Dir)) {
        throw "'$Dir' does not exist."
    }

    $markerPath = Get-SnapshotMarkerPath $Dir
    if (-not (Test-Path $markerPath)) {
        return [ordered]@{ ok = $false; reason = 'no-marker'; marker = $null }
    }

    try {
        $marker = Get-Content $markerPath -Raw | ConvertFrom-Json
    }
    catch {
        return [ordered]@{ ok = $false; reason = 'unreadable-marker'; marker = $null }
    }

    if ($marker.repository -ne $Repo -or $marker.ref -ne $Ref) {
        return [ordered]@{ ok = $false; reason = 'wrong-repo-or-ref'; marker = $marker }
    }

    $computed = Get-SnapshotTreeHash -Dir $Dir
    if ($computed.fileCount -ne $marker.fileCount -or $computed.treeHash -ne $marker.treeHash) {
        return [ordered]@{ ok = $false; reason = 'tree-modified'; marker = $marker; computed = $computed }
    }

    return [ordered]@{ ok = $true; reason = 'ok'; marker = $marker }
}
