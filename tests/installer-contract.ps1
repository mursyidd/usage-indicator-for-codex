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
    'DefaultDirName={code:GetDefaultDirName}',
    'ExpandConstant(''{localappdata}\Programs\UsageIndicatorForCodex'')',
    'PrivilegesRequired=lowest',
    'ArchitecturesAllowed=x64compatible',
    'MinVersion=10.0.22000',
    '#ifndef RepositoryLicensePath',
    '#error RepositoryLicensePath must be defined.',
    '#ifndef UpdateHostPath',
    '#error UpdateHostPath must be defined.',
    'LicenseFile={#RepositoryLicensePath}',
    '#ifndef InstallerAppId',
    'AppId={#InstallerAppId}',
    'OutputBaseFilename={#InstallerBaseName}',
    'VersionInfoVersion={#ProductVersion}.0',
    'Source: "{#PublishDirectory}\*"; DestDir: "{app}\app"; Excludes: "UsageIndicatorForCodex.exe,LICENSE.txt"',
    'Source: "{#RepositoryLicensePath}"; DestDir: "{app}\app"; DestName: "LICENSE.txt"',
    'Source: "{#InstalledLauncher}"; DestDir: "{app}\bin"; DestName: "usage-indicator.exe"',
    'Source: "{#UpdateHostPath}"; DestDir: "{app}\updater"; DestName: "UsageIndicatorForCodex.UpdateHost.exe"',
    'Check: ShouldInstallLauncher',
    'ChangesEnvironment=yes',
    'CloseApplications=yes',
    'RestartApplications=no',
    '#define InstallerStateSubKey "Software\UsageIndicatorForCodex\Installer"',
    'InstallerStateKey = ''{#InstallerStateSubKey}'';',
    'BootstrapVersionValue = ''BootstrapVersion'';',
    'InstallPathValue = ''InstallPath'';',
    'InstalledVersionValue = ''InstalledVersion'';',
    'SupportedBootstrapVersion = 1;',
    'PathOwnershipValue = ''PathEntryOwned'';',
    'RegWriteExpandStringValue(HKCU, EnvironmentKey, ''Path'', PathValue)',
    'RegWriteDWordValue(HKCU, InstallerStateKey, PathOwnershipValue, 1)',
    'RegQueryDWordValue(',
    'if (not Removed) and',
    'RegDeleteValue(HKCU, InstallerStateKey, PathOwnershipValue)',
    'if CurStep = ssPostInstall then',
    'WriteInstalledState;',
    'if not CliUpdateMode then',
    'if CurUninstallStep = usUninstall then',
    'Flags: nowait postinstall skipifsilent',
    'Check: ShouldRunPostInstall',
    'function IsCliUpdateCommandLine: Boolean;',
    'function ValidateCliUpdateInstallation: Boolean;',
    'if CliUpdateMode and (not WizardSilent) then',
    'The private /CLIUPDATE mode requires silent installer execution.',
    'ExpandConstant(''{param:BOOTSTRAPVERSION|}'')',
    'The private /CLIUPDATE mode requires an existing bootstrap-v1 installation.',
    'Silent installation is supported only for a validated private /CLIUPDATE.',
    'function ShouldInstallLauncher: Boolean;',
    'function ShouldRunPostInstall: Boolean;',
    'function ShouldCreateShortcuts: Boolean;',
    'Check: ShouldCreateShortcuts',
    'StartupPage := CreateCustomPage(',
    'wpSelectTasks,',
    'StartupCheckBox.Caption := ''Start with Windows'';',
    'StartupCheckBox.Checked := False;',
    'ExpandConstant(''{app}\bin\usage-indicator.exe'')',
    'ExecAndCaptureOutput(',
    'ewWaitUntilTerminated',
    'LineCount := GetArrayLength(Output.StdOut)',
    '(LineCount <> 3) and',
    '(LineCount <> 4)',
    'GetArrayLength(Output.StdErr) <> 0',
    'Output.Error',
    'IsBooleanStatusRecord(Output.StdOut[0], ''running'')',
    'IsBooleanStatusRecord(Output.StdOut[1], ''indicator-enabled'')',
    'StartupIndex := 2',
    'if LineCount = 4 then',
    '''credit-expiry: enabled''',
    '''credit-expiry: disabled''',
    'StartupIndex := 3',
    'Output.StdOut[StartupIndex]',
    'Value = Name + '': true''',
    'Value = Name + '': false''',
    '''startup: enabled''',
    '''startup: disabled''',
    '''startup: unrecognized''',
    'StartupInitialChecked',
    'StartupPreferenceKnown',
    'StartupCollisionDetected',
    'StartupUserChanged',
    'StartupCheckBox.Enabled := not StartupCollisionDetected',
    'StartupCollisionMessage.Visible := StartupCollisionDetected',
    'A same-name unrecognized Windows startup task must be inspected manually.',
    'if StartupCollisionDetected then',
    'ApplyStartupPreference',
    'RunStartupCleanupForUninstall',
    'RemoveInstallerState',
    'RegDeleteValue(HKCU, InstallerStateKey, BootstrapVersionValue)',
    'RegDeleteValue(HKCU, InstallerStateKey, InstallPathValue)',
    'RegDeleteValue(HKCU, InstallerStateKey, InstalledVersionValue)',
    'RegDeleteValue(HKCU, InstallerStateKey, PathOwnershipValue)',
    'RegDeleteKeyIfEmpty(HKCU, InstallerStateKey)',
    'LogStartupCommandOutput(''Startup cleanup'', Output)',
    'else if ResultCode = 2 then',
    'Foreign same-name startup tasks were preserved for manual inspection.',
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
    '[UninstallRun]',
    '/SILENT',
    '/VERYSILENT',
    'uninsdeletevalue',
    'Root: HKLM',
    'DestDir: "{app}\bin"; DestName: "UsageIndicatorForCodex.Gui.exe"',
    'CliUpdateMode := WizardSilent'
)) {
    if ($script.IndexOf($prohibitedFragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Installer contains prohibited behavior: $prohibitedFragment"
    }
}

$uninstallCleanupMatch = [regex]::Match(
    $script,
    '(?s)procedure RemoveInstallerState;.*?begin(?<body>.*?)end;')
if (-not $uninstallCleanupMatch.Success) {
    throw 'Installer does not define bounded installer-state cleanup.'
}
$uninstallCleanupBody = $uninstallCleanupMatch.Groups['body'].Value
foreach ($stateValue in @(
    'BootstrapVersionValue',
    'InstallPathValue',
    'InstalledVersionValue',
    'PathOwnershipValue'
)) {
    if ($uninstallCleanupBody.IndexOf(
        "RegDeleteValue(HKCU, InstallerStateKey, $stateValue)",
        [StringComparison]::Ordinal) -lt 0) {
        throw "Uninstall cleanup does not delete $stateValue."
    }
}
if ($uninstallCleanupBody.IndexOf(
    'RegDeleteKeyIfEmpty(HKCU, InstallerStateKey)',
    [StringComparison]::Ordinal) -lt 0) {
    throw 'Uninstall cleanup does not delete the empty installer key.'
}

$buildInstallerScript = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'scripts\build-installer.ps1') -Raw
foreach ($fragment in @(
    '[Parameter(Mandatory)][string]$UpdateHostPath',
    'UsageIndicatorForCodex.UpdateHost.exe',
    'VersionInfo.ProductVersion',
    '/DUpdateHostPath=',
    '$IntegrationTestInstallerStateSubKey',
    '$IntegrationTestAppId',
    'Software\UsageIndicatorForCodex\IntegrationTests\',
    '/DInstallerStateSubKey=',
    '/DInstallerAppId='
)) {
    if ($buildInstallerScript.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Installer build contract fragment is missing: $fragment"
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
        [bool]$UserChanged,
        [bool]$CollisionDetected = $false
    )

    if ($CollisionDetected) {
        return 'none'
    }
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
        $StdErr.Count -ne 0 -or $StdOut.Count -notin @(3, 4)) {
        return $null
    }
    if ($StdOut[0] -cnotin @('running: true', 'running: false') -or
        $StdOut[1] -cnotin @('indicator-enabled: true', 'indicator-enabled: false')) {
        return $null
    }

    $startupIndex = 2
    if ($StdOut.Count -eq 4) {
        if ($StdOut[2] -cnotin @('credit-expiry: enabled', 'credit-expiry: disabled')) {
            return $null
        }
        $startupIndex = 3
    }
    if ($StdOut[$startupIndex] -cnotin @(
            'startup: enabled',
            'startup: disabled',
            'startup: unrecognized')) {
        return $null
    }

    return $StdOut[$startupIndex].Substring('startup: '.Length)
}

