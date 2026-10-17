; Instalador do AkiraCore: NSIS 3, por usuário (sem administrador), português do Brasil.
; Não rode direto: use build/build-installer.sh ou build/build-installer.ps1, que publicam o app
; e chamam o makensis com VERSION, PUBLISH_DIR e OUT_FILE.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!ifndef VERSION
  !error "Defina -DVERSION=x.y.z"
!endif
!ifndef PUBLISH_DIR
  !error "Defina -DPUBLISH_DIR=<pasta do dotnet publish>"
!endif
!ifndef OUT_FILE
  !error "Defina -DOUT_FILE=<caminho do Setup.exe>"
!endif

!define APP_NAME "AkiraCore"
!define APP_EXE "AkiraCore.exe"
!define UNINSTALL_EXE "Uninstall.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\AkiraCore"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
; Mesmos nomes do Program.cs e do StartupCommand.cs
!define MUTEX_NAME "AkiraCore.SingleInstance"
!define EXIT_EVENT_NAME "AkiraCore.Exit"
!define RUN_VALUE "AkiraCore"
; Runtime Evergreen do WebView2 (GUID documentado pela Microsoft)
!define WEBVIEW2_KEY "Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
!define WEBVIEW2_URL "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

!include MUI2.nsh
!include LogicLib.nsh
!include FileFunc.nsh
!include x64.nsh
!include WinVer.nsh

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails nevershow
ShowUninstDetails nevershow

; --- Aparência e páginas -------------------------------------------------------------

!define MUI_ICON "../src/AkiraCore.App/Assets/akira.ico"
!define MUI_UNICON "../src/AkiraCore.App/Assets/akira.ico"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_NODESC

!define MUI_WELCOMEPAGE_TITLE "Instalar o ${APP_NAME} ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "O ${APP_NAME} controla a iluminação do teclado AKIRA CORE pelo cabo ou pelo dongle 2.4G. É um app independente, não é o driver oficial.$\r$\n$\r$\nA instalação é só para o seu usuário e não pede senha de administrador.$\r$\n$\r$\nFeche o app oficial do teclado antes de usar o ${APP_NAME}: os dois usam o mesmo canal."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir o ${APP_NAME} agora"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "PortugueseBR"

VIProductVersion "${VERSION}.0"
VIFileVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileDescription" "Instalador do ${APP_NAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "LegalCopyright" "Projeto ${APP_NAME}, 2026"

; --- Fechar o app aberto antes de trocar ou apagar o .exe ----------------------------
; O app cria o mutex AkiraCore.SingleInstance e sai quando o evento AkiraCore.Exit é sinalizado.

!macro DEFINE_CLOSE_APP PREFIX
Function ${PREFIX}CloseRunningApp
  ; SYNCHRONIZE = 0x00100000: só testa se o mutex existe
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${MUTEX_NAME}") i .r0'
  ${If} $0 = 0
    Return
  ${EndIf}
  System::Call 'kernel32::CloseHandle(i r0)'

  ; EVENT_MODIFY_STATE = 0x0002: pede para o app sair (ele fecha a janela e a bandeja)
  System::Call 'kernel32::OpenEventW(i 0x0002, i 0, w "${EXIT_EVENT_NAME}") i .r1'
  ${If} $1 <> 0
    System::Call 'kernel32::SetEvent(i r1)'
    System::Call 'kernel32::CloseHandle(i r1)'
  ${EndIf}

  ; Espera até ~8 s o mutex sumir (o processo terminou)
  retry:
  StrCpy $2 0
  ${Do}
    Sleep 250
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${MUTEX_NAME}") i .r0'
    ${If} $0 = 0
      Return
    ${EndIf}
    System::Call 'kernel32::CloseHandle(i r0)'
    IntOp $2 $2 + 1
  ${LoopUntil} $2 >= 32

  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "O ${APP_NAME} ainda está aberto. Feche-o pela bandeja (botão direito no ícone, Sair) e clique em Repetir." /SD IDCANCEL IDRETRY retry
  Abort
FunctionEnd
!macroend

!insertmacro DEFINE_CLOSE_APP ""
!insertmacro DEFINE_CLOSE_APP "un."

; --- WebView2 -----------------------------------------------------------------------------

; Coloca 1 em $R0 se o runtime Evergreen estiver instalado (máquina ou usuário)
Function DetectWebView2
  StrCpy $R0 0
  ; Instalador de 32 bits: HKLM\SOFTWARE\... é redirecionado para WOW6432Node no Windows 64 bits
  SetRegView 32
  ReadRegStr $1 HKLM "SOFTWARE\${WEBVIEW2_KEY}" "pv"
  ${If} $1 != ""
  ${AndIf} $1 != "0.0.0.0"
    StrCpy $R0 1
    Return
  ${EndIf}
  ReadRegStr $1 HKCU "Software\${WEBVIEW2_KEY}" "pv"
  ${If} $1 != ""
  ${AndIf} $1 != "0.0.0.0"
    StrCpy $R0 1
  ${EndIf}
FunctionEnd

Function EnsureWebView2
  Call DetectWebView2
  ${If} $R0 = 1
    Return
  ${EndIf}
!ifdef WEBVIEW2_BOOTSTRAPPER
  DetailPrint "Instalando o Microsoft Edge WebView2 Runtime..."
  InitPluginsDir
  File "/oname=$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" "${WEBVIEW2_BOOTSTRAPPER}"
  ExecWait '"$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" /silent /install' $1
  Call DetectWebView2
  ${If} $R0 = 1
    Return
  ${EndIf}
!endif
  MessageBox MB_YESNO|MB_ICONINFORMATION "O ${APP_NAME} precisa do Microsoft Edge WebView2 Runtime para mostrar a janela, e ele não foi encontrado.$\r$\n$\r$\nAbrir a página de download agora? A bandeja do app funciona mesmo sem ele." /SD IDNO IDNO done
  ExecShell "open" "${WEBVIEW2_URL}"
  done:
FunctionEnd

; --- Seções ---------------------------------------------------------------------------------

Section "${APP_NAME}" SecApp
  SectionIn RO
  Call CloseRunningApp
  SetOutPath "$INSTDIR"
  File "${PUBLISH_DIR}/${APP_EXE}"
  WriteUninstaller "$INSTDIR\${UNINSTALL_EXE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Projeto ${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\${UNINSTALL_EXE}"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\${UNINSTALL_EXE}" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" "$0"

  Call EnsureWebView2
SectionEnd

Section /o "Atalho na área de trabalho" SecDesktop
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section /o "Iniciar com o Windows, escondido na bandeja" SecStartup
  ; Mesmo valor que o app grava em Ajustes (StartupCommand.For)
  WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}" --hidden'
SectionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "O ${APP_NAME} precisa do Windows de 64 bits." /SD IDOK
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "O ${APP_NAME} precisa do Windows 10 ou mais novo." /SD IDOK
    Abort
  ${EndIf}
FunctionEnd

; --- Desinstalação -------------------------------------------------------------------------

Section "Uninstall"
  Call un.CloseRunningApp

  ; Só os arquivos que o instalador colocou; a pasta sai apenas se ficar vazia
  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\${UNINSTALL_EXE}"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"

  ; Configurações (%AppData%\AkiraCore) e dados do WebView2 (%LocalAppData%\AkiraCore):
  ; só com confirmação, e nunca no modo silencioso
  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Apagar também as configurações salvas do ${APP_NAME}?" /SD IDNO IDNO keep
  RMDir /r "$APPDATA\${APP_NAME}"
  RMDir /r "$LOCALAPPDATA\${APP_NAME}"
  keep:
SectionEnd
