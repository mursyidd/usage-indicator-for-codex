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
foreach ($portableProperty in @('PortableAssetName', 'PortableChecksumAssetName')) {
    if ($metadata.PSObject.Properties.Name -ccontains $portableProperty) {
        throw "Product metadata still exposes a portable asset property: $portableProperty"
    }
}
if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'scripts\package-release.ps1')) {
    throw 'The obsolete portable package-release.ps1 script must not exist.'
}

$expectedAssets = @(
    "UsageIndicatorForCodex-Setup-v$($metadata.Version).exe",
    "UsageIndicatorForCodex-Setup-v$($metadata.Version).exe.sha256"
)
$actualAssets = @(
    $metadata.InstallerAssetName,
    $metadata.InstallerChecksumAssetName
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
$requiredReadmeOrder = @(
    '## Installation',
    '## Quick start',
    '## Installed layout',
    '## Commands'
)
$previousReadmeSectionIndex = -1
foreach ($section in $requiredReadmeOrder) {
    $sectionIndex = $readme.IndexOf($section, [StringComparison]::Ordinal)
    if ($sectionIndex -lt 0 -or $sectionIndex -le $previousReadmeSectionIndex) {
        throw "README first-run sections are missing or out of order: $($requiredReadmeOrder -join ', ')"
    }
    $previousReadmeSectionIndex = $sectionIndex
}

$displayAndControlsIndex = $readme.IndexOf('## Display and controls', [StringComparison]::Ordinal)
$troubleshootingIndex = $readme.IndexOf('## Troubleshooting', [StringComparison]::Ordinal)
$uninstallIndex = $readme.IndexOf('## Uninstall', [StringComparison]::Ordinal)
if ($troubleshootingIndex -lt 0 -or
    $troubleshootingIndex -le $displayAndControlsIndex -or
    $troubleshootingIndex -ge $uninstallIndex) {
    throw 'README Troubleshooting must follow Display and controls and precede Uninstall.'
}

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

foreach ($unsupportedPublicArgument in @(
    '--exit',
    '--install',
    '--uninstall',
    '--toggle',
    '--revalidate-cli',
    '--help',
    '-h'
)) {
    if ($commandSource.IndexOf(
        """$unsupportedPublicArgument""",
        [StringComparison]::Ordinal) -ge 0) {
        throw "Managed parser still supports non-public argument: $unsupportedPublicArgument"
    }
}
foreach ($forbiddenHelpFragment in @(
    'UsageIndicatorForCodex.exe',
    'Portable compatibility',
    'Portable updates'
)) {
    if ($commandSource.IndexOf($forbiddenHelpFragment, [StringComparison]::Ordinal) -ge 0) {
        throw "Managed help still advertises a non-public interface: $forbiddenHelpFragment"
    }
}

$publicContractDocuments = [ordered]@{
    'README.md' = @(
        '## Installation',
        '## Quick start',
        '## Installed layout',
        '## Commands',
        '## Status and exit codes',
        '## Start with Windows',
        '## Updates',
        '## Codex CLI configuration',
        '## Display and controls',
        '## Troubleshooting',
        '## Uninstall',
        '## Security and privacy',
        '## Development and contributing',
        '## Limitations and license',
        'internal upgrade compatibility rule',
        'foreign canonical tasks',
        'exits `2`',
        'startup: unrecognized',
        'An update is already in progress.',
        '`usage-indicator` is not recognized',
        'Usage unavailable does not mean 0% remaining.',
        'Get-TimeZone',
        'Do not include',
        '[`usage-indicator` is not recognized](#usage-indicator-is-not-recognized)',
        '[an indicator that is not visible](#the-indicator-is-not-visible)',
        '[`Usage unavailable`](#the-indicator-shows-usage-unavailable)',
        'UsageIndicatorForCodex.Gui.exe',
        'LICENSE.txt',
        'Arm64'
    )
    'SECURITY.md' = @(
        'Foreign canonical',
        'Ownership collisions return exit code `2`',
        'operational inspection failures return `1`',
        'An update is already in progress.',
        'LICENSE.txt',
        'Arm64',
        'UsageIndicatorForCodex-Setup-v<version>.exe',
        'UsageIndicatorForCodex-Setup-v<version>.exe.sha256',
        'exactly two public assets'
    )
    'CONTRIBUTING.md' = @(
        'UsageIndicatorForCodex.Gui.exe --background',
        'UsageIndicatorForCodex.exe --background',
        'internal upgrade compatibility',
        'Ownership collisions exit `2`',
        'operational inspection failures',
        'LICENSE.txt',
        'Arm64',
        'Get-UsageIndicatorProductMetadata',
        '$installerPath = Join-Path $release $metadata.InstallerAssetName',
        '-InstallerPath $installerPath'
    )
    '2026-07-23-usage-indicator-for-codex-design.md' = @(
        'Distribution and Command Architecture',
        'UsageIndicatorForCodex.exe --background',
        'internal upgrade compatibility',
        'Ownership collisions exit `2`',
        'An update is already in progress.',
        'LICENSE.txt',
        'Arm64'
    )
}
foreach ($documentEntry in $publicContractDocuments.GetEnumerator()) {
    $document = Get-Content -LiteralPath (
        Join-Path $repositoryRoot $documentEntry.Key) -Raw
    foreach ($fragment in $documentEntry.Value) {
        if ($document.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
            throw "$($documentEntry.Key) is missing public contract text: $fragment"
        }
    }
}

foreach ($documentPath in $publicContractDocuments.Keys) {
    $document = Get-Content -LiteralPath (Join-Path $repositoryRoot $documentPath) -Raw
    foreach ($forbiddenPublicFragment in @(
        'UsageIndicatorForCodex.exe --install',
        'UsageIndicatorForCodex.exe --uninstall',
        'UsageIndicatorForCodex.exe --toggle',
        'UsageIndicatorForCodex.exe --revalidate-cli',
        'UsageIndicatorForCodex.exe --exit',
        'UsageIndicatorForCodex.exe --help',
        'UsageIndicatorForCodex.exe -h',
        'usage-indicator-for-codex-v0.1.0-win-x64.zip',
        'portable distribution',
        'portable release',
        'portable update'
    )) {
        if ($document.IndexOf(
            $forbiddenPublicFragment,
            [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "$documentPath still advertises removed public behavior: $forbiddenPublicFragment"
        }
    }

    if ($document.IndexOf('UsageIndicatorForCodex.exe --background', [StringComparison]::Ordinal) -ge 0 -and
        -not [regex]::IsMatch($document, 'internal\s+upgrade compatibility', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        throw "$documentPath mentions the historical launcher action outside an explicitly labelled migration or cleanup context."
    }
}

$appServerSource = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'src\UsageIndicatorForCodex\Services\CodexAppServerUsageProvider.cs') -Raw
if ($appServerSource.IndexOf('version = "1.0.0"', [StringComparison]::Ordinal) -ge 0 -or
    $appServerSource.IndexOf('version = ProductInfo.Version', [StringComparison]::Ordinal) -lt 0) {
    throw 'Codex app-server clientInfo.version is not authoritative.'
}

foreach ($documentPath in @(
    'README.md',
    'CONTRIBUTING.md',
    'SECURITY.md',
    '.github\workflows\ci.yml'
)) {
    $document = Get-Content -LiteralPath (Join-Path $repositoryRoot $documentPath) -Raw
    if ($document -cmatch 'UsageIndicatorForCodex-Setup-v\d+\.\d+\.\d+\.exe') {
        throw "$documentPath contains a version-literal installer filename in an evergreen surface."
    }
}

foreach ($requiredReadmeFragment in @(
    'UsageIndicatorForCodex-Setup-v<version>.exe',
    'UsageIndicatorForCodex-Setup-v<version>.exe.sha256',
    '$installers = @(',
    "throw 'Expected exactly one Usage Indicator installer in this folder.'",
    'Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256',
    'Get-Content -LiteralPath "$($installer.FullName).sha256"'
)) {
    if ($readme.IndexOf($requiredReadmeFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "README is missing version-neutral installer guidance: $requiredReadmeFragment"
    }
}

foreach ($forbiddenReadmeFragment in @(
    'Usage unavailable means 0% remaining',
    'share your API key',
    'share your token',
    'UsageIndicatorForCodex.Gui.exe start',
    'UsageIndicatorForCodex.Gui.exe stop'
)) {
    if ($readme.IndexOf($forbiddenReadmeFragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "README contains unsafe public guidance: $forbiddenReadmeFragment"
    }
}
if ($readme -cmatch '(?im)^(?!.*\bdo not\b).*\b(?:disable|turn off)\s+(?:Microsoft Defender\s+)?SmartScreen\b') {
    throw 'README instructs users to disable SmartScreen.'
}

$releaseAssetScript = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'scripts\release-assets.ps1') -Raw
foreach ($forbiddenReleaseFragment in @(
    'PortableArchivePath',
    'PortableAssetName',
    'PortableChecksumAssetName'
)) {
    if ($releaseAssetScript.IndexOf(
        $forbiddenReleaseFragment,
        [StringComparison]::Ordinal) -ge 0) {
        throw "Release asset script still requires a portable artifact: $forbiddenReleaseFragment"
    }
}
$launcherBuildScript = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'scripts\build-launcher.ps1') -Raw
foreach ($forbiddenLauncherBuildFragment in @(
    'ValidateSet(''Portable'', ''Installed'')',
    '$Layout',
    '-Layout',
    'LAUNCHER_REJECT_UPDATE'
)) {
    if ($launcherBuildScript.IndexOf($forbiddenLauncherBuildFragment, [StringComparison]::Ordinal) -ge 0) {
        throw "Installed launcher build still has a portable layout branch: $forbiddenLauncherBuildFragment"
    }
}
foreach ($requiredLauncherBuildFragment in @(
    "'usage-indicator.exe'",
    'Launcher output filename must be usage-indicator.exe'
)) {
    if ($launcherBuildScript.IndexOf($requiredLauncherBuildFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Installed launcher build is missing required behavior: $requiredLauncherBuildFragment"
    }
}
$launcherSource = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'src\UsageIndicatorForCodex.Launcher\launcher.c') -Raw
foreach ($forbiddenLauncherSourceFragment in @(
    'LAUNCHER_REJECT_UPDATE',
    'PortableUpdateErrorMessage',
    'Portable updates are not supported'
)) {
    if ($launcherSource.IndexOf($forbiddenLauncherSourceFragment, [StringComparison]::Ordinal) -ge 0) {
        throw "Native launcher still has portable behavior: $forbiddenLauncherSourceFragment"
    }
}
foreach ($requiredLauncherSourceFragment in @(
    '..\\app\\UsageIndicatorForCodex.Gui.exe',
    'DefaultArgument[] = L"help"',
    'AsyncArgument[] = L"start"'
)) {
    if ($launcherSource.IndexOf($requiredLauncherSourceFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Native launcher is missing installed-only behavior: $requiredLauncherSourceFragment"
    }
}

$ciWorkflow = Get-Content -LiteralPath (
    Join-Path $repositoryRoot '.github\workflows\ci.yml') -Raw
$releaseWorkflow = Get-Content -LiteralPath (
    Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
$innoVerifier = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'scripts\verify-inno-setup.ps1') -Raw
if ($ciWorkflow.IndexOf('name: usage-indicator-for-codex-release-assets', [StringComparison]::Ordinal) -lt 0) {
    throw 'CI artifact label must be version-neutral.'
}
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

$expectedInnoSetupVersion = '6.7.1'
$expectedInstallCommand =
    "choco install innosetup --version=$expectedInnoSetupVersion --yes --no-progress"
$workflowContracts = [ordered]@{
    CI = $ciWorkflow
    Release = $releaseWorkflow
}
$workflowPins = [Collections.Generic.List[string]]::new()
foreach ($workflowEntry in $workflowContracts.GetEnumerator()) {
    foreach ($forbiddenWorkflowFragment in @(
        '-Layout Installed',
        '-Layout Portable',
        'PortableArchivePath',
        'PortableAssetName',
        'package-release.ps1',
        'Compress-Archive',
        '.zip'
    )) {
        if ($workflowEntry.Value.IndexOf(
            $forbiddenWorkflowFragment,
            [StringComparison]::Ordinal) -ge 0) {
            throw "$($workflowEntry.Key) still publishes or packages a portable artifact: $forbiddenWorkflowFragment"
        }
    }

    foreach ($requiredLauncherContractFragment in @(
        '- name: Validate installed launcher contract',
        '.\tests\installed-launcher-contract.ps1',
        '-InstalledLauncher (Join-Path $env:RUNNER_TEMP ''usage-indicator.exe'')',
        '-LauncherProbe .\tests\UsageIndicatorForCodex.LauncherProbe\bin\Release\net8.0\UsageIndicatorForCodex.LauncherProbe.exe'
    )) {
        if ($workflowEntry.Value.IndexOf($requiredLauncherContractFragment, [StringComparison]::Ordinal) -lt 0) {
            throw "$($workflowEntry.Key) is missing installed launcher contract coverage: $requiredLauncherContractFragment"
        }
    }

    $installCommands = @(
        [regex]::Matches(
            $workflowEntry.Value,
            '(?m)^\s*(?:run:\s*)?(choco install innosetup[^\r\n]*)$') |
            ForEach-Object { $_.Groups[1].Value.Trim() }
    )
    if ($installCommands.Count -ne 1 -or
        $installCommands[0] -cne $expectedInstallCommand) {
        throw "$($workflowEntry.Key) must contain exactly this Inno Setup install command: $expectedInstallCommand"
    }

    $pinMatch = [regex]::Match(
        $installCommands[0],
        '--version=([0-9]+\.[0-9]+\.[0-9]+)')
    if (-not $pinMatch.Success) {
        throw "$($workflowEntry.Key) uses an unpinned Inno Setup installation command."
    }
    $workflowPins.Add($pinMatch.Groups[1].Value)

    foreach ($fragment in @(
        '- name: Verify Inno Setup compiler version',
        "`$expectedInnoSetupVersion = '$expectedInnoSetupVersion'",
        '$compilerPath = Join-Path ${env:ProgramFiles(x86)} ''Inno Setup 6\ISCC.exe''',
        '$compiler = .\scripts\verify-inno-setup.ps1 `',
        '-CompilerPath $compilerPath `',
        '-ExpectedVersion $expectedInnoSetupVersion',
        '"INNO_SETUP_COMPILER=$($compiler.FullName)"',
        '-IsccPath $env:INNO_SETUP_COMPILER'
    )) {
        if ($workflowEntry.Value.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
            throw "$($workflowEntry.Key) is missing Inno Setup compiler verification: $fragment"
        }
    }

    $exportPattern =
        '(?s)"INNO_SETUP_COMPILER=\$\(\$compiler\.FullName\)"\s*\|\s*' +
        'Out-File -FilePath \$env:GITHUB_ENV -Encoding utf8 -Append'
    if (-not [regex]::IsMatch($workflowEntry.Value, $exportPattern)) {
        throw "$($workflowEntry.Key) must export the exact verified ISCC.exe path."
    }

    $installIndex = $workflowEntry.Value.IndexOf(
        $expectedInstallCommand,
        [StringComparison]::Ordinal)
    $verifyIndex = $workflowEntry.Value.IndexOf(
        '- name: Verify Inno Setup compiler version',
        [StringComparison]::Ordinal)
    $buildIndex = $workflowEntry.Value.IndexOf(
        '- name: Build and validate installer',
        [StringComparison]::Ordinal)
    if ($installIndex -lt 0 -or
        $verifyIndex -le $installIndex -or
        $buildIndex -le $verifyIndex) {
        throw "$($workflowEntry.Key) must install, verify, then use Inno Setup in that order."
    }
}
if ($workflowPins.Count -ne 2 -or
    $workflowPins[0] -cne $workflowPins[1] -or
    $workflowPins[0] -cne $expectedInnoSetupVersion) {
    throw "CI and release Inno Setup pins must both be $expectedInnoSetupVersion."
}

foreach ($fragment in @(
    'ExpectedVersion -cnotmatch ''^([0-9]+)\.([0-9]+)\.([0-9]+)$''',
    '#if Ver != EncodeVer($major,$minor,$revision)',
    '#error Unexpected Inno Setup compiler version',
    'Output=no',
    '& $resolvedCompilerPath ''/Q'' $probePath',
    '$compilerExitCode = $LASTEXITCODE',
    'if ($compilerExitCode -ne 0)',
    'throw "ISCC.exe is not version $ExpectedVersion.',
    'Get-Item -LiteralPath $resolvedCompilerPath'
)) {
    if ($innoVerifier.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Inno Setup compiler verifier is missing required behavior: $fragment"
    }
}

Write-Output 'PASS repository ignore, local preservation, version, commands, documentation, and release contract'
