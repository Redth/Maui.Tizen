param(
    [Parameter(Mandatory = $true)]
    [string] $PackagesDirectory,

    [Parameter(Mandatory = $true)]
    [string] $CertificateSha256,

    [Parameter(Mandatory = $true)]
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$approved = ($CertificateSha256 -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
if ($approved -notmatch '^[0-9A-F]{64}$') {
    throw 'CertificateSha256 must be a 64-character SHA-256 certificate fingerprint.'
}

$packages = @()
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("maui-tizen-authenticode-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    foreach ($package in Get-ChildItem -LiteralPath $PackagesDirectory -Filter '*.nupkg' | Sort-Object Name) {
        $packageRoot = Join-Path $tempRoot $package.BaseName
        [System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $packageRoot)

        $binaries = @()
        foreach ($binary in Get-ChildItem -LiteralPath $packageRoot -Recurse -Filter '*.dll' | Sort-Object FullName) {
            $relative = [System.IO.Path]::GetRelativePath($packageRoot, $binary.FullName).Replace('\', '/')
            if (($relative -split '/')[0] -notin @('lib', 'ref', 'runtimes', 'tasks', 'tools')) {
                continue
            }

            $signature = Get-AuthenticodeSignature -LiteralPath $binary.FullName
            $fingerprint = if ($null -ne $signature.SignerCertificate) {
                $signature.SignerCertificate.GetCertHashString(
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
            } else {
                ''
            }

            $binaries += [ordered]@{
                path = $relative
                sha256 = (Get-FileHash -LiteralPath $binary.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                status = $signature.Status.ToString()
                certificateSha256 = $fingerprint.ToLowerInvariant()
            }

            if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
                throw "Authenticode signature is not valid for $($package.Name):$relative ($($signature.Status))."
            }
            if ($fingerprint -ne $approved) {
                throw "Authenticode signer mismatch for $($package.Name):$relative."
            }
        }

        if ($binaries.Count -eq 0) {
            throw "Shipping package $($package.Name) contains no managed binaries to verify."
        }

        $packages += [ordered]@{
            filename = $package.Name
            sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            binaries = $binaries
        }
    }

    if ($packages.Count -eq 0) {
        throw "No .nupkg files were found in $PackagesDirectory."
    }

    $report = [ordered]@{
        schemaVersion = 1
        certificateSha256 = $approved.ToLowerInvariant()
        packages = $packages
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
