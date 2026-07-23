[CmdletBinding()]
param(
    [switch]$RequirePreservedLocalFiles
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$gitignorePath = Join-Path $repositoryRoot '.gitignore'
$metadataScript = Join-Path $repositoryRoot 'scripts\product-metadata.ps1'

$requiredSection = @(
    '# Local configuration and state',
    '.env',
    '.env.*',
    '!.env.example',
    '*.local',
    '*.local.json',
    'appsettings.*.local.json',
    'secrets.json',
    'settings.local.json',
    '.agents/',
    '/.ai-bridge/',
    '/graphify-out/',
    '/docs/superpowers/',
    'runtime/'
)

$gitignoreLines = @(Get-Content -LiteralPath $gitignorePath)
$sectionStarts = @(for ($index = 0; $index -lt $gitignoreLines.Count; $index++) {
    if ($gitignoreLines[$index] -ceq $requiredSection[0]) {
        $index
    }
})
if ($sectionStarts.Count -ne 1) {
    throw 'The required local configuration and state section must exist exactly once.'
}

$actualSection = @($gitignoreLines[$sectionStarts[0]..($sectionStarts[0] + $requiredSection.Count - 1)])
if (-not [Linq.Enumerable]::SequenceEqual(
    [string[]]$requiredSection,
    [string[]]$actualSection,
    [StringComparer]::Ordinal)) {
    throw "The local configuration and state section differs from the required contract: $($actualSection -join ', ')"
}

& git -C $repositoryRoot check-ignore --no-index --quiet -- .env.example
if ($LASTEXITCODE -eq 0) {
    throw '.env.example must remain trackable.'
}

foreach ($ignoredPath in @(
    '.env',
    '.env.production',
    'sample.local',
    'sample.local.json',
    'appsettings.Development.local.json',
    'secrets.json',
    'settings.local.json',
    '.agents/probe',
    '.ai-bridge/probe',
    'graphify-out/probe',
    'docs/superpowers/probe',
    'runtime/probe'
)) {
    & git -C $repositoryRoot check-ignore --no-index --quiet -- $ignoredPath
    if ($LASTEXITCODE -ne 0) {
        throw "Expected ignored path is trackable: $ignoredPath"
    }
}

$trackedSuperpowers = @(& git -C $repositoryRoot ls-files -- docs/superpowers)
if ($trackedSuperpowers.Count -ne 0) {
    throw "docs/superpowers must be removed from the Git index: $($trackedSuperpowers -join ', ')"
}

$preservedFiles = @(
    'docs\superpowers\plans\2026-07-23-minimize-restore-lifecycle.md',
    'docs\superpowers\plans\2026-07-23-powershell-console-race.md',
    'docs\superpowers\plans\2026-07-23-usage-indicator-for-codex-public-release.md',
    'docs\superpowers\specs\2026-07-23-minimize-restore-lifecycle-design.md',
    'docs\superpowers\specs\2026-07-23-powershell-console-race-design.md',
    'docs\superpowers\specs\2026-07-23-usage-indicator-for-codex-public-release-design.md'
)
$stagedSuperpowersDeletions = @(
    & git -C $repositoryRoot diff --cached --name-only --diff-filter=D -- docs/superpowers
)
if ($RequirePreservedLocalFiles -or $stagedSuperpowersDeletions.Count -gt 0) {
    foreach ($relativePath in $preservedFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf)) {
            throw "Index-removed local file was not preserved: $relativePath"
        }
    }
}

. $metadataScript
$metadata = Get-UsageIndicatorProductMetadata -RepositoryRoot $repositoryRoot
if ($metadata.Version -cne '0.1.0') {
    throw "Unexpected product version: $($metadata.Version)"
}

$expectedAssets = @(
    'UsageIndicatorForCodex-Setup-v0.1.0.exe',
    'UsageIndicatorForCodex-Setup-v0.1.0.exe.sha256',
    'usage-indicator-for-codex-v0.1.0-win-x64.zip',
    'usage-indicator-for-codex-v0.1.0-win-x64.zip.sha256'
)
$actualAssets = @(
    $metadata.InstallerAssetName,
    $metadata.InstallerChecksumAssetName,
    $metadata.PortableAssetName,
    $metadata.PortableChecksumAssetName
)
if (-not [Linq.Enumerable]::SequenceEqual(
    [string[]]$expectedAssets,
    [string[]]$actualAssets,
    [StringComparer]::Ordinal)) {
    throw "Versioned asset names do not match the release contract: $($actualAssets -join ', ')"
}

$normalizedSshRepository = ConvertTo-UsageIndicatorRepositoryUrl 'git@github.com:owner/project.git'
if ($normalizedSshRepository -cne 'https://github.com/owner/project') {
    throw "SSH repository URL was not normalized safely: $normalizedSshRepository"
}
try {
    ConvertTo-UsageIndicatorRepositoryUrl 'https://example.invalid/owner/project' | Out-Null
    throw 'A non-GitHub repository URL was accepted.'
}
catch {
    if ($_.Exception.Message -ceq 'A non-GitHub repository URL was accepted.') {
        throw
    }
}

$commandSource = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'src\UsageIndicatorForCodex\CommandLineOptions.cs') -Raw
$readme = Get-Content -LiteralPath (Join-Path $repositoryRoot 'README.md') -Raw
$requiredCommands = @(
    'start',
    'stop',
    'status',
    'version',
    'check-update',
    'update',
    'enable-startup',
    'disable-startup',
    'help'
)
foreach ($command in $requiredCommands) {
    if ($commandSource.IndexOf("""$command""", [StringComparison]::Ordinal) -lt 0) {
        throw "Managed command parser is missing installed verb: $command"
    }
    if ($readme.IndexOf("usage-indicator $command", [StringComparison]::Ordinal) -lt 0) {
        throw "README is missing installed verb: usage-indicator $command"
    }
}

$appServerSource = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'src\UsageIndicatorForCodex\Services\CodexAppServerUsageProvider.cs') -Raw
if ($appServerSource.IndexOf('version = "1.0.0"', [StringComparison]::Ordinal) -ge 0 -or
    $appServerSource.IndexOf('version = ProductInfo.Version', [StringComparison]::Ordinal) -lt 0) {
    throw 'Codex app-server clientInfo.version is not authoritative.'
}

foreach ($documentPath in @('README.md', 'SECURITY.md')) {
    $document = Get-Content -LiteralPath (Join-Path $repositoryRoot $documentPath) -Raw
    foreach ($assetName in $expectedAssets) {
        if ($document.IndexOf($assetName, [StringComparison]::Ordinal) -lt 0) {
            throw "$documentPath is missing release asset $assetName."
        }
    }
}

$releaseWorkflow = Get-Content -LiteralPath (
    Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
foreach ($fragment in @(
    '$env:GITHUB_REF_NAME -cne $metadata.Tag',
    '${{ github.server_url }}/${{ github.repository }}',
    '.\tests\release-contract.ps1',
    'gh release create $env:GITHUB_REF_NAME @assets --verify-tag'
)) {
    if ($releaseWorkflow.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Release workflow is missing version/asset invariant: $fragment"
    }
}

Write-Output 'PASS repository ignore, local preservation, version, commands, documentation, and release contract'
