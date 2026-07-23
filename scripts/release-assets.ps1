[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$PortableArchivePath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata
$installer = Get-Item -LiteralPath $InstallerPath
$portable = Get-Item -LiteralPath $PortableArchivePath
if ($installer.Name -cne $metadata.InstallerAssetName) {
    throw "Installer must be named $($metadata.InstallerAssetName)."
}
if ($portable.Name -cne $metadata.PortableAssetName) {
    throw "Portable archive must be named $($metadata.PortableAssetName)."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path

function Copy-ReleaseAsset {
    param(
        [Parameter(Mandatory)][IO.FileInfo]$Source,
        [Parameter(Mandatory)][string]$DestinationRoot
    )

    $destination = Join-Path $DestinationRoot $Source.Name
    if (-not [string]::Equals(
        $Source.FullName,
        $destination,
        [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $Source.FullName -Destination $destination -Force
    }
    return Get-Item -LiteralPath $destination
}

function Write-Sha256Asset {
    param(
        [Parameter(Mandatory)][IO.FileInfo]$Asset,
        [Parameter(Mandatory)][string]$ChecksumName
    )

    $checksumPath = Join-Path $Asset.DirectoryName $ChecksumName
    $hash = (Get-FileHash -LiteralPath $Asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $checksumPath,
        "$hash  $($Asset.Name)`n",
        [Text.UTF8Encoding]::new($false))
    return Get-Item -LiteralPath $checksumPath
}

$outputInstaller = Copy-ReleaseAsset $installer $outputRoot
$outputPortable = Copy-ReleaseAsset $portable $outputRoot
$installerChecksum = Write-Sha256Asset `
    $outputInstaller `
    $metadata.InstallerChecksumAssetName
$portableChecksum = Write-Sha256Asset `
    $outputPortable `
    $metadata.PortableChecksumAssetName

@(
    $outputInstaller,
    $installerChecksum,
    $outputPortable,
    $portableChecksum
)
