# -*- coding: utf-8 -*-
Unicode true
ManifestDPIAware true
RequestExecutionLevel user
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"
!include "WinVer.nsh"

Name "GhostSlacking ${DISPLAY_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\GhostSlacking"
InstallDirRegKey HKCU "Software\GhostSlacking" "InstallRoot"
VIProductVersion "${FILE_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "GhostSlacking"
VIAddVersionKey /LANG=1033 "FileDescription" "GhostSlacking current-user installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${DISPLAY_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${DISPLAY_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "GhostSlacking contributors"
!define MUI_ICON "${APP_ICON}"
!define MUI_ABORTWARNING
!define MUI_CUSTOMFUNCTION_ABORT PreventAbortDuringReplacement
!define MUI_CUSTOMFUNCTION_UNABORT un.PreventAbortDuringReplacement
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfPassive
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirectoryForUpgrade
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDirectory
!insertmacro MUI_PAGE_DIRECTORY
Page custom ShortcutPage ShortcutPageLeave
!insertmacro MUI_PAGE_INSTFILES
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfPassive
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\GhostSlacking.App.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

LangString LegacyMsi ${LANG_SIMPCHINESE} "检测到旧 MSI 安装。请先安全退出 GhostSlacking，并在 Windows 的已安装应用中卸载所有旧版本，再运行此 EXE。用户设置和日志将保留。"
LangString LegacyMsi ${LANG_ENGLISH} "A legacy MSI installation exists. Exit GhostSlacking safely and uninstall the old versions in Windows Installed apps, then run this EXE. Settings and logs are retained."
LangString RuntimeMissing ${LANG_SIMPCHINESE} "需要系统 .NET 8 Runtime x64，安装器不会自动下载。是否打开微软官方下载页面？安装运行时后请重试。"
LangString RuntimeMissing ${LANG_ENGLISH} "System .NET 8 Runtime x64 is required. Open Microsoft's download page? Install the runtime and retry."
LangString Failed ${LANG_SIMPCHINESE} "操作未完成。程序文件将保留或回退到旧版本；请查看以下诊断，并安全退出程序后重试。"
LangString Failed ${LANG_ENGLISH} "The operation did not complete. Program files are retained or restored. Review the diagnostic below and retry after closing the application safely."
LangString Concurrent ${LANG_SIMPCHINESE} "另一个安装或卸载正在操作此目录，请等待完成后重试。"
LangString Concurrent ${LANG_ENGLISH} "Another installation or removal is using this directory. Wait and retry."
LangString ChooseShortcuts ${LANG_SIMPCHINESE} "选择当前用户的快捷方式"
LangString ChooseShortcuts ${LANG_ENGLISH} "Choose shortcuts for the current user"
LangString StartMenu ${LANG_SIMPCHINESE} "开始菜单快捷方式"
LangString StartMenu ${LANG_ENGLISH} "Start menu shortcut"
LangString Desktop ${LANG_SIMPCHINESE} "桌面快捷方式"
LangString Desktop ${LANG_ENGLISH} "Desktop shortcut"
LangString Closing ${LANG_SIMPCHINESE} "正在安全关闭应用并检查安装状态（关闭最多等待 30 秒）"
LangString Closing ${LANG_ENGLISH} "Closing the application safely and checking installation state (up to 30 seconds)"
LangString Extracting ${LANG_SIMPCHINESE} "正在准备完整的新版本程序"
LangString Extracting ${LANG_ENGLISH} "Preparing the complete new application"
LangString Switching ${LANG_SIMPCHINESE} "正在替换程序并保存安装信息"
LangString Switching ${LANG_ENGLISH} "Replacing the application and saving installation details"

Var Passive
Var Update
Var PreviousRoot
Var MenuChoice
Var DesktopChoice
Var MenuBox
Var DesktopBox
Var SetupMutex
Var Helper
Var Replacing

Function PreventAbortDuringReplacement
  ${If} $Replacing == 1
    Abort
  ${EndIf}
FunctionEnd
Function un.PreventAbortDuringReplacement
  ${If} $Replacing == 1
    Abort
  ${EndIf}
FunctionEnd

!macro ExtractHelper
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=GhostSlacking.InstallHelper.exe "${HELPER_FILE}"
  StrCpy $Helper "$PLUGINSDIR\GhostSlacking.InstallHelper.exe"
!macroend

