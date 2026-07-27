[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$InstalledLauncher,
    [Parameter(Mandatory)][string]$UpdateHostPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RepositoryUrl,
    [string]$IsccPath,
    [string]$IntegrationTestInstallerStateSubKey,
    [string]$IntegrationTestAppId
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'product-metadata.ps1')
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$metadata = Get-UsageIndicatorProductMetadata `
    -RepositoryRoot $repositoryRoot `
    -RepositoryUrl $RepositoryUrl
if ([string]::IsNullOrWhiteSpace($metadata.RepositoryUrl)) {
    throw 'RepositoryUrl is required because this checkout has no usable origin remote.'
}
if ([string]::IsNullOrWhiteSpace($IntegrationTestInstallerStateSubKey) -ne
    [string]::IsNullOrWhiteSpace($IntegrationTestAppId)) {
    throw 'Both isolated installer integration properties must be supplied together.'
}
if (-not [string]::IsNullOrWhiteSpace($IntegrationTestInstallerStateSubKey)) {
    if (-not $IntegrationTestInstallerStateSubKey.StartsWith(
        'Software\UsageIndicatorForCodex\IntegrationTests\',
        [StringComparison]::Ordinal)) {
        throw 'The integration installer state key is outside the isolated test namespace.'
    }
    if ($IntegrationTestAppId -cnotmatch '^\{\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$') {
        throw 'The integration installer AppId must be a double-open-brace uppercase GUID.'
    }
}

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$launcherPath = (Resolve-Path -LiteralPath $InstalledLauncher).Path
$resolvedUpdateHostPath = (Resolve-Path -LiteralPath $UpdateHostPath).Path
$repositoryLicensePath = (Resolve-Path -LiteralPath (
    Join-Path $repositoryRoot 'LICENSE')).Path
if ((Split-Path -Leaf $launcherPath) -cne 'usage-indicator.exe') {
    throw 'Installed launcher must be named usage-indicator.exe.'
}
if ((Get-Item -LiteralPath $launcherPath).VersionInfo.ProductVersion.Trim() -cne $metadata.Version) {
    throw "Installed launcher product version must be $($metadata.Version)."
}
if ((Split-Path -Leaf $resolvedUpdateHostPath) -cne 'UsageIndicatorForCodex.UpdateHost.exe') {
    throw 'UpdateHost must be named UsageIndicatorForCodex.UpdateHost.exe.'
}
if ((Get-Item -LiteralPath $resolvedUpdateHostPath).VersionInfo.ProductVersion.Trim() -cne $metadata.Version) {
    throw "UpdateHost product version must be $($metadata.Version)."
}

if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'UsageIndicatorForCodex.Gui.exe') -PathType Leaf)) {
    throw 'Publish directory is missing UsageIndicatorForCodex.Gui.exe.'
}
if ((Get-Item -LiteralPath (
    Join-Path $publishRoot 'UsageIndicatorForCodex.Gui.exe')).VersionInfo.ProductVersion.Trim() -cne $metadata.Version) {
    throw "GUI product version must be $($metadata.Version)."
}
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'LICENSE.txt') -PathType Leaf)) {
    throw 'Publish directory is missing LICENSE.txt.'
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -ne $isccCommand) {
        $IsccPath = $isccCommand.Source
    } else {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        )
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $IsccPath = $candidate
                break
            }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or
    -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup 6 compiler ISCC.exe could not be found.'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$installerBaseName = [IO.Path]::GetFileNameWithoutExtension($metadata.InstallerAssetName)
$installerScript = Join-Path $repositoryRoot 'installer\UsageIndicatorForCodex.iss'
$arguments = @(
    '/Qp',
    "/DPublishDirectory=$publishRoot",
    "/DInstalledLauncher=$launcherPath",
    "/DUpdateHostPath=$resolvedUpdateHostPath",
    "/DProductVersion=$($metadata.Version)",
    "/DInstallerBaseName=$installerBaseName",
    "/DRepositoryUrl=$($metadata.RepositoryUrl)",
    "/DRepositoryLicensePath=$repositoryLicensePath",
    "/O$outputRoot",
    $installerScript
)
if (-not [string]::IsNullOrWhiteSpace($IntegrationTestInstallerStateSubKey)) {
    $arguments = @(
        $arguments[0..($arguments.Count - 2)]
        "/DInstallerStateSubKey=$IntegrationTestInstallerStateSubKey"
        "/DInstallerAppId=$IntegrationTestAppId"
        $arguments[-1]
    )
}

& $IsccPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputRoot $metadata.InstallerAssetName
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer build did not produce $($metadata.InstallerAssetName)."
}

Get-Item -LiteralPath $installerPath
