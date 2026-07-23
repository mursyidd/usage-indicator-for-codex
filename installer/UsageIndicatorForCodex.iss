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

[UninstallRun]
Filename: "{app}\bin\usage-indicator.exe"; Parameters: "disable-startup"; RunOnceId: "DisableOwnedStartup"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
const
  EnvironmentKey = 'Environment';
  InstallerStateKey = 'Software\UsageIndicatorForCodex\Installer';
  PathOwnershipValue = 'PathEntryOwned';

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
    AddOwnedBinToPath;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveOwnedBinFromPath;
end;
