Unicode True
ManifestDPIAware True
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetShellVarContext current

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "StrFunc.nsh"
${StrStr}

!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.1.0-dev"
!endif
!ifndef PRODUCT_FILE_VERSION
  !define PRODUCT_FILE_VERSION "0.1.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "${__FILEDIR__}\..\artifacts\publish\win-x64"
!endif
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "${__FILEDIR__}\..\artifacts\release"
!endif

!define PRODUCT_NAME "Climb and Maintain ACARS"
!define PRODUCT_PUBLISHER "Climb and Maintain, SPC"
!define PRODUCT_EXE "ClimbAndMaintain.Acars.App.exe"
!define PRODUCT_ICON "${__FILEDIR__}\..\assets\branding\ClimbAndMaintain.Acars.ico"
!define PRODUCT_REG_KEY "Software\ClimbAndMaintain\ACARS"
!define UNINSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClimbAndMaintain.Acars"

Name "${PRODUCT_NAME}"
Caption "${PRODUCT_NAME} Setup"
OutFile "${OUTPUT_DIR}\ClimbAndMaintain-ACARS-Setup-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\ClimbAndMaintain\ACARS"
InstallDirRegKey HKCU "${PRODUCT_REG_KEY}" "InstallDir"
BrandingText "${PRODUCT_NAME}"
Icon "${PRODUCT_ICON}"
UninstallIcon "${PRODUCT_ICON}"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${PRODUCT_FILE_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright 2026 ${PRODUCT_PUBLISHER}"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${PRODUCT_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "${PRODUCT_NAME} requires 64-bit Windows."
    Abort
  ${EndIf}

  SetRegView 64
  nsExec::ExecToStack '"$SYSDIR\tasklist.exe" /FI "IMAGENAME eq ${PRODUCT_EXE}" /NH'
  Pop $0
  Pop $1
  ${StrStr} $2 $1 "${PRODUCT_EXE}"
  StrCmp $2 "" app_not_running
  MessageBox MB_OK|MB_ICONEXCLAMATION "Close ${PRODUCT_NAME} before installing or upgrading."
  Abort

app_not_running:
FunctionEnd

Section "${PRODUCT_NAME} (required)" SEC_CORE
  SectionIn RO
  SetRegView 64
  SetOutPath "$INSTDIR"

  ; Never package Microsoft native or managed SimConnect DLLs. Do not delete a
  ; user-owned library that may already exist beside an older installation.
  ; This does not exclude the project's own ClimbAndMaintain.Acars.SimConnect.dll.
  File /r /x "SimConnect*.dll" /x "Microsoft.FlightSimulator.SimConnect*.dll" "${PUBLISH_DIR}\*"
  File /oname=LICENSE.txt "${__FILEDIR__}\..\LICENSE"
  File /oname=NOTICE.txt "${__FILEDIR__}\..\NOTICE"
  File /oname=THIRD_PARTY_NOTICES.md "${__FILEDIR__}\..\THIRD_PARTY_NOTICES.md"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${PRODUCT_REG_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE},0"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\Climb and Maintain"
  CreateShortcut "$SMPROGRAMS\Climb and Maintain\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}"
  CreateShortcut "$SMPROGRAMS\Climb and Maintain\Uninstall ${PRODUCT_NAME}.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section /o "Desktop shortcut" SEC_DESKTOP
  CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}"
SectionEnd

LangString DESC_SEC_CORE ${LANG_ENGLISH} "Application files, runtime, documentation, and uninstaller. Microsoft SimConnect DLLs are not included."
LangString DESC_SEC_DESKTOP ${LANG_ENGLISH} "Create a shortcut on the current user's desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CORE} $(DESC_SEC_CORE)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} $(DESC_SEC_DESKTOP)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetRegView 64
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\Climb and Maintain\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\Climb and Maintain\Uninstall ${PRODUCT_NAME}.lnk"
  RMDir "$SMPROGRAMS\Climb and Maintain"
  DeleteRegKey HKCU "${UNINSTALL_REG_KEY}"
  DeleteRegKey HKCU "${PRODUCT_REG_KEY}"
  RMDir /r "$INSTDIR"

  MessageBox MB_YESNO|MB_ICONQUESTION "Remove local settings, profiles, logs, and saved flight or queue data?" IDNO keep_user_data
  RMDir /r "$LOCALAPPDATA\ClimbAndMaintain\ACARS"
keep_user_data:
SectionEnd
