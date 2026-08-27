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

Test-Case 'Repartitioning content across a filename containing an embedded newline does not collide (length-prefixed encoding)' {
    # A plain "path\n<content>" concatenation is not strictly injective for a filename that
    # legally contains an embedded newline byte (POSIX allows this): two different (path,
    # content) splits of the exact same underlying bytes could in principle hash identically.
    # Get-SnapshotTreeHash length-prefixes each path and content instead of relying on a
    # delimiter, so this constructs the adversarial case directly: one tree with a single file
    # whose name contains a literal newline, and a differently-partitioned tree with two files,
    # such that a delimiter-based (non-length-prefixed) scheme would concatenate to the same
    # byte stream. The hashes must differ.
    $dirA = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    $dirB = Join-Path ([System.IO.Path]::GetTempPath()) "mt-snap-test-$([System.Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $dirA -Force | Out-Null
        # Single file whose name is "a`nb" (contains a literal newline) and whose content is "c".
        $nameWithNewline = "a`nb"
        Set-Content -Path (Join-Path $dirA $nameWithNewline) -Value 'c' -NoNewline

        New-Item -ItemType Directory -Path $dirB -Force | Out-Null
        # Two files, "a" (content "b") and "b" (content "c") -- chosen so that a delimiter-based
        # "path\ncontent" scheme would produce the identical byte stream "a\nb" + "b\nc" split
        # differently than dirA's "a\nb\nc", i.e. exactly the ambiguity a length-prefixed scheme
        # must NOT be fooled by.
        Set-Content -Path (Join-Path $dirB 'a') -Value 'b' -NoNewline
        Set-Content -Path (Join-Path $dirB 'b') -Value 'c' -NoNewline

        $hashA = Get-SnapshotTreeHash -Dir $dirA
        $hashB = Get-SnapshotTreeHash -Dir $dirB
        Assert-True ($hashA.treeHash -ne $hashB.treeHash) "expected different (path, content) partitions to produce different tree hashes even when their naive concatenation would coincide"
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