$validLegacyStatus = @(
    'running: true',
    'indicator-enabled: false',
    'startup: enabled')
if ((Read-StartupStatusModel -StdOut $validLegacyStatus) -cne 'enabled') {
    throw 'Installer status model rejected the exact legacy three-line status contract.'
}

$validCurrentStatus = @(
    'running: false',
    'indicator-enabled: true',
    'credit-expiry: enabled',
    'startup: disabled')
if ((Read-StartupStatusModel -StdOut $validCurrentStatus) -cne 'disabled') {
    throw 'Installer status model rejected the exact current four-line status contract.'
}
foreach ($invalidStatus in @(
    @(,'running: false'),
    @('running: false', 'indicator-enabled: true'),
    @('running: false', 'indicator-enabled: true', 'startup: enabled', 'extra'),
    @('running: false', 'indicator-enabled: true', 'credit-expiry: disabled', 'startup: enabled', 'extra'),
    @('Running: false', 'indicator-enabled: true', 'startup: enabled'),
    @('running: false', 'indicator-enabled: yes', 'startup: enabled'),
    @('running: false', 'indicator-enabled: true', 'startup: unknown'),
    @('running: false', 'indicator-enabled: true', 'credit-expiry: disabled'),
    @('Running: false', 'indicator-enabled: true', 'credit-expiry: disabled', 'startup: enabled'),
    @('running: false', 'indicator-enabled: yes', 'credit-expiry: disabled', 'startup: enabled'),
    @('running: false', 'indicator-enabled: true', 'credit-expiry: invalid', 'startup: enabled'),
    @('running: false', 'indicator-enabled: true', 'credit-expiry: disabled', 'startup: unknown'),
    @('running: false', 'indicator-enabled: true', 'startup: enabled', 'credit-expiry: disabled')
)) {
    if ($null -ne (Read-StartupStatusModel -StdOut $invalidStatus)) {
        throw "Installer status model accepted malformed output: $($invalidStatus -join ' | ')"
    }
}
if ($null -ne (Read-StartupStatusModel -StdOut $validCurrentStatus -StdErr @('error')) -or
    $null -ne (Read-StartupStatusModel -StdOut $validCurrentStatus -ExitCode 1) -or
    $null -ne (Read-StartupStatusModel -StdOut $validCurrentStatus -LaunchSucceeded $false) -or
    $null -ne (Read-StartupStatusModel -StdOut $validCurrentStatus -CaptureError $true)) {
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
if ((Read-StartupStatusModel -StdOut @(
        'running: false',
        'indicator-enabled: true',
        'credit-expiry: disabled',
        'startup: unrecognized')) -cne 'unrecognized') {
    throw 'Installer status model must retain an exact unrecognized collision state.'
}
if ((Read-StartupStatusModel -StdOut @(
        'running: false',
        'indicator-enabled: true',
        'startup: unrecognized')) -cne 'unrecognized') {
    throw 'Legacy installer status model must retain an exact unrecognized collision state.'
}
if ((Get-StartupMutationModel $false $false $true $true $true) -cne 'none' -or
    (Get-StartupMutationModel $false $false $false $true $true) -cne 'none') {
    throw 'An unrecognized startup collision must disable all installer startup mutation.'
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

Write-Output 'PASS per-user installer layout, guarded CLI update, transition, and ownership contract'
