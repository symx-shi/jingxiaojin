Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

!ifndef APP_EXE
  !error "Define APP_EXE to the compiled application path"
!endif
!ifndef ASSET_LICENSE
  !error "Define ASSET_LICENSE to the asset attribution path"
!endif
!ifndef OUTPUT_EXE
  !error "Define OUTPUT_EXE to the output installer path"
!endif
!define APP_VERSION "1.2.2"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Jingxiaojin"

Name "鲸小金"
OutFile "${OUTPUT_EXE}"
InstallDir "$LOCALAPPDATA\Programs\jingxiaojin"
InstallDirRegKey HKCU "Software\Jingxiaojin" "InstallPath"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetDatablockOptimize on
VIProductVersion "1.2.2.0"
VIAddVersionKey /LANG=2052 "ProductName" "鲸小金安装程序"
VIAddVersionKey /LANG=2052 "FileDescription" "鲸小金安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "1.2.2"
VIAddVersionKey /LANG=2052 "LegalCopyright" "鲸小金 · 素材 CC BY-NC-SA 4.0"
!ifdef APP_ICON
  Icon "${APP_ICON}"
  UninstallIcon "${APP_ICON}"
!endif
!define MUI_ABORTWARNING
!define MUI_HEADER_TRANSPARENT_TEXT
!define MUI_FINISHPAGE_RUN "$INSTDIR\鲸小金.exe"
!define MUI_FINISHPAGE_RUN_TEXT "立即启动鲸小金"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\素材来源与许可.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "查看素材来源与许可"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_WELCOMEPAGE_TEXT "小金陪你看国际金价。$\r$\n$\r$\n下一步可选择快捷方式和安装目录。$\r$\n$\r$\n角色素材限非商业使用，许可随程序安装。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_DIRECTORYPAGE_TEXT_TOP "请选择安装位置。选择父目录后会自动补上 jingxiaojin 子目录。例如 D:\soft → D:\soft\jingxiaojin。"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE NormalizeDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"
LangString ^Font ${LANG_SIMPCHINESE} "Microsoft YaHei UI"
LangString ^FontSize ${LANG_SIMPCHINESE} "9"

Function .onInit
  SetShellVarContext current
  SetRegView 64
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "鲸小金需要64位 Windows。"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "鲸小金需要 Windows 10 或更新版本。"
    Abort
  ${EndIf}
  # A cross-format upgrade must not leave an old MSI registration pointing at new files.
  System::Call 'msi::MsiEnumRelatedProductsW(w "{D4EEAB35-222A-4EBD-A09C-7A55A230FF22}", i 0, i 0, w .r0) i .r1'
  ${If} $1 == 0
    MessageBox MB_ICONINFORMATION "检测到旧 MSI 安装版。请先退出小金，从 Windows 应用列表卸载旧版，再运行本安装程序。设置和提醒记录会保留。"
    Abort
  ${EndIf}
FunctionEnd

Function NormalizeDirectory
  # Directory-page Browse may append this leaf itself; avoid jingxiaojin\jingxiaojin.
  StrCpy $0 $INSTDIR 1 -1
  ${If} $0 == "\"
    StrCpy $INSTDIR $INSTDIR -1
  ${EndIf}
  StrCpy $0 $INSTDIR 12 -12
  ${If} $0 != "\jingxiaojin"
    StrCpy $INSTDIR "$INSTDIR\jingxiaojin"
  ${EndIf}
FunctionEnd

Function CloseInstalledApp
  ${If} ${FileExists} "$INSTDIR\鲸小金.exe"
    ClearErrors
    ExecWait '"$INSTDIR\鲸小金.exe" --shutdown-for-maintenance' $0
    ${If} ${Errors}
      MessageBox MB_ICONSTOP "无法关闭正在运行的小金。请先右键退出小金后重试。"
      Abort
    ${EndIf}
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "小金尚未退出。请先右键退出小金后重试。"
      Abort
    ${EndIf}
  ${EndIf}
FunctionEnd

