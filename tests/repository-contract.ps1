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
if ($metadata.Version -cne '0.2.0') {
    throw "Unexpected product version: $($metadata.Version)"
}

$releaseNotesPath = Join-Path (
    Join-Path $repositoryRoot '.github\release-notes') "$($metadata.Tag).md"
if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
    throw "Canonical release notes file is missing: $releaseNotesPath"
}
$releaseNotes = Get-Content -LiteralPath $releaseNotesPath -Raw
if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
    throw "Canonical release notes file is empty: $releaseNotesPath"
}
$expectedReleaseComparison =
    'https://github.com/mursyidd/usage-indicator-for-codex/compare/v0.1.0...v0.2.0'
$releaseComparisons = @(
    [regex]::Matches(
        $releaseNotes,
        'https://github\.com/mursyidd/usage-indicator-for-codex/compare/v[^)\s]+') |
        ForEach-Object Value
)
if ($releaseComparisons.Count -ne 1 -or
    $releaseComparisons[0] -cne $expectedReleaseComparison) {
    throw "Canonical release notes must contain exactly this comparison: $expectedReleaseComparison"
}
if ([regex]::IsMatch(
    $releaseNotes,
    '\bpatch\s+release\b',
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
    throw 'Canonical release notes must not describe v0.2.0 as a patch release.'
}
if ([regex]::IsMatch(
    $releaseNotes,
    '\breset[\s-]+time(?:stamp)?s?\b|\b(?:local[\s-]+)?time[\s-]*zone\b',
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
    throw 'Canonical v0.2.0 release notes must not list the already-tagged reset-time timezone correction.'
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
$readmeHeading = '# Usage Indicator for Codex'
$readmeHeadingIndex = $readme.IndexOf($readmeHeading, [StringComparison]::Ordinal)
$readmeIntroductionIndex = $readme.IndexOf(
    'Usage Indicator for Codex is an independent Windows companion',
    [StringComparison]::Ordinal)
if ($readmeHeadingIndex -lt 0 -or
    $readmeIntroductionIndex -lt 0 -or
    $readmeIntroductionIndex -le $readmeHeadingIndex) {
    throw 'README must contain an H1 followed by the introductory product paragraph.'
}
$badgeAreaStart = $readmeHeadingIndex + $readmeHeading.Length
$badgeArea = $readme.Substring(
    $badgeAreaStart,
    $readmeIntroductionIndex - $badgeAreaStart)
$badgeContracts = [ordered]@{
    'CI' = [pscustomobject]@{
        Source = 'https://github\.com/mursyidd/usage-indicator-for-codex/actions/workflows/ci\.yml/badge\.svg\?[^)\s\r\n]*'
        Destination = 'https://github\.com/mursyidd/usage-indicator-for-codex/actions/workflows/ci\.yml(?=\s*\))'
        RequiredSourceFragment = 'branch=master'
    }
    'Latest Release' = [pscustomobject]@{
        Source = 'https://img\.shields\.io/github/v/release/mursyidd/usage-indicator-for-codex(?:\?[^)\s\r\n]*)?'
        Destination = 'https://github\.com/mursyidd/usage-indicator-for-codex/releases/latest(?=\s*\))'
        RequiredSourceFragment = $null
    }
    'MIT licence' = [pscustomobject]@{
        Source = 'https://img\.shields\.io/github/license/mursyidd/usage-indicator-for-codex(?:\?[^)\s\r\n]*)?'
        Destination = '(?<![A-Za-z0-9_./-])(?:\./)?LICENSE(?=\s*\))'
        RequiredSourceFragment = $null
    }
}
foreach ($badgeContract in $badgeContracts.GetEnumerator()) {
    $sourceMatches = @([regex]::Matches(
        $badgeArea,
        $badgeContract.Value.Source,
        [Text.RegularExpressions.RegexOptions]::IgnoreCase))
    $destinationMatches = @([regex]::Matches(
        $badgeArea,
        $badgeContract.Value.Destination,
        [Text.RegularExpressions.RegexOptions]::IgnoreCase))
    if ($sourceMatches.Count -ne 1 -or $destinationMatches.Count -ne 1) {
        throw "README badge area must contain exactly one $($badgeContract.Key) source and destination."
    }
    if ($null -ne $badgeContract.Value.RequiredSourceFragment -and
        $sourceMatches[0].Value.IndexOf(
            $badgeContract.Value.RequiredSourceFragment,
            [StringComparison]::Ordinal) -lt 0) {
        throw "README $($badgeContract.Key) badge source must contain $($badgeContract.Value.RequiredSourceFragment)."
    }
}
foreach ($forbiddenBadgeConcept in @(
    'downloads',
    'stars',
    'forks',
    'Windows version',
    '.NET runtime',
    'build size',
    'OpenAI',
    'ChatGPT',
    'official Codex'
)) {
    if ([regex]::IsMatch(
        $readme.Substring($readmeHeadingIndex, $readmeIntroductionIndex - $readmeHeadingIndex),
        [regex]::Escape($forbiddenBadgeConcept),
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        throw "README badge row contains a forbidden concept: $forbiddenBadgeConcept"
    }
}
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

$issueTemplateDirectory = Join-Path $repositoryRoot '.github\ISSUE_TEMPLATE'
$bugReportPath = Join-Path $issueTemplateDirectory 'bug-report.yml'
$featureRequestPath = Join-Path $issueTemplateDirectory 'feature-request.yml'
$issueConfigPath = Join-Path $issueTemplateDirectory 'config.yml'
foreach ($issueTemplatePath in @($bugReportPath, $featureRequestPath, $issueConfigPath)) {
    if (-not (Test-Path -LiteralPath $issueTemplatePath -PathType Leaf)) {
        throw "Required GitHub issue template is missing: $issueTemplatePath"
    }
}

$issueConfig = Get-Content -LiteralPath $issueConfigPath -Raw
foreach ($requiredIssueConfigFragment in @(
    'blank_issues_enabled: false',
    'https://github.com/mursyidd/usage-indicator-for-codex/security/policy',
    'https://github.com/mursyidd/usage-indicator-for-codex#troubleshooting'
)) {
    if ($issueConfig.IndexOf($requiredIssueConfigFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Issue chooser is missing required configuration: $requiredIssueConfigFragment"
    }
}

$issueForms = [ordered]@{
    'bug-report.yml' = Get-Content -LiteralPath $bugReportPath -Raw
    'feature-request.yml' = Get-Content -LiteralPath $featureRequestPath -Raw
}
$supportedIssueFormComponentTypes = @('markdown', 'checkboxes', 'dropdown', 'input', 'textarea')
foreach ($issueFormEntry in $issueForms.GetEnumerator()) {
    $issueForm = $issueFormEntry.Value
    if ($issueForm -match "`t") {
        throw "$($issueFormEntry.Key) must use spaces instead of tabs."
    }
    if (@([regex]::Matches($issueForm, '(?m)^body:\s*$')).Count -ne 1) {
        throw "$($issueFormEntry.Key) must contain exactly one top-level body block."
    }
    $componentTypes = @([regex]::Matches($issueForm, '(?m)^  - type:\s+([^\r\n]+)\s*$') |
        ForEach-Object { $_.Groups[1].Value.Trim() })
    if ($componentTypes.Count -eq 0 -or
        @($componentTypes | Where-Object { $_ -notin $supportedIssueFormComponentTypes }).Count -ne 0) {
        throw "$($issueFormEntry.Key) uses an unsupported Issue Forms component type."
    }
    $componentIds = @([regex]::Matches($issueForm, '(?m)^    id:\s+([^\r\n]+)\s*$') |
        ForEach-Object { $_.Groups[1].Value.Trim() })
    if ($componentIds.Count -ne @($componentIds | Select-Object -Unique).Count) {
        throw "$($issueFormEntry.Key) contains duplicate component IDs."
    }
    foreach ($requiredIssueFormFragment in @('name:', 'description:', 'attributes:')) {
        if ($issueForm.IndexOf($requiredIssueFormFragment, [StringComparison]::Ordinal) -lt 0) {
            throw "$($issueFormEntry.Key) is missing required Issue Forms structure: $requiredIssueFormFragment"
        }
    }
}

foreach ($requiredBugReportFragment in @(
    'Application version',
    'usage-indicator version',
    'usage-indicator status',
    'Windows environment',
    'Codex environment',
    'Actual behaviour',
    'Expected behaviour',
    'Reproduction steps',
    'Secret safety',
    'security-reporting process',
    'CODEX_CLI_PATH'
)) {
    if ($issueForms['bug-report.yml'].IndexOf($requiredBugReportFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Bug report form is missing required diagnostic or safety field: $requiredBugReportFragment"
    }
}
if ($issueForms['bug-report.yml'] -match '(?m)^  - type: dropdown\r?\n    id: application-version\s*$') {
    throw 'Bug report form must not hardcode product version as a dropdown.'
}

foreach ($requiredFeatureRequestFragment in @(
    'Existing requests',
    'Problem or limitation',
    'What problem or limitation are you encountering?',
    'Desired outcome',
    'What would you like to be able to do?',
    'Affected surface',
    'Product boundary',
    'does not modify Codex Desktop',
    'does not read Codex Desktop credentials',
    'does not substitute OpenAI Platform API billing or usage data',
    'Arm64 is unverified through x64 emulation'
)) {
    if ($issueForms['feature-request.yml'].IndexOf($requiredFeatureRequestFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Feature request form is missing required field or boundary: $requiredFeatureRequestFragment"
    }
}
$featureProblemIndex = $issueForms['feature-request.yml'].IndexOf('Problem or limitation', [StringComparison]::Ordinal)
$featureProposedBehaviourIndex = $issueForms['feature-request.yml'].IndexOf('Proposed behaviour', [StringComparison]::Ordinal)
if ($featureProblemIndex -lt 0 -or $featureProposedBehaviourIndex -lt 0 -or $featureProblemIndex -ge $featureProposedBehaviourIndex) {
    throw 'Feature request form must ask about the problem before a proposed solution.'
}

foreach ($issueFormEntry in $issueForms.GetEnumerator()) {
    if ([regex]::IsMatch(
        $issueFormEntry.Value,
        '(?im)^\s*(?:description|placeholder|value):.*\b(?:paste|enter|provide|share|attach)\b.*\b(?:token|api key|password|credential file|browser (?:data|profile)|(?:private )?account email)\b')) {
        throw "$($issueFormEntry.Key) requests sensitive diagnostic content."
    }
}

$socialPreviewPath = Join-Path $repositoryRoot 'docs\images\usage-indicator-social-preview.png'
if (-not (Test-Path -LiteralPath $socialPreviewPath -PathType Leaf)) {
    throw 'Social-preview image is missing.'
}
$socialPreviewBytes = [IO.File]::ReadAllBytes($socialPreviewPath)
$pngSignature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
if ($socialPreviewBytes.Length -lt 24 -or
    -not [Linq.Enumerable]::SequenceEqual([byte[]]$pngSignature, [byte[]]$socialPreviewBytes[0..7])) {
    throw 'Social-preview image must have a valid PNG signature.'
}
$socialPreviewWidth = [System.BitConverter]::ToUInt32(
    [byte[]]@($socialPreviewBytes[19], $socialPreviewBytes[18], $socialPreviewBytes[17], $socialPreviewBytes[16]),
    0)
$socialPreviewHeight = [System.BitConverter]::ToUInt32(
    [byte[]]@($socialPreviewBytes[23], $socialPreviewBytes[22], $socialPreviewBytes[21], $socialPreviewBytes[20]),
    0)
if ($socialPreviewWidth -ne 1280 -or $socialPreviewHeight -ne 640) {
    throw "Social-preview image dimensions must be 1280x640; found ${socialPreviewWidth}x${socialPreviewHeight}."
}
if ($socialPreviewBytes.Length -ge 1MB) {
    throw 'Social-preview image must be smaller than 1 MB.'
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
    '$notesPath = Join-Path $env:GITHUB_WORKSPACE ".github\release-notes\$($env:GITHUB_REF_NAME).md"',
    'Test-Path -LiteralPath $notesPath -PathType Leaf',
    '$notes = Get-Content -LiteralPath $notesPath -Raw',
    '[string]::IsNullOrWhiteSpace($notes)',
    'gh release create $env:GITHUB_REF_NAME @assets',
    '--verify-tag',
    '--fail-on-no-commits',
    '--title "Usage Indicator for Codex $env:GITHUB_REF_NAME"',
    '--notes-file $notesPath'
)) {
    if ($releaseWorkflow.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Release workflow is missing version/asset invariant: $fragment"
    }
}
if ($releaseWorkflow.IndexOf('--generate-notes', [StringComparison]::Ordinal) -ge 0) {
    throw 'Release workflow must publish the canonical notes file instead of generating notes.'
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

$applicationProjectPath = Join-Path $repositoryRoot 'src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj'
$applicationManifestPath = Join-Path $repositoryRoot 'src\UsageIndicatorForCodex\app.manifest'
$applicationProject = Get-Content -LiteralPath $applicationProjectPath -Raw
if ($applicationProject.IndexOf(
    '<ApplicationManifest>app.manifest</ApplicationManifest>',
    [StringComparison]::Ordinal) -lt 0) {
    throw 'The managed application project must embed app.manifest.'
}
if (-not (Test-Path -LiteralPath $applicationManifestPath -PathType Leaf)) {
    throw 'The managed application manifest is missing.'
}
$applicationManifest = Get-Content -LiteralPath $applicationManifestPath -Raw
foreach ($requiredManifestFragment in @(
    '<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>',
    '<dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>'
)) {
    if ($applicationManifest.IndexOf($requiredManifestFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "The managed application manifest is missing DPI contract: $requiredManifestFragment"
    }
}

Write-Output 'PASS repository ignore, local preservation, version, commands, documentation, and release contract'
