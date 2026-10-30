; EarKit installer (NSIS 3, Unicode)
Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma

!define APPNAME   "EarKit"
!define EXENAME   "EarKit.exe"
!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!define PUBLISHER "EarKit contributors"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EarKit"
!define LEGACYKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EarfunTools"
!define RUNKEY    "Software\Microsoft\Windows\CurrentVersion\Run"
!ifndef PUBLISHDIR
  !define PUBLISHDIR "..\publish"
!endif
!ifndef REDIST
  !define REDIST "..\redist\windowsdesktop-runtime-win-x64.exe"
!endif
!ifndef OUTFILE
  !define OUTFILE "EarKit_Setup.exe"
!endif

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"
!include "StrFunc.nsh"
!include "WordFunc.nsh"
${StrStr}

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "Software\EarKit" "InstallDir"
RequestExecutionLevel admin
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APPNAME} Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 EarKit contributors · MIT"

Var NeedRuntime
Var IsUpgrade
Var OldVersion
Var UpgradeNote

!define MUI_ICON "..\src\EarKit\Assets\app.ico"
!define MUI_UNICON "..\src\EarKit\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Run ${APPNAME} now"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!define MUI_WELCOMEPAGE_TEXT "${APPNAME} ${VERSION} will be installed on your computer.$\r$\n$\r$\nEarKit is an open-source, third-party headset control app for compatible EarFun headsets, with noise control, an equalizer, custom controls, multipoint, and more. EarKit is not affiliated with EarFun.$\r$\n$\r$\nIf the .NET 10 Desktop Runtime is missing, Setup will install it for you.$\r$\n$\r$\n$UpgradeNote"
!insertmacro MUI_PAGE_WELCOME
!define MUI_LICENSEPAGE_TEXT_TOP "Please review the following license and notices, including Microsoft's license terms for the bundled .NET runtime."
!insertmacro MUI_PAGE_LICENSE "License.txt"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirOnUpgrade
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"


Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APPNAME} supports 64-bit Windows 10 and 11 only."
    Abort
  ${EndIf}
  SetRegView 64
  ; Allow only one installer instance.
  System::Call 'kernel32::CreateMutex(p 0, i 0, t "EarKitSetupMutex") p .r1 ?e'
  Pop $R0
  ${If} $R0 == 183
    MessageBox MB_ICONEXCLAMATION "The installer is already running."
    Abort
  ${EndIf}
  Call DetectExisting
FunctionEnd

Function SkipDirOnUpgrade
  ${If} $IsUpgrade == 1
    Abort
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

; Check for the .NET 10 Desktop Runtime. $NeedRuntime = 1 means it must be installed.
Function CheckRuntime
  StrCpy $NeedRuntime 1
  ; 1) Registry
  StrCpy $0 0
  loop:
    EnumRegValue $1 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" $0
    StrCmp $1 "" regdone
    StrCpy $2 $1 3
    ${If} $2 == "10."
      StrCpy $NeedRuntime 0
      Goto done
    ${EndIf}
    IntOp $0 $0 + 1
    Goto loop
  regdone:
  ; 2) Directory
  FindFirst $3 $4 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
  ${If} $4 != ""
    StrCpy $NeedRuntime 0
  ${EndIf}
  FindClose $3
  done:
FunctionEnd

; Remove the old “Earfun Tools” installation. Keep user data for migration on first launch.
Function RemoveLegacy
  ; Preserve the old startup setting under the new name.
  ReadRegStr $1 HKCU "${RUNKEY}" "Earfun Tools"
  ReadRegStr $0 HKLM "${LEGACYKEY}" "InstallLocation"
  ${If} $0 != ""
  ${AndIf} ${FileExists} "$0\Uninstall.exe"
    DetailPrint "Removing the old Earfun Tools installation..."
    ExecWait '"$0\Uninstall.exe" /S _?=$0'
    RMDir /r "$0"
  ${EndIf}
  DeleteRegKey HKLM "${LEGACYKEY}"
  DeleteRegKey HKLM "Software\EarfunTools"
  SetShellVarContext all
  Delete "$SMPROGRAMS\Earfun Tools.lnk"
  Delete "$DESKTOP\Earfun Tools.lnk"
  DeleteRegValue HKCU "${RUNKEY}" "Earfun Tools"
  ${If} $1 != ""
    ${StrStr} $2 $1 "--tray"
    ${If} $2 != ""
      WriteRegStr HKCU "${RUNKEY}" "${APPNAME}" '"$INSTDIR\${EXENAME}" --tray'
    ${Else}
      WriteRegStr HKCU "${RUNKEY}" "${APPNAME}" '"$INSTDIR\${EXENAME}"'
    ${EndIf}
  ${EndIf}
