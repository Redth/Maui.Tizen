#!/usr/bin/env pwsh
<#
.SYNOPSIS
    CI entrypoint: validates the migration tooling and its checked-in generated artifacts.

.DESCRIPTION
    Runs entirely offline (no network, no Tizen workload):
      1. Builds eng/tools/ApiDump and eng/tools/SourceInventory (compile-correctness check).
      2. Runs tests/Migration.Tooling.Tests, which schema-validates
         eng/manifests/source-disposition.json, cross-checks eng/api-baselines/** manifests
         against eng/baselines.json, and re-hashes every checked-in PublicAPI.*.txt file against
         its recorded SHA-256 -- catching a generated artifact that was hand-edited (or a
         baselines.json ref bump) without being regenerated.

    This does NOT re-download sources/packages or re-run the generators against the network; for
    that (to actually refresh the checked-in artifacts), use:
      eng/scripts/generate-source-inventory.ps1
      eng/scripts/generate-api-baseline.ps1
      eng/scripts/fetch-net11-publicapi-inputs.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

Write-Host "==> Building eng/tools/ApiDump"
dotnet build (Join-Path $RepoRoot 'eng/tools/ApiDump') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "ApiDump build failed" }

Write-Host "==> Building eng/tools/SourceInventory"
dotnet build (Join-Path $RepoRoot 'eng/tools/SourceInventory') -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "SourceInventory build failed" }

Write-Host "==> Running tests/Migration.Tooling.Tests"
dotnet test (Join-Path $RepoRoot 'tests/Migration.Tooling.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw "Migration.Tooling.Tests failed" }

Write-Host "All migration tooling checks passed."
