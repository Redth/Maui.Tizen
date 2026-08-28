#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Offline regression tests for eng/scripts/lib/Snapshot.ps1's provenance verification.

.DESCRIPTION
    A ".mt-snapshot.json" marker is only useful if every consumer actually recomputes and compares
    a snapshot's tree hash rather than trusting the marker's stored claims. These tests exercise
    the shared Test-SnapshotIntegrity/New-SnapshotMarker functions directly against small synthetic
    directories -- no network access, no real dotnet/maui checkout -- covering exactly the tamper
    scenarios a marker-only check (repository/ref match with no recompute) would silently accept:
    a file added, a file deleted, and a file's content modified after the marker was written.

    Exit code is non-zero if any check fails.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '../..')).Path
. (Join-Path $RepoRoot 'eng/scripts/lib/Snapshot.ps1')

$FAILURES = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        Write-Host "  PASS $Name" -ForegroundColor Green
    }
    catch {
        Write-Host "  FAIL $Name" -ForegroundColor Red
        Write-Host "       $($_.Exception.Message)" -ForegroundColor Red
        $script:FAILURES++
    }
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-NaiveSnapshotEncoding([string]$dir) {
    $stream = [System.IO.MemoryStream]::new()
    try {
        $files = [System.Collections.Generic.SortedDictionary[string, string]]::new(
            [System.StringComparer]::Ordinal)
        foreach ($file in [System.IO.Directory]::EnumerateFiles(
                $dir, '*', [System.IO.SearchOption]::AllDirectories)) {
            $relativePath = [System.IO.Path]::GetRelativePath($dir, $file).Replace('\', '/')
            $files.Add($relativePath, $file)
        }

        foreach ($entry in $files.GetEnumerator()) {
            $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($entry.Key)
            $stream.Write($pathBytes, 0, $pathBytes.Length)
            $stream.WriteByte(10)
            $contentBytes = [System.IO.File]::ReadAllBytes($entry.Value)
            $stream.Write($contentBytes, 0, $contentBytes.Length)
        }

        return [System.Convert]::ToBase64String($stream.ToArray())
    }
    finally {
        $stream.Dispose()
    }
}

function New-SyntheticSnapshot([string]$dir) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $dir 'src/Sub') -Force | Out-Null
    Set-Content -Path (Join-Path $dir 'README.md') -Value 'hello' -NoNewline
    Set-Content -Path (Join-Path $dir 'src/Program.cs') -Value 'class Program {}' -NoNewline
    Set-Content -Path (Join-Path $dir 'src/Sub/Nested.cs') -Value 'class Nested {}' -NoNewline
}

Write-Host "Snapshot verification tests"