Function .onInit
  SetShellVarContext current
  StrCpy $MenuChoice 1
  StrCpy $DesktopChoice 0
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/PASSIVE" $1
  ${IfNot} ${Errors}
    StrCpy $Passive 1
  ${EndIf}
  ClearErrors
  ${GetOptions} $0 "/UPDATE" $1
  ${IfNot} ${Errors}
    StrCpy $Update 1
  ${EndIf}
  !insertmacro ExtractHelper
  nsExec::ExecToStack '"$Helper" legacy'
  Pop $0
  Pop $1
  ${If} $0 == 10
    MessageBox MB_ICONSTOP "$(LegacyMsi)" /SD IDOK
    SetErrorLevel 10
    Quit
  ${ElseIf} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  nsExec::ExecToStack '"$Helper" runtime'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_YESNO "$(RuntimeMissing)" /SD IDNO IDNO runtimeNotOpened
    ExecShell "open" "https://dotnet.microsoft.com/download/dotnet/8.0"
    runtimeNotOpened:
    SetErrorLevel 11
    Quit
  ${EndIf}
  ReadRegStr $PreviousRoot HKCU "Software\GhostSlacking" "InstallRoot"
  ${If} $PreviousRoot != ""
    StrCpy $INSTDIR $PreviousRoot
    ReadRegDWORD $MenuChoice HKCU "Software\GhostSlacking" "StartMenuShortcut"
    ReadRegDWORD $DesktopChoice HKCU "Software\GhostSlacking" "DesktopShortcut"
  ${EndIf}
  ${If} $Update == 1
  ${AndIf} $PreviousRoot == ""
    SetErrorLevel 20
    Quit
  ${EndIf}
FunctionEnd

Function SkipIfPassive
  ${If} $Passive == 1
    Abort
  ${EndIf}
FunctionEnd
Function SkipDirectoryForUpgrade
  ${If} $PreviousRoot != ""
    Abort
  ${EndIf}
  Call SkipIfPassive
FunctionEnd
Function ValidateDirectory
  nsExec::ExecToStack '"$Helper" validate "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1"
    Abort
  ${EndIf}
FunctionEnd
Function ShortcutPage
  ${If} $Passive == 1
  ${OrIf} $PreviousRoot != ""
    Abort
  ${EndIf}
  !insertmacro MUI_HEADER_TEXT "$(ChooseShortcuts)" "GhostSlacking"
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateCheckbox} 0 10u 100% 16u "$(StartMenu)"
  Pop $MenuBox
  ${NSD_SetState} $MenuBox $MenuChoice
  ${NSD_CreateCheckbox} 0 40u 100% 16u "$(Desktop)"
  Pop $DesktopBox
  ${NSD_SetState} $DesktopBox $DesktopChoice
  nsDialogs::Show
FunctionEnd
Function ShortcutPageLeave
  ${NSD_GetState} $MenuBox $MenuChoice
  ${NSD_GetState} $DesktopBox $DesktopChoice
FunctionEnd

!macro LockDirectory
  nsExec::ExecToStack '"$Helper" id "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\GhostSlacking.Setup.$1") p .r2'
  StrCpy $SetupMutex $2
  System::Call 'kernel32::WaitForSingleObject(p $SetupMutex, i 0) i .r2'
  ${If} $2 != 0
  ${AndIf} $2 != 128
    MessageBox MB_ICONSTOP "$(Concurrent)" /SD IDOK
    SetErrorLevel 21
    Quit
  ${EndIf}
!macroend

Section "Install"
  !insertmacro LockDirectory
  DetailPrint "$(Closing)"
  nsExec::ExecToStack '"$Helper" recover "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  GetDlgItem $0 $HWNDPARENT 2
  EnableWindow $0 0
  StrCpy $Replacing 1
  CreateDirectory "$INSTDIR"
  nsExec::ExecToStack '"$Helper" stage "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  SetOutPath "$INSTDIR\.staging"
  DetailPrint "$(Extracting)"
  System::Call 'kernel32::GetTickCount() i .r4'
  ClearErrors
  File /r "${PUBLISH_DIRECTORY}\*"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(Failed)" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  WriteUninstaller "$INSTDIR\uninstall-new.exe"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(Failed)" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  System::Call 'kernel32::GetTickCount() i .r5'
  IntOp $5 $5 - $4
  DetailPrint "InstallationPhase extraction elapsedMs=$5"
  CreateDirectory "$LOCALAPPDATA\GhostSlacking\logs"
  FileOpen $4 "$LOCALAPPDATA\GhostSlacking\logs\installer.log" a
  FileWrite $4 "InstallationPhase extraction elapsedMs=$5$\r$\n"
  FileClose $4
  SetOutPath "$PLUGINSDIR"
  DetailPrint "$(Switching)"
  nsExec::ExecToStack '"$Helper" install "$INSTDIR" "${DISPLAY_VERSION}" "$MenuChoice" "$DesktopChoice"'
  Pop $0
  Pop $1
  DetailPrint "$1"
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  SetErrorLevel 0
  StrCpy $Replacing 0
SectionEnd

Function un.onInit
  SetShellVarContext current
  !insertmacro ExtractHelper
FunctionEnd
Section "Uninstall"
  !insertmacro LockDirectory
  GetDlgItem $0 $HWNDPARENT 2
  EnableWindow $0 0
  StrCpy $Replacing 1
  nsExec::ExecToStack '"$Helper" uninstall "$INSTDIR"'
  Pop $0
  Pop $1
  DetailPrint "$1"
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(Failed)$\r$\n$1" /SD IDOK
    SetErrorLevel 20
    Quit
  ${EndIf}
  SetOutPath "$TEMP"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
  SetErrorLevel 0
  StrCpy $Replacing 0
SectionEnd
