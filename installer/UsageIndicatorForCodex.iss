#ifndef PublishDirectory
  #error PublishDirectory must be defined.
#endif
#ifndef InstalledLauncher
  #error InstalledLauncher must be defined.
#endif
#ifndef ProductVersion
  #error ProductVersion must be defined.
#endif
#ifndef InstallerBaseName
  #error InstallerBaseName must be defined.
#endif
#ifndef RepositoryUrl
  #error RepositoryUrl must be defined.
#endif

#define ProductName "Usage Indicator for Codex"
#define ProductExecutable "UsageIndicatorForCodex.Gui.exe"

[Setup]
AppId={{3C77270D-28B4-45B7-BE77-B051195C969D}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppVerName={#ProductName} {#ProductVersion}
AppPublisher=Usage Indicator for Codex contributors
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}
AppUpdatesURL={#RepositoryUrl}/releases
DefaultDirName={localappdata}\Programs\UsageIndicatorForCodex
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputBaseFilename={#InstallerBaseName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes
UninstallDisplayIcon={app}\app\{#ProductExecutable}
SetupLogging=yes
VersionInfoVersion={#ProductVersion}.0
VersionInfoProductVersion={#ProductVersion}
VersionInfoProductName={#ProductName}
VersionInfoDescription={#ProductName} per-user installer

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}\app"; Excludes: "UsageIndicatorForCodex.exe"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#InstalledLauncher}"; DestDir: "{app}\bin"; DestName: "usage-indicator.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\Usage Indicator for Codex"; Filename: "{app}\app\{#ProductExecutable}"
Name: "{group}\Uninstall Usage Indicator for Codex"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\app\{#ProductExecutable}"; Description: "Start Usage Indicator for Codex"; Flags: nowait postinstall skipifsilent

[Code]
const
  EnvironmentKey = 'Environment';
  InstallerStateKey = 'Software\UsageIndicatorForCodex\Installer';
  PathOwnershipValue = 'PathEntryOwned';

var
  StartupPage: TWizardPage;
  StartupCheckBox: TNewCheckBox;
  StartupCollisionMessage: TNewStaticText;
  StartupPageInitialized: Boolean;
  StartupInitializing: Boolean;
  StartupInitialChecked: Boolean;
  StartupPreferenceKnown: Boolean;
  StartupCollisionDetected: Boolean;
  StartupUserChanged: Boolean;

procedure StartupCheckBoxClick(Sender: TObject);
begin
  if not StartupInitializing then
    StartupUserChanged := True;
end;

procedure InitializeWizard;
begin
  StartupPage := CreateCustomPage(
    wpSelectTasks,
    'Startup',
    'Choose whether Usage Indicator for Codex starts when you sign in.');
  StartupCheckBox := TNewCheckBox.Create(StartupPage.Surface);
  StartupCheckBox.Parent := StartupPage.Surface;
  StartupCheckBox.Caption := 'Start with Windows';
  StartupCheckBox.Left := 0;
  StartupCheckBox.Top := ScaleY(8);
  StartupCheckBox.Width := StartupPage.SurfaceWidth;
  StartupCheckBox.Checked := False;
  StartupCheckBox.OnClick := @StartupCheckBoxClick;

  StartupCollisionMessage := TNewStaticText.Create(StartupPage.Surface);
  StartupCollisionMessage.Parent := StartupPage.Surface;
  StartupCollisionMessage.Caption :=
    'A same-name unrecognized Windows startup task must be inspected manually. ' +
    'Setup will preserve it unchanged.';
  StartupCollisionMessage.Left := 0;
  StartupCollisionMessage.Top := StartupCheckBox.Top + StartupCheckBox.Height + ScaleY(8);
  StartupCollisionMessage.Width := StartupPage.SurfaceWidth;
  StartupCollisionMessage.AutoSize := False;
  StartupCollisionMessage.WordWrap := True;
  StartupCollisionMessage.Height := ScaleY(42);
  StartupCollisionMessage.Visible := False;
end;

function IsBooleanStatusRecord(const Value, Name: string): Boolean;
begin
  Result :=
    (Value = Name + ': true') or
    (Value = Name + ': false');
end;

function TryInspectExistingStartup(
  var PreferenceKnown, StartupEnabled, CollisionDetected: Boolean): Boolean;
var
  CliPath: string;
  ResultCode: Integer;
  Output: TExecOutput;
  LaunchSucceeded: Boolean;
begin
  Result := False;
  PreferenceKnown := False;
  StartupEnabled := False;
  CollisionDetected := False;
  CliPath := ExpandConstant('{app}\bin\usage-indicator.exe');
  if not FileExists(CliPath) then
    Exit;

  try
    LaunchSucceeded := ExecAndCaptureOutput(
      CliPath,
      'status',
      '',
      SW_SHOWNORMAL,
      ewWaitUntilTerminated,
      ResultCode,
      Output);
  except
    Log('Existing startup status inspection failed: ' + GetExceptionMessage);
    Exit;
  end;

  if (not LaunchSucceeded) or
     (ResultCode <> 0) or
     Output.Error or
     (GetArrayLength(Output.StdOut) <> 3) or
     (GetArrayLength(Output.StdErr) <> 0) then
  begin
    Log('Existing startup status inspection returned an unusable result.');
    Exit;
  end;

  if (not IsBooleanStatusRecord(Output.StdOut[0], 'running')) or
     (not IsBooleanStatusRecord(Output.StdOut[1], 'indicator-enabled')) or
     ((Output.StdOut[2] <> 'startup: enabled') and
      (Output.StdOut[2] <> 'startup: disabled') and
      (Output.StdOut[2] <> 'startup: unrecognized')) then
  begin
    Log('Existing startup status inspection returned malformed records.');
    Exit;
  end;

  Result := True;
  if Output.StdOut[2] = 'startup: enabled' then
  begin
    PreferenceKnown := True;
    StartupEnabled := True;
  end
  else if Output.StdOut[2] = 'startup: disabled' then
  begin
    PreferenceKnown := True;
    StartupEnabled := False;
  end
  else
  begin
    CollisionDetected := True;
  end;
end;

procedure InitializeStartupPage;
var
  StartupEnabled: Boolean;
begin
  if StartupPageInitialized then
    Exit;

  StartupPageInitialized := True;
  TryInspectExistingStartup(
    StartupPreferenceKnown,
    StartupEnabled,
    StartupCollisionDetected);
  StartupInitializing := True;
  if StartupPreferenceKnown then
    StartupCheckBox.Checked := StartupEnabled
  else
    StartupCheckBox.Checked := False;
  StartupInitialChecked := StartupCheckBox.Checked;
  StartupCheckBox.Enabled := not StartupCollisionDetected;
  StartupCollisionMessage.Visible := StartupCollisionDetected;
  StartupUserChanged := False;
  StartupInitializing := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = StartupPage.ID then
    InitializeStartupPage;
end;

procedure RunStartupCommand(const Command: string);
var
  CliPath: string;
  ResultCode: Integer;
  Output: TExecOutput;
  LaunchSucceeded: Boolean;
begin
  CliPath := ExpandConstant('{app}\bin\usage-indicator.exe');
  try
    LaunchSucceeded := ExecAndCaptureOutput(
      CliPath,
      Command,
      '',
      SW_SHOWNORMAL,
      ewWaitUntilTerminated,
      ResultCode,
      Output);
  except
    RaiseException(
      'The startup preference could not be applied. ' + GetExceptionMessage);
  end;

  if (not LaunchSucceeded) or
     (ResultCode <> 0) or
     Output.Error or
     (GetArrayLength(Output.StdErr) <> 0) then
    RaiseException('The startup preference could not be applied.');
end;

procedure ApplyStartupPreference;
begin
  if StartupCollisionDetected then
    Exit;

  if not StartupUserChanged then
    Exit;

  if StartupPreferenceKnown and
     (StartupCheckBox.Checked = StartupInitialChecked) then
    Exit;

  if StartupCheckBox.Checked then
    RunStartupCommand('enable-startup')
  else
    RunStartupCommand('disable-startup');
end;

procedure LogStartupCommandOutput(const Prefix: string; Output: TExecOutput);
var
  Index: Integer;
begin
  for Index := 0 to GetArrayLength(Output.StdOut) - 1 do
    Log(Prefix + ' stdout: ' + Output.StdOut[Index]);
  for Index := 0 to GetArrayLength(Output.StdErr) - 1 do
    Log(Prefix + ' stderr: ' + Output.StdErr[Index]);
end;

procedure RunStartupCleanupForUninstall;
var
  CliPath: string;
  ResultCode: Integer;
  Output: TExecOutput;
  LaunchSucceeded: Boolean;
begin
  CliPath := ExpandConstant('{app}\bin\usage-indicator.exe');
  if not FileExists(CliPath) then
  begin
    Log('Startup cleanup skipped because the installed command is unavailable.');
    Exit;
  end;

  try
    LaunchSucceeded := ExecAndCaptureOutput(
      CliPath,
      'disable-startup',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode,
      Output);
    LogStartupCommandOutput('Startup cleanup', Output);
    if (not LaunchSucceeded) or Output.Error then
      Log('Startup cleanup could not be captured; uninstall will continue.')
    else if ResultCode = 0 then
      Log('Recognized application-owned startup tasks were removed.')
    else if ResultCode = 2 then
      Log('Foreign same-name startup tasks were preserved for manual inspection.')
    else
      Log(
        'Startup cleanup exited with code ' + IntToStr(ResultCode) +
        '; uninstall will continue.');
  except
    Log(
      'Startup cleanup failed; uninstall will continue. ' +
      GetExceptionMessage);
  end;
end;

function NormalizePathEntry(Value: string): string;
begin
  Result := Trim(Value);
  if (Length(Result) >= 2) and (Result[1] = '"') and
     (Result[Length(Result)] = '"') then
  begin
    Delete(Result, Length(Result), 1);
    Delete(Result, 1, 1);
    Result := Trim(Result);
  end;

  while (Length(Result) > 3) and
        ((Result[Length(Result)] = '\') or (Result[Length(Result)] = '/')) do
    Delete(Result, Length(Result), 1);
  Result := Lowercase(Result);
end;

function PathContainsEntry(const PathValue, ExpectedEntry: string): Boolean;
var
  Remaining: string;
  Entry: string;
  Separator: Integer;
  NormalizedExpected: string;
begin
  Result := False;
  NormalizedExpected := NormalizePathEntry(ExpectedEntry);
  Remaining := PathValue;
  while Remaining <> '' do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Entry := Remaining;
      Remaining := '';
    end
    else
    begin
      Entry := Copy(Remaining, 1, Separator - 1);
      Delete(Remaining, 1, Separator);
    end;

    if NormalizePathEntry(Entry) = NormalizedExpected then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddOwnedBinToPath;
var
  PathValue: string;
  BinPath: string;
begin
  BinPath := ExpandConstant('{app}\bin');
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', PathValue) then
    PathValue := '';

  if PathContainsEntry(PathValue, BinPath) then
    Exit;

  if (PathValue <> '') and (PathValue[Length(PathValue)] <> ';') then
    PathValue := PathValue + ';';
  PathValue := PathValue + BinPath;
  if not RegWriteDWordValue(HKCU, InstallerStateKey, PathOwnershipValue, 1) then
    RaiseException('PATH ownership state could not be recorded.');
  if not RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', PathValue) then
  begin
    RegDeleteValue(HKCU, InstallerStateKey, PathOwnershipValue);
    RaiseException('The current-user PATH could not be updated.');
  end;
end;

procedure RemoveOwnedBinFromPath;
var
  Owned: Cardinal;
  PathValue: string;
  BinPath: string;
  Remaining: string;
  Entry: string;
  NewPath: string;
  Separator: Integer;
  Removed: Boolean;
begin
  if not RegQueryDWordValue(
    HKCU, InstallerStateKey, PathOwnershipValue, Owned) or (Owned <> 1) then
    Exit;

  BinPath := ExpandConstant('{app}\bin');
  if RegQueryStringValue(HKCU, EnvironmentKey, 'Path', PathValue) then
  begin
    Remaining := PathValue;
    NewPath := '';
    Removed := False;
    while Remaining <> '' do
    begin
      Separator := Pos(';', Remaining);
      if Separator = 0 then
      begin
        Entry := Remaining;
        Remaining := '';
      end
      else
      begin
        Entry := Copy(Remaining, 1, Separator - 1);
        Delete(Remaining, 1, Separator);
      end;

      if (not Removed) and
         (NormalizePathEntry(Entry) = NormalizePathEntry(BinPath)) then
        Removed := True
      else
      begin
        if NewPath <> '' then
          NewPath := NewPath + ';';
        NewPath := NewPath + Entry;
      end;
    end;

    if Removed and
       (not RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', NewPath)) then
      RaiseException('The owned current-user PATH entry could not be removed.');
  end;

  RegDeleteValue(HKCU, InstallerStateKey, PathOwnershipValue);
  RegDeleteKeyIfEmpty(HKCU, InstallerStateKey);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AddOwnedBinToPath;
    ApplyStartupPreference;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RunStartupCleanupForUninstall;
    RemoveOwnedBinFromPath;
  end;
end;
