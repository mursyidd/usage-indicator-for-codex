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
    'Flags: nowait postinstall skipifsilent',
    'StartupPage := CreateCustomPage(',
    'wpSelectTasks,',
    'StartupCheckBox.Caption := ''Start with Windows'';',
    'StartupCheckBox.Checked := False;',
    'ExpandConstant(''{app}\bin\usage-indicator.exe'')',
    'ExecAndCaptureOutput(',
    'ewWaitUntilTerminated',
    'GetArrayLength(Output.StdOut) <> 3',
    'GetArrayLength(Output.StdErr) <> 0',
    'Output.Error',
    'IsBooleanStatusRecord(Output.StdOut[0], ''running'')',
    'IsBooleanStatusRecord(Output.StdOut[1], ''indicator-enabled'')',
    'Value = Name + '': true''',
    'Value = Name + '': false''',
    '''startup: enabled''',
    '''startup: disabled''',
    '''startup: unrecognized''',
    'StartupInitialChecked',
    'StartupPreferenceKnown',
    'StartupUserChanged',
    'ApplyStartupPreference',
    '''enable-startup''',
    '''disable-startup'''
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

function Get-StartupMutationModel {
    param(
        [bool]$PreferenceKnown,
        [bool]$InitialChecked,
        [bool]$CurrentChecked,
        [bool]$UserChanged
    )

    if (-not $UserChanged) {
        return 'none'
    }
    if ($PreferenceKnown -and $InitialChecked -eq $CurrentChecked) {
        return 'none'
    }

    if ($CurrentChecked) {
        return 'enable-startup'
    }

    return 'disable-startup'
}

function Read-StartupStatusModel {
    param(
        [string[]]$StdOut,
        [string[]]$StdErr = @(),
        [int]$ExitCode = 0,
        [bool]$LaunchSucceeded = $true,
        [bool]$CaptureError = $false
    )

    if (-not $LaunchSucceeded -or $ExitCode -ne 0 -or $CaptureError -or
        $StdErr.Count -ne 0 -or $StdOut.Count -ne 3) {
        return $null
    }
    if ($StdOut[0] -cnotin @('running: true', 'running: false') -or
        $StdOut[1] -cnotin @('indicator-enabled: true', 'indicator-enabled: false') -or
        $StdOut[2] -cnotin @('startup: enabled', 'startup: disabled', 'startup: unrecognized')) {
        return $null
    }

    return $StdOut[2].Substring('startup: '.Length)
}

$validStatus = @('running: false', 'indicator-enabled: true', 'startup: enabled')
if ((Read-StartupStatusModel -StdOut $validStatus) -cne 'enabled') {
    throw 'Installer status model rejected the exact complete status contract.'
}
foreach ($invalidStatus in @(
    @(,'running: false'),
    @('running: false', 'indicator-enabled: true', 'startup: enabled', 'extra'),
    @('Running: false', 'indicator-enabled: true', 'startup: enabled'),
    @('running: false', 'indicator-enabled: yes', 'startup: enabled'),
    @('running: false', 'indicator-enabled: true', 'startup: unknown')
)) {
    if ($null -ne (Read-StartupStatusModel -StdOut $invalidStatus)) {
        throw "Installer status model accepted malformed output: $($invalidStatus -join ' | ')"
    }
}
if ($null -ne (Read-StartupStatusModel -StdOut $validStatus -StdErr @('error')) -or
    $null -ne (Read-StartupStatusModel -StdOut $validStatus -ExitCode 1) -or
    $null -ne (Read-StartupStatusModel -StdOut $validStatus -LaunchSucceeded $false) -or
    $null -ne (Read-StartupStatusModel -StdOut $validStatus -CaptureError $true)) {
    throw 'Installer status model accepted a failed launch, exit, stderr, or capture result.'
}

if ((Get-StartupMutationModel $false $false $false $false) -cne 'none') {
    throw 'Unknown unchanged startup state must be preserved.'
}
if ((Get-StartupMutationModel $true $true $true $false) -cne 'none') {
    throw 'Known enabled startup state must be preserved when unchanged.'
}
if ((Get-StartupMutationModel $true $true $false $true) -cne 'disable-startup') {
    throw 'Explicitly clearing a recognized enabled startup state must disable startup.'
}
if ((Get-StartupMutationModel $true $false $true $true) -cne 'enable-startup') {
    throw 'Explicitly selecting startup must enable startup.'
}
if ((Get-StartupMutationModel $false $false $true $true) -cne 'enable-startup') {
    throw 'Explicit opt-in from unknown state must enable startup.'
}
if ((Get-StartupMutationModel $false $false $false $true) -cne 'disable-startup') {
    throw 'An explicit opt-out from unknown state must disable startup.'
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
