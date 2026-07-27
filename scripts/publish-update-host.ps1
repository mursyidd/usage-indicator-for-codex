[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RepositoryUrl,
    [switch]$NoRestore
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

$projectPath = Join-Path $repositoryRoot (
    'src\UsageIndicatorForCodex.UpdateHost\UsageIndicatorForCodex.UpdateHost.csproj')
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$arguments = @(
    'publish',
    $projectPath,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $outputRoot,
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:RepositoryUrl=$($metadata.RepositoryUrl)"
)
if ($NoRestore) {
    $arguments += '--no-restore'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$hostPath = Join-Path $outputRoot 'UsageIndicatorForCodex.UpdateHost.exe'
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw 'UpdateHost publish did not produce UsageIndicatorForCodex.UpdateHost.exe.'
}

$hostFile = Get-Item -LiteralPath $hostPath
if ($hostFile.VersionInfo.ProductVersion.Trim() -cne $metadata.Version) {
    throw "UpdateHost product version must be $($metadata.Version)."
}

$unexpectedRuntimeFiles = @(
    Get-ChildItem -LiteralPath $outputRoot -File |
        Where-Object {
            $_.Extension -ceq '.dll' -or
            $_.Name.EndsWith('.deps.json', [StringComparison]::Ordinal) -or
            $_.Name.EndsWith('.runtimeconfig.json', [StringComparison]::Ordinal)
        }
)
if ($unexpectedRuntimeFiles.Count -ne 0) {
    throw "UpdateHost publish is not standalone: $($unexpectedRuntimeFiles.Name -join ', ')."
}

$hostFile
