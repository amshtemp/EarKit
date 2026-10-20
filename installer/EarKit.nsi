; EarKit 安装程序 (NSIS 3, Unicode)
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
VIAddVersionKey /LANG=2052 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APPNAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright (c) 2026 EarKit contributors · MIT"

Var NeedRuntime
Var IsUpgrade
Var OldVersion
Var UpgradeNote

!define MUI_ICON "..\src\EarKit\Assets\app.ico"
!define MUI_UNICON "..\src\EarKit\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APPNAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!define MUI_WELCOMEPAGE_TEXT "将在你的电脑上安装 ${APPNAME} ${VERSION}。$\r$\n$\r$\n这是一款开源的第三方耳机控制工具，兼容 EarFun 耳机（降噪、均衡器、按键、多设备连接等）。本软件与 EarFun 公司无关。$\r$\n$\r$\n如电脑缺少 .NET 10 桌面运行时，安装程序会自动为你安装。$\r$\n$\r$\n$UpgradeNote"
!insertmacro MUI_PAGE_WELCOME
!define MUI_LICENSEPAGE_TEXT_TOP "请阅读以下许可与用户须知（含内置 .NET 运行时的 Microsoft 许可条款）。"
!insertmacro MUI_PAGE_LICENSE "License.txt"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirOnUpgrade
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"


Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APPNAME} 仅支持 64 位 Windows 10/11。"
    Abort
  ${EndIf}
  SetRegView 64
  ; 单实例安装
  System::Call 'kernel32::CreateMutex(p 0, i 0, t "EarKitSetupMutex") p .r1 ?e'
  Pop $R0
  ${If} $R0 == 183
    MessageBox MB_ICONEXCLAMATION "安装程序已在运行。"
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

; 检测 .NET 10 桌面运行时 -> $NeedRuntime = 1 表示需要安装
Function CheckRuntime
  StrCpy $NeedRuntime 1
  ; 1) 注册表
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
  ; 2) 文件夹
  FindFirst $3 $4 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
  ${If} $4 != ""
    StrCpy $NeedRuntime 0
  ${EndIf}
  FindClose $3
  done:
FunctionEnd

; 卸载旧名称 “Earfun Tools” 的安装（保留用户数据，由程序首次启动时自动迁移）
Function RemoveLegacy
  ; 记住旧版的开机自启设置，迁移到新名称
  ReadRegStr $1 HKCU "${RUNKEY}" "Earfun Tools"
  ReadRegStr $0 HKLM "${LEGACYKEY}" "InstallLocation"
  ${If} $0 != ""
  ${AndIf} ${FileExists} "$0\Uninstall.exe"
    DetailPrint "正在移除旧版 Earfun Tools……"
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
  ; 通过 explorer 以普通权限启动，避免程序继承管理员权限
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXENAME}"'
FunctionEnd

Section "${APPNAME} 主程序" SecMain
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
    DetailPrint "正在安装 .NET 10 桌面运行时，请稍候……"
    InitPluginsDir
    InitPluginsDir
    SetOutPath "$PLUGINSDIR"
    File /oname=dotnet-desktop-runtime.exe "${REDIST}"
    ExecWait '"$PLUGINSDIR\dotnet-desktop-runtime.exe" /install /quiet /norestart' $0
    Delete "$PLUGINSDIR\dotnet-desktop-runtime.exe"
    ${If} $0 != 0
    ${AndIf} $0 != 3010
    ${AndIf} $0 != 1641
      MessageBox MB_ICONEXCLAMATION ".NET 10 桌面运行时安装失败（代码 $0）。$\r$\n可稍后手动从 https://dotnet.microsoft.com/download/dotnet/10.0 安装 “.NET Desktop Runtime 10 (x64)”。"
    ${Else}
      DetailPrint ".NET 10 桌面运行时安装完成。"
    ${EndIf}
  ${Else}
    DetailPrint "已检测到 .NET 10 桌面运行时，跳过安装。"
  ${EndIf}

  ${If} $IsUpgrade == 1
    DetailPrint "正在覆盖安装：移除旧版程序文件（保留设置）……"
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

Section "开始菜单快捷方式" SecStart
  SetShellVarContext all
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
SectionEnd

Section "桌面快捷方式" SecDesk
  SetShellVarContext all
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
SectionEnd

Section /o "开机自动启动（最小化到托盘）" SecAuto
  ; 覆盖安装时保留原来“是否最小化到托盘”的选择
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

; 覆盖安装时，未勾选的项目要移除旧的快捷方式 / 自启项
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

; 检测已安装的 EarKit：沿用原安装位置，并按现有状态预选组件
Function DetectExisting
  StrCpy $IsUpgrade 0
  StrCpy $UpgradeNote "点击“下一步”继续。"
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
    StrCpy $OldVersion "未知版本"
  ${EndIf}

  ${VersionCompare} "$OldVersion" "${VERSION}" $1
  ${If} $1 == 0
    StrCpy $UpgradeNote "检测到已安装相同版本 ${APPNAME} $OldVersion，将覆盖安装（可用于修复）。$\r$\n"
  ${ElseIf} $1 == 2
    StrCpy $UpgradeNote "检测到已安装 ${APPNAME} $OldVersion，将升级到 ${VERSION}。$\r$\n"
  ${Else}
    StrCpy $UpgradeNote "检测到已安装较新的 ${APPNAME} $OldVersion，继续将降级为 ${VERSION}。$\r$\n"
    ${IfNot} ${Silent}
      MessageBox MB_YESNO|MB_ICONEXCLAMATION "已安装的 ${APPNAME} $OldVersion 比本安装包（${VERSION}）更新。$\r$\n确定要降级安装吗？" IDYES downgrade_ok
      Abort
      downgrade_ok:
    ${EndIf}
  ${EndIf}
  StrCpy $5 "安装位置：$INSTDIR$\r$\n你的设置和自定义预设会保留。$\r$\n$\r$\n点击“下一步”开始覆盖安装。"
  StrCpy $UpgradeNote "$UpgradeNote$5"

  ; 按现有状态预选组件
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
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}  "程序本体（如缺少 .NET 10 桌面运行时会自动安装）。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStart} "在开始菜单中创建快捷方式。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesk}  "在桌面创建快捷方式。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAuto}  "登录 Windows 后自动在托盘中启动，并自动连接耳机。之后也可在“应用设置”中更改。"
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
    MessageBox MB_YESNO|MB_ICONQUESTION "是否同时删除个人设置和自定义均衡器预设？$\r$\n（$APPDATA\EarKit）" IDNO skipdata
    RMDir /r "$APPDATA\EarKit"
  skipdata:
SectionEnd
