[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata
$assetRoot = (Resolve-Path -LiteralPath $AssetDirectory).Path
$expectedNames = @(
    $metadata.InstallerAssetName,
    $metadata.InstallerChecksumAssetName
)
$actualFiles = @(Get-ChildItem -LiteralPath $assetRoot -File)
$actualNames = @($actualFiles | ForEach-Object Name)
if (-not [Linq.Enumerable]::SequenceEqual(
    [string[]]($expectedNames | Sort-Object),
    [string[]]($actualNames | Sort-Object),
    [StringComparer]::Ordinal)) {
    throw "Release assets must be exactly: $($expectedNames -join ', '). Found: $($actualNames -join ', ')."
}

$assetPath = Join-Path $assetRoot $metadata.InstallerAssetName
$checksumPath = Join-Path $assetRoot $metadata.InstallerChecksumAssetName
$checksumLines = @(
    (Get-Content -LiteralPath $checksumPath -Raw).TrimStart([char]0xFEFF) -split '\r?\n' |
        Where-Object { $_ -ne '' }
)
if ($checksumLines.Count -ne 1 -or
    $checksumLines[0] -cnotmatch '^([0-9a-f]{64})  (.+)$' -or
    $Matches[2] -cne $metadata.InstallerAssetName) {
    throw "Checksum file must contain one exact lowercase SHA-256 record for $($metadata.InstallerAssetName)."
}

$actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Matches[1] -cne $actualHash) {
    throw "Checksum mismatch for $($metadata.InstallerAssetName)."
}

Write-Output 'PASS exact two-asset installer and SHA-256 contract'
