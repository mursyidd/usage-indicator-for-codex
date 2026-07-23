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
    $metadata.InstallerChecksumAssetName,
    $metadata.PortableAssetName,
    $metadata.PortableChecksumAssetName
)
$actualFiles = @(Get-ChildItem -LiteralPath $assetRoot -File)
$actualNames = @($actualFiles | ForEach-Object Name)
if (-not [Linq.Enumerable]::SequenceEqual(
    [string[]]($expectedNames | Sort-Object),
    [string[]]($actualNames | Sort-Object),
    [StringComparer]::Ordinal)) {
    throw "Release assets must be exactly: $($expectedNames -join ', '). Found: $($actualNames -join ', ')."
}

foreach ($assetName in @($metadata.InstallerAssetName, $metadata.PortableAssetName)) {
    $assetPath = Join-Path $assetRoot $assetName
    $checksumPath = "$assetPath.sha256"
    $checksumLines = @(
        (Get-Content -LiteralPath $checksumPath -Raw).TrimStart([char]0xFEFF) -split '\r?\n' |
            Where-Object { $_ -ne '' }
    )
    if ($checksumLines.Count -ne 1 -or
        $checksumLines[0] -cnotmatch '^([0-9a-f]{64})  (.+)$' -or
        $Matches[2] -cne $assetName) {
        throw "Checksum file must contain one exact lowercase SHA-256 record for $assetName."
    }

    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Matches[1] -cne $actualHash) {
        throw "Checksum mismatch for $assetName."
    }
}

Write-Output 'PASS exact four-asset release and SHA-256 contract'