FunctionEnd

Function LaunchApp
  ; Launch through Explorer at normal privilege so the app does not inherit administrator rights.
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXENAME}"'
FunctionEnd

Section "${APPNAME} application" SecMain
  SectionIn RO
  SetShellVarContext all
  nsExec::Exec 'taskkill /F /IM ${EXENAME}'
  Pop $0
  nsExec::Exec 'taskkill /F /IM EarfunTools.exe'
  Pop $0
  Sleep 500
  Call RemoveLegacy

  Call CheckRuntime
  ${If} $NeedRuntime == 1
    DetailPrint "Installing the .NET 10 Desktop Runtime. Please wait..."
    InitPluginsDir
    InitPluginsDir
    SetOutPath "$PLUGINSDIR"
    File /oname=dotnet-desktop-runtime.exe "${REDIST}"
    ExecWait '"$PLUGINSDIR\dotnet-desktop-runtime.exe" /install /quiet /norestart' $0
    Delete "$PLUGINSDIR\dotnet-desktop-runtime.exe"
    ${If} $0 != 0
    ${AndIf} $0 != 3010
    ${AndIf} $0 != 1641
      MessageBox MB_ICONEXCLAMATION "The .NET 10 Desktop Runtime installation failed (code $0).$\r$\nYou can install .NET Desktop Runtime 10 (x64) later from https://dotnet.microsoft.com/download/dotnet/10.0."
    ${Else}
      DetailPrint ".NET 10 Desktop Runtime installation complete."
    ${EndIf}
  ${Else}
    DetailPrint ".NET 10 Desktop Runtime detected; skipping installation."
  ${EndIf}

  ${If} $IsUpgrade == 1
    DetailPrint "Upgrading: removing old application files (settings will be kept)..."
    Delete "$INSTDIR\*.dll"
    Delete "$INSTDIR\*.exe"
    Delete "$INSTDIR\*.json"
    Delete "$INSTDIR\*.pdb"
    RMDir /r "$INSTDIR\runtimes"
  ${EndIf}
  SetOutPath "$INSTDIR"
  File /r "${PUBLISHDIR}\*.*"
  File "..\src\EarKit\Assets\app.ico"
  File /oname=LICENSE.txt "..\LICENSE"
  File "..\THIRD-PARTY-NOTICES.txt"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\EarKit" "InstallDir" "$INSTDIR"
  !ifdef PROJECTURL
  WriteRegStr HKLM "${UNINSTKEY}" "URLInfoAbout" "${PROJECTURL}"
  !endif
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINSTKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Start menu shortcut" SecStart
  SetShellVarContext all
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
SectionEnd

Section "Desktop shortcut" SecDesk
  SetShellVarContext all
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
SectionEnd

Section /o "Start with Windows (minimized to tray)" SecAuto
  ; Preserve the existing start-minimized preference during upgrades.
  ReadRegStr $3 HKCU "${RUNKEY}" "${APPNAME}"
  ${If} $3 != ""
    ${StrStr} $4 $3 "--tray"
    ${If} $4 == ""
      WriteRegStr HKCU "${RUNKEY}" "${APPNAME}" '"$INSTDIR\${EXENAME}"'
      Return
    ${EndIf}
  ${EndIf}
  WriteRegStr HKCU "${RUNKEY}" "${APPNAME}" '"$INSTDIR\${EXENAME}" --tray'
SectionEnd

