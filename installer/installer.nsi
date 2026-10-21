; Play CosmicReload installer (NSIS 3, Modern UI 2). Built by ..\build.ps1:
;   makensis /DVERSION=<x.y.z> /DAPP_EXE=<path to the built app> installer.nsi
; Per-user install (no admin): %LOCALAPPDATA%\Programs\Play CosmicReload, Start menu shortcut, optional desktop
; shortcut, listed in Settings > Apps with an uninstaller.
Unicode true
ManifestDPIAware true

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef APP_EXE
  !define APP_EXE "..\build\Play CosmicReload.exe"
!endif
!define APP "Play CosmicReload"
!define EXE "Play CosmicReload.exe"
!define PUBLISHER "CosmicReload"
!define SITE "https://cosmicreload.com"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PlayCosmicReload"

Name "${APP}"
OutFile "..\dist\PlayCosmicReloadSetup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${APP}"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${PUBLISHER}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 ${PUBLISHER}"

; ---------------------------------------------------------------- look
!define MUI_ICON "..\app\icon.ico"
!define MUI_UNICON "..\app\icon.ico"
!define MUI_ABORTWARNING
!define MUI_BGCOLOR "0B0B12"
!define MUI_TEXTCOLOR "F2F2F7"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "welcome.bmp"

; ---------------------------------------------------------------- pages
!define MUI_WELCOMEPAGE_TITLE "Welcome to ${APP}"
!define MUI_WELCOMEPAGE_TEXT "This installs ${APP} ${VERSION}.$\r$\n$\r$\nIt opens the official Cosmic Client with CosmicReload support, so item skins and Cosmic features work on play.cosmicreload.com.$\r$\n$\r$\nNo admin rights are needed and none of the Cosmic Client's files are changed.$\r$\n$\r$\nClick Next to continue."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "${APP} is ready"
!define MUI_FINISHPAGE_TEXT "Open ${APP} and press Play to start the Cosmic Client with CosmicReload support."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Open ${APP}"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Create a desktop shortcut"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION DesktopShortcut
!define MUI_FINISHPAGE_LINK "cosmicreload.com"
!define MUI_FINISHPAGE_LINK_LOCATION "${SITE}"
!define MUI_FINISHPAGE_LINK_COLOR "55FFFF"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---------------------------------------------------------------- helpers
; ask a running copy of the app to close (it would lock its exe)
!macro CLOSE_APP un
  Function ${un}CloseApp
    StrCpy $1 0
    loop:
      FindWindow $0 "" "${APP}"
      StrCmp $0 0 done
      SendMessage $0 ${WM_CLOSE} 0 0
      Sleep 300
      IntOp $1 $1 + 1
      IntCmp $1 20 done loop done
    done:
  FunctionEnd
!macroend
!insertmacro CLOSE_APP ""
!insertmacro CLOSE_APP "un."

Function DesktopShortcut
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}"
FunctionEnd

; ---------------------------------------------------------------- install
Section "Install"
  Call CloseApp
  SetOutPath "$INSTDIR"
  File "/oname=${EXE}" "${APP_EXE}"
  File "/oname=LICENSE.txt" "..\LICENSE"
  File "/oname=LICENSE-ASM.txt" "..\LICENSE-ASM.txt"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${EXE}"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "${SITE}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" $0

  ; started by the app's updater ("/S /UPDATE"): reopen the new version
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/UPDATE" $R1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\${EXE}"'
  ${EndIf}
SectionEnd

; ---------------------------------------------------------------- uninstall
Section "Uninstall"
  Call un.CloseApp
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\LICENSE-ASM.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$DESKTOP\${APP}.lnk"
  ; the agent the app unpacks (the folder only goes if nothing else is in it)
  Delete "$LOCALAPPDATA\CosmicReload\cosmic-agent.jar"
  RMDir "$LOCALAPPDATA\CosmicReload"
  DeleteRegKey HKCU "${UNINST_KEY}"
SectionEnd