Section "鲸小金（必选）" MainSection
  SectionIn RO
  Call NormalizeDirectory
  Call CloseInstalledApp
  SetOutPath "$INSTDIR"
  # Do not register an incomplete installation when the directory is unwritable.
  ClearErrors
  File /oname=鲸小金.exe "${APP_EXE}"
  File /oname=素材来源与许可.txt "${ASSET_LICENSE}"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "文件写入失败，请选择有写入权限的目录后重试。"
    Abort
  ${EndIf}
  WriteUninstaller "$INSTDIR\卸载鲸小金.exe"
  CreateDirectory "$SMPROGRAMS\鲸小金"
  CreateShortcut "$SMPROGRAMS\鲸小金\鲸小金.lnk" "$INSTDIR\鲸小金.exe" "" "$INSTDIR\鲸小金.exe" 0
  CreateShortcut "$SMPROGRAMS\鲸小金\卸载鲸小金.lnk" "$INSTDIR\卸载鲸小金.exe"
  WriteRegStr HKCU "Software\Jingxiaojin" "InstallPath" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "鲸小金"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "鲸小金"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\鲸小金.exe,0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\卸载鲸小金.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\卸载鲸小金.exe" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/symx-shi/jingxiaojin"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" 3000
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section /o "桌面快捷方式" DesktopSection
  CreateShortcut "$DESKTOP\鲸小金.lnk" "$INSTDIR\鲸小金.exe" "" "$INSTDIR\鲸小金.exe" 0
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "DesktopCreated" 1
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd

Section "Uninstall"
  FileOpen $9 "$TEMP\WhaleGold-uninstall.log" w
  FileWrite $9 "Uninstall target: $INSTDIR$\r$\n"
  ${If} ${FileExists} "$INSTDIR\鲸小金.exe"
    ClearErrors
    ExecWait '"$INSTDIR\鲸小金.exe" --shutdown-for-maintenance' $0
    FileWrite $9 "Shutdown result: $0$\r$\n"
    ${If} ${Errors}
      MessageBox MB_ICONSTOP "无法关闭小金。请先右键退出小金再卸载。"
      Abort
    ${EndIf}
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "小金尚未退出，请先右键退出小金再卸载。"
      Abort
    ${EndIf}
  ${EndIf}
  # Remove only this installation's autostart; a portable copy's entry is left alone.
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "GoldWidgetRMB"
  ${If} $0 == '"$INSTDIR\鲸小金.exe"'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "GoldWidgetRMB"
  ${EndIf}
  # CLR teardown may briefly hold the image after the app has released its mutex.
  StrCpy $1 0
  retryDelete:
  ClearErrors
  Delete "$INSTDIR\鲸小金.exe"
  ${If} ${Errors}
    IntOp $1 $1 + 1
    ${If} $1 < 30
      Sleep 100
      Goto retryDelete
    ${EndIf}
    FileWrite $9 "Application removal failed.$\r$\n"
    FileClose $9
    MessageBox MB_ICONSTOP "程序文件仍被占用，请退出小金后重试卸载。" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  FileWrite $9 "Application removed.$\r$\n"
  ClearErrors
  Delete "$INSTDIR\素材来源与许可.txt"
  ${If} ${Errors}
    FileWrite $9 "Asset attribution removal failed.$\r$\n"
    FileClose $9
    MessageBox MB_ICONSTOP "部分程序文件无法删除，请检查文件占用或目录权限后重试。" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKCU "${UNINSTALL_KEY}" "DesktopCreated"
  ${If} $0 == 1
    Delete "$DESKTOP\鲸小金.lnk"
  ${EndIf}
  Delete "$SMPROGRAMS\鲸小金\鲸小金.lnk"
  Delete "$SMPROGRAMS\鲸小金\卸载鲸小金.lnk"
  RMDir "$SMPROGRAMS\鲸小金"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegValue HKCU "Software\Jingxiaojin" "InstallPath"
  DeleteRegValue HKCU "Software\Jingxiaojin" "DesktopShortcut"
  DeleteRegKey /ifempty HKCU "Software\Jingxiaojin"
  Delete "$INSTDIR\卸载鲸小金.exe"
  # Non-recursive removal protects files added by the user. LocalAppData\GoldWidgetRMB is preserved.
  RMDir "$INSTDIR"
  FileWrite $9 "Uninstall completed.$\r$\n"
  FileClose $9
SectionEnd
