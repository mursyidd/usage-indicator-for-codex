[CmdletBinding()]
param(
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repositoryRoot 'scripts\product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata -RepositoryRoot $repositoryRoot
$scriptPath = Join-Path $repositoryRoot 'installer\UsageIndicatorForCodex.iss'
$script = Get-Content -LiteralPath $scriptPath -Raw

$requiredFragments = @(
    'DefaultDirName={localappdata}\Programs\UsageIndicatorForCodex',
    'PrivilegesRequired=lowest',
    'ArchitecturesAllowed=x64compatible',
    'OutputBaseFilename={#InstallerBaseName}',
    'VersionInfoVersion={#ProductVersion}.0',
    'Source: "{#PublishDirectory}\*"; DestDir: "{app}\app"; Excludes: "UsageIndicatorForCodex.exe"',
    'Source: "{#InstalledLauncher}"; DestDir: "{app}\bin"; DestName: "usage-indicator.exe"',
    'Filename: "{app}\bin\usage-indicator.exe"; Parameters: "disable-startup"',
    'ChangesEnvironment=yes',
    'CloseApplications=yes',
    'RestartApplications=no',
    'InstallerStateKey = ''Software\UsageIndicatorForCodex\Installer'';',
    'PathOwnershipValue = ''PathEntryOwned'';',
    'RegWriteExpandStringValue(HKCU, EnvironmentKey, ''Path'', PathValue)',
    'RegWriteDWordValue(HKCU, InstallerStateKey, PathOwnershipValue, 1)',
    'RegQueryDWordValue(',
    'if (not Removed) and',
    'RegDeleteValue(HKCU, InstallerStateKey, PathOwnershipValue)',
    'if CurStep = ssPostInstall then',
    'if CurUninstallStep = usUninstall then',
    'Flags: nowait postinstall skipifsilent'
)
foreach ($fragment in $requiredFragments) {
    if ($script.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Installer contract fragment is missing: $fragment"
    }
}

foreach ($prohibitedFragment in @(
    'PrivilegesRequired=admin',
    'PrivilegesRequired=poweruser',
    'runascurrentuser',
    '/SILENT',
    '/VERYSILENT',
    'uninsdeletevalue',
    'Root: HKLM',
    'DestDir: "{app}\bin"; DestName: "UsageIndicatorForCodex.Gui.exe"'
)) {
    if ($script.IndexOf($prohibitedFragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Installer contains prohibited behavior: $prohibitedFragment"
    }
}

function Normalize-PathEntry {
    param([string]$Value)
    $normalized = $Value.Trim().Trim('"').Trim()
    while ($normalized.Length -gt 3 -and
        ($normalized.EndsWith('\', [StringComparison]::Ordinal) -or
         $normalized.EndsWith('/', [StringComparison]::Ordinal))) {
        $normalized = $normalized.Substring(0, $normalized.Length - 1)
    }
    return $normalized.ToLowerInvariant()
}

function Add-PathEntryModel {
    param(
        [string]$PathValue,
        [string]$BinPath,
        [ref]$Owned
    )
    foreach ($entry in @($PathValue -split ';')) {
        if ((Normalize-PathEntry $entry) -ceq (Normalize-PathEntry $BinPath)) {
            return $PathValue
        }
    }

    $Owned.Value = $true
    if ([string]::IsNullOrEmpty($PathValue)) {
        return $BinPath
    }
    if ($PathValue.EndsWith(';', [StringComparison]::Ordinal)) {
        return "$PathValue$BinPath"
    }
    return "$PathValue;$BinPath"
}

function Remove-PathEntryModel {
    param(
        [string]$PathValue,
        [string]$BinPath,
        [bool]$Owned
    )
    if (-not $Owned) {
        return $PathValue
    }

    $removed = $false
    $preserved = [Collections.Generic.List[string]]::new()
    foreach ($entry in @($PathValue -split ';')) {
        if (-not $removed -and
            (Normalize-PathEntry $entry) -ceq (Normalize-PathEntry $BinPath)) {
            $removed = $true
        } else {
            $preserved.Add($entry)
        }
    }
    return $preserved -join ';'
}

$binPath = 'C:\Users\Example\AppData\Local\Programs\UsageIndicatorForCodex\bin'
$originalPath = 'C:\Windows;C:\Tools'
$owned = $false
$addedPath = Add-PathEntryModel $originalPath $binPath ([ref]$owned)
if (-not $owned -or $addedPath -cne "$originalPath;$binPath") {
    throw 'Installer PATH model did not add and own its missing bin entry.'
}
if ((Remove-PathEntryModel $addedPath $binPath $owned) -cne $originalPath) {
    throw 'Installer PATH model did not remove only its owned bin entry.'
}

$preexisting = "$originalPath;$binPath"
$owned = $false
$unchangedPath = Add-PathEntryModel $preexisting $binPath ([ref]$owned)
if ($owned -or $unchangedPath -cne $preexisting -or
    (Remove-PathEntryModel $unchangedPath $binPath $owned) -cne $preexisting) {
    throw 'Installer PATH model claimed or removed a pre-existing bin entry.'
}

$duplicatePath = "$binPath;$originalPath;$binPath"
if ((Remove-PathEntryModel $duplicatePath $binPath $true) -cne "$originalPath;$binPath") {
    throw 'Installer PATH model must remove at most one owned matching entry.'
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    $installer = Get-Item -LiteralPath $InstallerPath
    if ($installer.Name -cne $metadata.InstallerAssetName) {
        throw "Compiled installer must be named $($metadata.InstallerAssetName)."
    }
    if ($installer.VersionInfo.ProductVersion.Trim() -cne $metadata.Version) {
        throw "Compiled installer product version must be $($metadata.Version)."
    }
}

Write-Output 'PASS per-user installer layout, interactive behavior, and PATH ownership contract'