Test-Case 'A fresh snapshot verifies against its own marker' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dir
        New-SnapshotMarker -Dir $dir -Repo 'example/repo' -Ref 'abc123' | Out-Null
        $result = Test-SnapshotIntegrity -Dir $dir -Repo 'example/repo' -Ref 'abc123'
        Assert-True $result.ok "expected ok=true, got reason=$($result.reason)"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'A missing marker is rejected' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dir
        $result = Test-SnapshotIntegrity -Dir $dir -Repo 'example/repo' -Ref 'abc123'
        Assert-True (-not $result.ok) "expected ok=false for a directory with no marker"
        Assert-True ($result.reason -eq 'no-marker') "expected reason=no-marker, got $($result.reason)"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'A marker for the wrong repo/ref is rejected' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dir
        New-SnapshotMarker -Dir $dir -Repo 'example/repo' -Ref 'abc123' | Out-Null
        $result = Test-SnapshotIntegrity -Dir $dir -Repo 'example/repo' -Ref 'different-ref'
        Assert-True (-not $result.ok) "expected ok=false for a ref mismatch"
        Assert-True ($result.reason -eq 'wrong-repo-or-ref') "expected reason=wrong-repo-or-ref, got $($result.reason)"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'A file MODIFIED after the marker was written is rejected (tamper)' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dir
        New-SnapshotMarker -Dir $dir -Repo 'example/repo' -Ref 'abc123' | Out-Null

        # Simulate tampering: change a file's content without touching the marker.
        Set-Content -Path (Join-Path $dir 'src/Program.cs') -Value 'class Program { /* tampered */ }' -NoNewline

        $result = Test-SnapshotIntegrity -Dir $dir -Repo 'example/repo' -Ref 'abc123'
        Assert-True (-not $result.ok) "expected ok=false after modifying a file's content"
        Assert-True ($result.reason -eq 'tree-modified') "expected reason=tree-modified, got $($result.reason)"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'A file ADDED after the marker was written is rejected (tamper)' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dir
        New-SnapshotMarker -Dir $dir -Repo 'example/repo' -Ref 'abc123' | Out-Null

        Set-Content -Path (Join-Path $dir 'src/Extra.cs') -Value 'class Extra {}' -NoNewline

        $result = Test-SnapshotIntegrity -Dir $dir -Repo 'example/repo' -Ref 'abc123'
        Assert-True (-not $result.ok) "expected ok=false after adding a file"
        Assert-True ($result.reason -eq 'tree-modified') "expected reason=tree-modified, got $($result.reason)"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'A file DELETED after the marker was written is rejected (tamper)' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dir
        New-SnapshotMarker -Dir $dir -Repo 'example/repo' -Ref 'abc123' | Out-Null

        Remove-Item (Join-Path $dir 'src/Sub/Nested.cs') -Force

        $result = Test-SnapshotIntegrity -Dir $dir -Repo 'example/repo' -Ref 'abc123'
        Assert-True (-not $result.ok) "expected ok=false after deleting a file"
        Assert-True ($result.reason -eq 'tree-modified') "expected reason=tree-modified, got $($result.reason)"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'Two independent snapshots of identical content produce the same tree hash (determinism)' {
    $dirA = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    $dirB = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-SyntheticSnapshot $dirA
        New-SyntheticSnapshot $dirB
        $hashA = Get-SnapshotTreeHash -Dir $dirA
        $hashB = Get-SnapshotTreeHash -Dir $dirB
        Assert-True ($hashA.treeHash -eq $hashB.treeHash) "expected identical content to produce identical tree hashes"
        Assert-True ($hashA.fileCount -eq $hashB.fileCount) "expected identical file counts"
    }
    finally {
        Remove-Item $dirA -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $dirB -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'Canonical length prefixes use explicit big-endian byte order' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'a') -Value 'b' -NoNewline

        $hash = Get-SnapshotTreeHash -Dir $dir
        Assert-True ($hash.treeHash -eq '3c9d591045bc8876f9d0399bbfb05c6a412096e906f73278f98406cd5dca86df') "expected the canonical big-endian hash vector"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'Canonical paths are sorted with ordinal comparison' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'a') -Value 'y' -NoNewline
        Set-Content -LiteralPath (Join-Path $dir 'B') -Value 'x' -NoNewline

        $hash = Get-SnapshotTreeHash -Dir $dir
        Assert-True ($hash.treeHash -eq 'f62ebaccd1a567edf71c6f059813dbbe50e7c65e74556cc1ad113d07482c3c86') "expected ordinal path ordering with 'B' before 'a'"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'A literal POSIX backslash does not alias a directory separator' {
    if ($IsWindows) {
        Write-Host '       SKIP Windows does not permit a literal backslash in a path segment'
        return
    }

    $dirA = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    $dirB = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $dirA -Force | Out-Null
        $literalBackslashPath = $dirA + [System.IO.Path]::DirectorySeparatorChar + 'a\b'
        [System.IO.File]::WriteAllText($literalBackslashPath, 'content')

        New-Item -ItemType Directory -Path (Join-Path $dirB 'a') -Force | Out-Null
        Set-Content -LiteralPath ([System.IO.Path]::Combine($dirB, 'a', 'b')) -Value 'content' -NoNewline

        $hashA = Get-SnapshotTreeHash -Dir $dirA
        $hashB = Get-SnapshotTreeHash -Dir $dirB
        Assert-True ($hashA.treeHash -ne $hashB.treeHash) "expected literal backslash and directory separator paths to hash differently"
    }
    finally {
        Remove-Item $dirA -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $dirB -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'Marker exclusion is ordinal and case-sensitive when the filesystem permits distinct names' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir '.mt-snapshot.json') -Value 'marker' -NoNewline
        Set-Content -LiteralPath (Join-Path $dir '.MT-SNAPSHOT.JSON') -Value 'payload' -NoNewline

        $names = @([System.IO.Directory]::EnumerateFiles($dir) | ForEach-Object {
                [System.IO.Path]::GetFileName($_)
            })
        if (@($names | Where-Object { $_ -ceq '.mt-snapshot.json' }).Count -ne 1 -or
            @($names | Where-Object { $_ -ceq '.MT-SNAPSHOT.JSON' }).Count -ne 1) {
            Write-Host '       SKIP filesystem does not permit these names as distinct files'
            return
        }

        $hash = Get-SnapshotTreeHash -Dir $dir
        Assert-True ($hash.fileCount -eq 1) "expected only the exact lowercase marker path to be excluded"
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'Equal naive streams from a newline filename do not collide after length-prefixing' {
    if ($IsWindows) {
        Write-Host '       SKIP Windows does not permit a literal newline in a filename'
        return
    }

    $dirA = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    $dirB = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $dirA -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dirA 'a') -Value "b`nc" -NoNewline

        New-Item -ItemType Directory -Path $dirB -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dirB "a`nb") -Value 'c' -NoNewline

        $oldEncodingA = Get-NaiveSnapshotEncoding $dirA
        $oldEncodingB = Get-NaiveSnapshotEncoding $dirB
        Assert-True ($oldEncodingA -eq $oldEncodingB) "fixture error: expected the old path-newline-content encodings to be identical"

        $hashA = Get-SnapshotTreeHash -Dir $dirA
        $hashB = Get-SnapshotTreeHash -Dir $dirB
        Assert-True ($hashA.fileCount -eq $hashB.fileCount) "fixture error: expected equal file counts"
        Assert-True ($hashA.treeHash -ne $hashB.treeHash) "expected length-prefixed encodings to distinguish equal naive streams"
    }
    finally {
        Remove-Item $dirA -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $dirB -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
if ($FAILURES -gt 0) {
    Write-Host "$FAILURES check(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host "All snapshot verification checks passed" -ForegroundColor Green
exit 0