; Remove old shortcuts or startup entries for components deselected during an upgrade.
Section "-Post"
  SetShellVarContext all
  ${IfNot} ${SectionIsSelected} ${SecStart}
    Delete "$SMPROGRAMS\${APPNAME}.lnk"
  ${EndIf}
  ${IfNot} ${SectionIsSelected} ${SecDesk}
    Delete "$DESKTOP\${APPNAME}.lnk"
  ${EndIf}
  ${IfNot} ${SectionIsSelected} ${SecAuto}
    ${If} $IsUpgrade == 1
      DeleteRegValue HKCU "${RUNKEY}" "${APPNAME}"
    ${EndIf}
  ${EndIf}
SectionEnd

; Detect an existing EarKit installation, reuse its location, and preselect components from its current state.
Function DetectExisting
  StrCpy $IsUpgrade 0
  StrCpy $UpgradeNote "Click Next to continue."
  ReadRegStr $0 HKLM "${UNINSTKEY}" "InstallLocation"
  ReadRegStr $OldVersion HKLM "${UNINSTKEY}" "DisplayVersion"
  ${If} $0 == ""
    Return
  ${EndIf}
  ${IfNot} ${FileExists} "$0\${EXENAME}"
    Return
  ${EndIf}
  StrCpy $IsUpgrade 1
  StrCpy $INSTDIR $0
  ${If} $OldVersion == ""
    StrCpy $OldVersion "Unknown version"
  ${EndIf}

  ${VersionCompare} "$OldVersion" "${VERSION}" $1
  ${If} $1 == 0
    StrCpy $UpgradeNote "The same version of ${APPNAME} ($OldVersion) is already installed. Setup will reinstall it; this can be used to repair the installation.$\r$\n"
  ${ElseIf} $1 == 2
    StrCpy $UpgradeNote "${APPNAME} $OldVersion is already installed. Setup will upgrade to ${VERSION}.$\r$\n"
  ${Else}
    StrCpy $UpgradeNote "A newer version of ${APPNAME} ($OldVersion) is installed. Continuing will downgrade to ${VERSION}.$\r$\n"
    ${IfNot} ${Silent}
      MessageBox MB_YESNO|MB_ICONEXCLAMATION "The installed version of ${APPNAME} ($OldVersion) is newer than this installer (${VERSION}).$\r$\nAre you sure you want to downgrade?" IDYES downgrade_ok
      Abort
      downgrade_ok:
    ${EndIf}
  ${EndIf}
  StrCpy $5 "Install location: $INSTDIR$\r$\nYour settings and custom presets will be kept.$\r$\n$\r$\nClick Next to begin the upgrade."
  StrCpy $UpgradeNote "$UpgradeNote$5"

  ; Preselect components based on the current installation.
  SetShellVarContext all
  ${If} ${FileExists} "$SMPROGRAMS\${APPNAME}.lnk"
    !insertmacro SelectSection ${SecStart}
  ${Else}
    !insertmacro UnselectSection ${SecStart}
  ${EndIf}
  ${If} ${FileExists} "$DESKTOP\${APPNAME}.lnk"
    !insertmacro SelectSection ${SecDesk}
  ${Else}
    !insertmacro UnselectSection ${SecDesk}
  ${EndIf}
  ReadRegStr $2 HKCU "${RUNKEY}" "${APPNAME}"
  ${If} $2 != ""
    !insertmacro SelectSection ${SecAuto}
  ${Else}
    !insertmacro UnselectSection ${SecAuto}
  ${EndIf}
FunctionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}  "The application. Installs the .NET 10 Desktop Runtime if it is missing."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStart} "Create a shortcut in the Start menu."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesk}  "Create a shortcut on the desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAuto}  "Start minimized to the tray and connect to the headset after signing in to Windows. You can change this later in Application Settings."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetShellVarContext all
  nsExec::Exec 'taskkill /F /IM ${EXENAME}'
  Pop $0
  Sleep 500
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  DeleteRegValue HKCU "${RUNKEY}" "${APPNAME}"
  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "Software\EarKit"
  RMDir /r "$INSTDIR"
  SetShellVarContext current
  IfSilent skipdata
  IfFileExists "$APPDATA\EarKit\*.*" 0 skipdata
    MessageBox MB_YESNO|MB_ICONQUESTION "Would you also like to delete your personal settings and custom EQ presets?$\r$\n($APPDATA\EarKit)" IDNO skipdata
    RMDir /r "$APPDATA\EarKit"
  skipdata:
SectionEnd
