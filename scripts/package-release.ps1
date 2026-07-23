[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
$expectedArchiveName = 'usage-indicator-for-codex-win-x64.zip'
$requiredFiles = @(
    'UsageIndicatorForCodex.exe',
    'coreclr.dll',
    'hostfxr.dll',
    'hostpolicy.dll'
)
$prohibitedPathComponents = @('.git', 'src', 'tests', 'bin', 'obj')
$prohibitedSourceExtensions = @(
    '.cs',
    '.csproj',
    '.sln',
    '.slnx',
    '.xaml',
    '.props',
    '.targets'
)

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Publish directory does not exist: $PublishDirectory"
}

$archiveParent = Split-Path -Parent $ArchivePath
if ([string]::IsNullOrWhiteSpace($archiveParent)) {
    $archiveParent = (Get-Location).Path
}

if (-not (Test-Path -LiteralPath $archiveParent -PathType Container)) {
    New-Item -ItemType Directory -Path $archiveParent | Out-Null
}

$resolvedArchiveParent = (Resolve-Path -LiteralPath $archiveParent).Path
$resolvedArchive = Join-Path $resolvedArchiveParent (Split-Path -Leaf $ArchivePath)
if ((Split-Path -Leaf $resolvedArchive) -cne $expectedArchiveName) {
    throw "Release archive must be named $expectedArchiveName."
}

if ($resolvedArchive.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release archive must be outside the publish directory.'
}

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile) -PathType Leaf)) {
        throw "Expected self-contained runtime file is missing: $requiredFile"
    }
}

$publishedFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File -Force)
if ($publishedFiles.Count -eq 0) {
    throw 'Publish directory contains no distributable files.'
}

foreach ($file in $publishedFiles) {
    $relativePath = $file.FullName.Substring($publishRoot.Length).TrimStart('\', '/')
    $components = $relativePath -split '[\\/]'
    if ($components | Where-Object { $_ -in $prohibitedPathComponents }) {
        throw "Release publish output contains a prohibited path: $relativePath"
    }

    if ($file.Extension -ieq '.pdb') {
        throw "Release publish output contains a PDB: $relativePath"
    }

    if ($file.Extension -in $prohibitedSourceExtensions) {
        throw "Release publish output contains source/build metadata: $relativePath"
    }

    if ($file.Name -match '\.Tests(\.|$)') {
        throw "Release publish output contains a test artifact: $relativePath"
    }
}

if (Test-Path -LiteralPath $resolvedArchive) {
    Remove-Item -LiteralPath $resolvedArchive -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $publishRoot,
    $resolvedArchive,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$archive = [IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
    $archiveEntryNames = @($archive.Entries | ForEach-Object FullName)
    foreach ($requiredFile in $requiredFiles) {
        if ($requiredFile -cnotin $archiveEntryNames) {
            throw "Release archive is missing required root file: $requiredFile"
        }
    }

    foreach ($entry in $archive.Entries) {
        $components = $entry.FullName -split '[\\/]'
        if ($components | Where-Object { $_ -in $prohibitedPathComponents }) {
            throw "Release archive contains a prohibited path: $($entry.FullName)"
        }

        $extension = [IO.Path]::GetExtension($entry.FullName)
        if ($extension -ieq '.pdb' -or $extension -in $prohibitedSourceExtensions) {
            throw "Release archive contains a prohibited file: $($entry.FullName)"
        }

        if ([IO.Path]::GetFileName($entry.FullName) -match '\.Tests(\.|$)') {
            throw "Release archive contains a test artifact: $($entry.FullName)"
        }
    }

    $entryCount = $archive.Entries.Count
}
finally {
    $archive.Dispose()
}

$archiveItem = Get-Item -LiteralPath $resolvedArchive
[pscustomobject]@{
    ArchivePath = $archiveItem.FullName
    EntryCount = $entryCount
    Bytes = $archiveItem.Length
}
