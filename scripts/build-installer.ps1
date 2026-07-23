[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$InstalledLauncher,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RepositoryUrl,
    [string]$IsccPath
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

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$launcherPath = (Resolve-Path -LiteralPath $InstalledLauncher).Path
if ((Split-Path -Leaf $launcherPath) -cne 'usage-indicator.exe') {
    throw 'Installed launcher must be named usage-indicator.exe.'
}

if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'UsageIndicatorForCodex.Gui.exe') -PathType Leaf)) {
    throw 'Publish directory is missing UsageIndicatorForCodex.Gui.exe.'
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
    "/DProductVersion=$($metadata.Version)",
    "/DInstallerBaseName=$installerBaseName",
    "/DRepositoryUrl=$($metadata.RepositoryUrl)",
    "/O$outputRoot",
    $installerScript
)

& $IsccPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputRoot $metadata.InstallerAssetName
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer build did not produce $($metadata.InstallerAssetName)."
}

Get-Item -LiteralPath $installerPath
