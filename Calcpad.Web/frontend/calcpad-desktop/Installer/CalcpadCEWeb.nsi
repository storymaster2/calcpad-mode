!include "MUI2.nsh"
!include "FileFunc.nsh"

;--------------------------------
; General

!define APP_NAME "CalcpadCE Web"
!define APP_SHORT_NAME "CalcpadCEWeb"
!ifndef APP_VERSION
  !define APP_VERSION "dev"
!endif
!define APP_PUBLISHER "CalcpadCE Community"
!define APP_URL "https://calcpad-ce.org/"
!define APP_EXE "CalcpadCEWeb.exe"
!define NEUTRALINO_EXE "calcpad-desktop-win_x64.exe"
!define PUBLISH_DIR "..\dist\calcpad-desktop"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "Output\CalcpadCEWeb-setup-${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${APP_SHORT_NAME}"
InstallDirRegKey HKCU "Software\${APP_SHORT_NAME}" "InstallPath"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Unicode True
ManifestDPIAware System

;--------------------------------
; Interface Settings

!define MUI_ICON "calcpad.ico"
!define MUI_UNICON "calcpad.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"

; Language dialog settings — remember choice in registry for upgrades
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "Software\${APP_SHORT_NAME}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"

;--------------------------------
; Pages

!insertmacro MUI_PAGE_LICENSE "..\..\..\..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
; Languages

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Bulgarian"
!insertmacro MUI_LANGUAGE "SimpChinese"

;--------------------------------
; Installer init — show language selection dialog

Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

;--------------------------------
; Install Section

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  ; Rename Neutralino binary to a friendly app name
  Rename "$INSTDIR\${NEUTRALINO_EXE}" "$INSTDIR\${APP_EXE}"

  ; Create Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  ; Create Desktop shortcut
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

  ; Write install path to registry
  WriteRegStr HKCU "Software\${APP_SHORT_NAME}" "InstallPath" "$INSTDIR"
  WriteRegStr HKCU "Software\${APP_SHORT_NAME}" "Version" "${APP_VERSION}"

  ; Map NSIS language ID to app culture string and write to registry
  StrCmp $LANGUAGE 1026 0 +3
    WriteRegStr HKCU "Software\${APP_SHORT_NAME}" "Language" "bg"
    Goto lang_done
  StrCmp $LANGUAGE 2052 0 +3
    WriteRegStr HKCU "Software\${APP_SHORT_NAME}" "Language" "zh"
    Goto lang_done
  WriteRegStr HKCU "Software\${APP_SHORT_NAME}" "Language" "en"
  lang_done:

  ; Write uninstall information
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "URLInfoAbout" "${APP_URL}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "URLUpdateInfo" "https://github.com/imartincei/CalcpadCE"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "NoRepair" 1

  ; Estimate installed size (KB)
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}" "EstimatedSize" $0
SectionEnd

;--------------------------------
; Uninstaller init — restore language for uninstall UI

Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd

;--------------------------------
; Uninstall Section

Section "Uninstall"
  ; Remove files and directories
  RMDir /r "$INSTDIR"

  ; Remove Start Menu shortcuts
  RMDir /r "$SMPROGRAMS\${APP_NAME}"

  ; Remove Desktop shortcut
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; Remove registry entries
  DeleteRegKey HKCU "Software\${APP_SHORT_NAME}"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_SHORT_NAME}"
SectionEnd