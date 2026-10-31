#ifndef VERSION
  #define VERSION "1.0.0"
#endif
#ifndef PUBLISHDIR
  #define PUBLISHDIR "..\publish"
#endif
#ifndef REDIST
  #define REDIST "..\redist\windowsdesktop-runtime-win-x64.exe"
#endif
#ifndef OUTDIR
  #define OUTDIR "..\dist"
#endif
#ifndef PROJECTURL
  #define PROJECTURL "https://github.com/ChuiFeng-wind/EarKit"
#endif

#define APPNAME "EarKit"
#define EXENAME "EarKit.exe"
#define APPID "{{D90C34B9-5A28-4DF7-80AA-FD733D8D6F20}"
#define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EarKit"
#define LEGACYKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EarfunTools"
#define RUNKEY "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={#APPID}
AppName={#APPNAME}
AppVersion={#VERSION}
AppVerName={#APPNAME} {#VERSION}
AppPublisher=EarKit contributors
AppPublisherURL={#PROJECTURL}
AppSupportURL={#PROJECTURL}
AppUpdatesURL={#PROJECTURL}/releases
DefaultDirName={autopf}\{#APPNAME}
DefaultGroupName={#APPNAME}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
UsedUserAreasWarning=no
AppMutex=EarKitSetupMutex
SetupIconFile=..\src\EarKit\Assets\app.ico
LicenseFile=License.txt
OutputDir={#OUTDIR}
OutputBaseFilename=EarKit-{#VERSION}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#APPNAME}
UninstallDisplayIcon={app}\{#EXENAME}
VersionInfoVersion={#VERSION}.0
VersionInfoCompany=EarKit contributors
VersionInfoDescription={#APPNAME} Installer
VersionInfoProductName={#APPNAME}
VersionInfoProductVersion={#VERSION}
VersionInfoCopyright=Copyright (c) 2026 EarKit contributors
Uninstallable=yes
UsePreviousAppDir=yes
SetupLogging=yes

[Tasks]
Name: "startmenu"; Description: "Start menu shortcut"; GroupDescription: "Additional shortcuts:"; Check: ShouldCreateStartMenuShortcut
Name: "desktop"; Description: "Desktop shortcut"; GroupDescription: "Additional shortcuts:"; Check: ShouldCreateDesktopShortcut
Name: "startwithwindows"; Description: "Start with Windows (minimized to tray)"; GroupDescription: "Windows startup:"; Check: ShouldStartAtLogon

[Files]
Source: "{#PUBLISHDIR}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\EarKit\Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#REDIST}"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{commonprograms}\{#APPNAME}"; Filename: "{app}\{#EXENAME}"; Tasks: startmenu
Name: "{commondesktop}\{#APPNAME}"; Filename: "{app}\{#EXENAME}"; Tasks: desktop

[Registry]
Root: HKLM; Subkey: "Software\EarKit"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\EarKit"; ValueType: string; ValueName: "Version"; ValueData: "{#VERSION}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "{#RUNKEY}"; ValueType: string; ValueName: "{#APPNAME}"; ValueData: """{app}\{#EXENAME}"" --tray"; Tasks: startwithwindows; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#EXENAME}"; Description: "Run {#APPNAME} now"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#EXENAME}"; Flags: runhidden waituntilterminated; RunOnceId: "KillEarKit"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Code]
var
  IsUpgrade: Boolean;
  OldVersion: String;
  PreviousInstallDir: String;
  UpgradeNote: String;
  PreviousStartupCommand: String;
  LegacyStartupCommand: String;
  RemoveUserData: Boolean;

function IsRuntimeInstalled: Boolean;
var
  RuntimeVersions: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := False;
  if RegGetValueNames(HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    RuntimeVersions) then
  begin
    for I := 0 to GetArrayLength(RuntimeVersions) - 1 do
      if Copy(RuntimeVersions[I], 1, 3) = '10.' then
      begin
        Result := True;
        Exit;
      end;
  end;

  if FindFirst(ExpandConstant('{autopf}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function TryGetVersionPart(const Version: String; PartIndex: Integer;
  var Part: Integer): Boolean;
var
  Remaining: String;
  PartText: String;
  Separator: Integer;
  I: Integer;
begin
  Remaining := Version;
  PartText := '';
  for I := 1 to PartIndex do
  begin
    Separator := Pos('.', Remaining);
    if I < PartIndex then
    begin
      if Separator = 0 then
      begin
        Part := 0;
        Result := True;
        Exit;
      end;
      Delete(Remaining, 1, Separator);
    end
    else if Separator = 0 then
      PartText := Remaining
    else
      PartText := Copy(Remaining, 1, Separator - 1);
  end;

  Part := StrToIntDef(PartText, -1);
  Result := Part >= 0;
end;

function TryCompareVersions(const Version1, Version2: String;
  var Comparison: Integer): Boolean;
var
  Part1: Integer;
  Part2: Integer;
  I: Integer;
begin
  Result := False;
  Comparison := 0;
  for I := 1 to 4 do
  begin
    if not TryGetVersionPart(Version1, I, Part1) or
      not TryGetVersionPart(Version2, I, Part2) then
      Exit;
    if Part1 < Part2 then
    begin
      Comparison := -1;
      Result := True;
      Exit;
    end;
    if Part1 > Part2 then
    begin
      Comparison := 1;
      Result := True;
      Exit;
    end;
  end;
  Result := True;
end;

function InitializeSetup: Boolean;
var
  VersionComparison: Integer;
begin
  Result := True;
  IsUpgrade := False;
  PreviousInstallDir := '';
  OldVersion := '';
  UpgradeNote := 'Click Next to continue.';
  PreviousStartupCommand := '';
  LegacyStartupCommand := '';
  RegQueryStringValue(HKCU, '{#RUNKEY}', '{#APPNAME}', PreviousStartupCommand);
  RegQueryStringValue(HKCU, '{#RUNKEY}', 'Earfun Tools', LegacyStartupCommand);

  RegQueryStringValue(HKLM64, 'Software\EarKit', 'InstallDir', PreviousInstallDir);
  if PreviousInstallDir = '' then
    RegQueryStringValue(HKLM64, '{#UNINSTKEY}', 'InstallLocation', PreviousInstallDir);
  if PreviousInstallDir = '' then
    Exit;

  if not FileExists(PreviousInstallDir + '\{#EXENAME}') then
    Exit;

  IsUpgrade := True;
  RegQueryStringValue(HKLM64, 'Software\EarKit', 'Version', OldVersion);
  if OldVersion = '' then
    RegQueryStringValue(HKLM64, '{#UNINSTKEY}', 'DisplayVersion', OldVersion);
  if OldVersion = '' then
    OldVersion := 'Unknown version';

  if TryCompareVersions(OldVersion, '{#VERSION}', VersionComparison) then
  begin
    if VersionComparison = 0 then
      UpgradeNote := 'The same version of {#APPNAME} (' + OldVersion +
        ') is already installed. Setup will reinstall it; this can be used to repair the installation.' + #13#10
    else if VersionComparison < 0 then
      UpgradeNote := '{#APPNAME} ' + OldVersion + ' is already installed. Setup will upgrade to {#VERSION}.' + #13#10
    else
    begin
      UpgradeNote := 'A newer version of {#APPNAME} (' + OldVersion +
        ') is installed. Continuing will downgrade to {#VERSION}.' + #13#10;
      if not WizardSilent and
        (MsgBox('The installed version of {#APPNAME} (' + OldVersion +
          ') is newer than this installer ({#VERSION}).' + #13#10 +
          'Are you sure you want to downgrade?', mbConfirmation, MB_YESNO) <> IDYES) then
      begin
        Result := False;
        Exit;
      end;
    end;
  end
  else
    UpgradeNote := '{#APPNAME} ' + OldVersion +
      ' is already installed. Setup will reinstall it.' + #13#10;

  UpgradeNote := UpgradeNote + 'Install location: ' + PreviousInstallDir + #13#10 +
    'Your settings and custom presets will be kept.' + #13#10#13#10 +
    'Click Next to begin the upgrade.';

end;

procedure InitializeWizard;
begin
  if IsUpgrade then
    WizardForm.DirEdit.Text := PreviousInstallDir;
  WizardForm.WelcomeLabel2.Caption :=
    '{#APPNAME} {#VERSION} will be installed on your computer.' + #13#10#13#10 +
    '{#APPNAME} is an open-source, third-party headset control app for compatible EarFun headsets, ' +
    'with noise control, an equalizer, custom controls, multipoint, and more. ' +
    '{#APPNAME} is not affiliated with EarFun.' + #13#10#13#10 +
    'If the .NET 10 Desktop Runtime is missing, Setup will install it for you.' + #13#10#13#10 +
    UpgradeNote;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsUpgrade and (PageID = wpSelectDir);
end;

function ShouldCreateStartMenuShortcut: Boolean;
begin
  Result := not IsUpgrade or FileExists(ExpandConstant('{commonprograms}\{#APPNAME}.lnk'));
end;

function ShouldCreateDesktopShortcut: Boolean;
begin
  Result := not IsUpgrade or FileExists(ExpandConstant('{commondesktop}\{#APPNAME}.lnk'));
end;

function ShouldStartAtLogon: Boolean;
begin
  Result := (IsUpgrade and (PreviousStartupCommand <> '')) or
    ((not IsUpgrade) and (LegacyStartupCommand <> ''));
end;

procedure DeleteMatchingFiles(const Pattern: String);
var
  FindRec: TFindRec;
begin
  if FindFirst(Pattern, FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and $10) = 0 then
          if not DeleteFile(ExpandConstant('{app}\') + FindRec.Name) then
            RaiseException('Could not replace the existing application file: ' + FindRec.Name);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure RemoveLegacyInstallation;
var
  LegacyInstallDir: String;
  ResultCode: Integer;
begin
  RegQueryStringValue(HKLM64, '{#LEGACYKEY}', 'InstallLocation', LegacyInstallDir);
  if (LegacyInstallDir <> '') and FileExists(LegacyInstallDir + '\Uninstall.exe') then
  begin
    if not Exec(LegacyInstallDir + '\Uninstall.exe', '/S _?=' + LegacyInstallDir,
      '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Could not start the existing Earfun Tools uninstaller.');
    if DirExists(LegacyInstallDir) and not DelTree(LegacyInstallDir, True, True, True) then
      RaiseException('Could not remove the existing Earfun Tools installation.');
  end;

  RegDeleteKeyIncludingSubkeys(HKLM, '{#LEGACYKEY}');
  RegDeleteKeyIncludingSubkeys(HKLM, 'Software\EarfunTools');
  DeleteFile(ExpandConstant('{commonprograms}\Earfun Tools.lnk'));
  DeleteFile(ExpandConstant('{commondesktop}\Earfun Tools.lnk'));
  RegDeleteValue(HKCU, '{#RUNKEY}', 'Earfun Tools');
end;

procedure PreserveLegacyStartupSetting;
var
  StartupCommand: String;
begin
  if LegacyStartupCommand = '' then
    Exit;

  if WizardIsTaskSelected('startwithwindows') or
    (Pos('--tray', LegacyStartupCommand) > 0) then
    StartupCommand := '"' + ExpandConstant('{app}\{#EXENAME}') + '" --tray'
  else
    StartupCommand := '"' + ExpandConstant('{app}\{#EXENAME}') + '"';
  RegWriteStringValue(HKCU, '{#RUNKEY}', '{#APPNAME}', StartupCommand);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#EXENAME}', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM EarfunTools.exe', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    RemoveLegacyInstallation;

    if not IsRuntimeInstalled then
    begin
      ExtractTemporaryFile('windowsdesktop-runtime-win-x64.exe');
      if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'),
        '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        ResultCode := -1;
      DeleteFile(ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'));
      if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) then
        MsgBox('The .NET 10 Desktop Runtime installation failed (code ' +
          IntToStr(ResultCode) + ').' + #13#10 +
          'You can install .NET Desktop Runtime 10 (x64) later from ' +
          'https://dotnet.microsoft.com/download/dotnet/10.0.',
          mbError, MB_OK);
    end;

    if IsUpgrade then
    begin
      DeleteMatchingFiles(ExpandConstant('{app}\*.dll'));
      DeleteMatchingFiles(ExpandConstant('{app}\*.exe'));
      DeleteMatchingFiles(ExpandConstant('{app}\*.json'));
      DeleteMatchingFiles(ExpandConstant('{app}\*.pdb'));
      if DirExists(ExpandConstant('{app}\runtimes')) and
        not DelTree(ExpandConstant('{app}\runtimes'), True, True, True) then
        RaiseException('Could not remove the previous application runtime files.');
    end;
  end
  else if CurStep = ssPostInstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKLM, '{#UNINSTKEY}');
    if IsUpgrade then
    begin
      if not WizardIsTaskSelected('startwithwindows') then
        RegDeleteValue(HKCU, '{#RUNKEY}', '{#APPNAME}');
    end
    else
      PreserveLegacyStartupSetting;
  end;
end;

function InitializeUninstall: Boolean;
var
  UserDataDir: String;
begin
  Result := True;
  RemoveUserData := False;
  UserDataDir := ExpandConstant('{userappdata}\EarKit');
  if not UninstallSilent and DirExists(UserDataDir) then
    RemoveUserData := MsgBox('Would you also like to delete your personal settings and custom EQ presets?' + #13#10 +
      '(' + UserDataDir + ')', mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#EXENAME}', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end
  else if (CurUninstallStep = usPostUninstall) and RemoveUserData then
    if not DelTree(ExpandConstant('{userappdata}\EarKit'), True, True, True) then
      MsgBox('Some personal EarKit data could not be deleted.', mbError, MB_OK);
end;
