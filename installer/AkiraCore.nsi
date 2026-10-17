; Instalador do AkiraCore: NSIS 3, por usuário (sem administrador), português do Brasil.
; Não rode direto: use build/build-installer.sh ou build/build-installer.ps1, que publicam o app,
; geram a lista de arquivos (FILES_NSH) e chamam o makensis com as definições abaixo.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel user
; Arquivo travado nunca é "ignorado": vira Repetir/Cancelar, e no modo silencioso a instalação falha
AllowSkipFiles off

!ifndef VERSION
  !error "Defina -DVERSION=<versão do .csproj>"
!endif
!ifndef VI_VERSION
  !error "Defina -DVI_VERSION=x.y.z.w (só números, para os metadados do Windows)"
!endif
!ifndef FILES_NSH
  !error "Defina -DFILES_NSH=<lista de arquivos gerada pelo build>"
!endif
!ifndef OUT_FILE
  !error "Defina -DOUT_FILE=<caminho do Setup.exe>"
!endif

!define APP_NAME "AkiraCore"
!define APP_EXE "AkiraCore.exe"
!define UNINSTALL_EXE "Uninstall.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\AkiraCore"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define STARTUP_APPROVED_KEY "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
; Mesmos nomes do Program.cs e do StartupCommand.cs: mudar só de um lado quebra o fechamento
!define MUTEX_NAME "AkiraCore.SingleInstance"
!define EXIT_EVENT_NAME "AkiraCore.Exit"
!define RUN_VALUE "AkiraCore"
; Runtime Evergreen do WebView2 (GUID documentado pela Microsoft)
!define WEBVIEW2_KEY "Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
; Página para o usuário final (a licença do WebView2 não permite repassar o link direto do instalador)
!define WEBVIEW2_PAGE "https://developer.microsoft.com/microsoft-edge/webview2/consumer/"

!include MUI2.nsh
!include LogicLib.nsh
!include FileFunc.nsh
!include x64.nsh
!include WinVer.nsh
!include "${FILES_NSH}"

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails nevershow
ShowUninstDetails nevershow

; Pasta de uma instalação anterior; vazia numa instalação nova
Var PreviousDir

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
; Atualização: mantém a pasta atual (trocar de pasta deixaria a cópia antiga para trás).
; A pasta vem antes dos componentes para o botão "Instalar" ficar sempre na página certa.
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirectoryOnUpgrade
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "PortugueseBR"

VIProductVersion "${VI_VERSION}"
VIFileVersion "${VI_VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileDescription" "Instalador do ${APP_NAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "LegalCopyright" "Projeto ${APP_NAME}, 2026"

; --- Fechar o app aberto antes de trocar ou apagar arquivos ---------------------------
; O app cria o mutex AkiraCore.SingleInstance e sai quando o evento AkiraCore.Exit é sinalizado.
; O mutex some um pouco antes de o Windows liberar o .exe, então a espera só termina quando
; o próprio .exe pode ser aberto para escrita.

!macro DEFINE_CLOSE_APP PREFIX
Function ${PREFIX}CloseRunningApp
  retry:
  StrCpy $2 0
  ${Do}
    ; Sinaliza a cada volta: pega também uma instância que abriu durante a espera
    ; (EVENT_MODIFY_STATE = 0x0002)
    System::Call 'kernel32::OpenEventW(i 0x0002, i 0, w "${EXIT_EVENT_NAME}") p .r1'
    ${If} $1 P<> 0
      System::Call 'kernel32::SetEvent(p r1)'
      System::Call 'kernel32::CloseHandle(p r1)'
    ${EndIf}

    ; SYNCHRONIZE = 0x00100000: só testa se o mutex existe
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${MUTEX_NAME}") p .r0'
    ${If} $0 P<> 0
      System::Call 'kernel32::CloseHandle(p r0)'
    ${ElseIfNot} ${FileExists} "$INSTDIR\${APP_EXE}"
      Return
    ${Else}
      ; GENERIC_WRITE sem compartilhamento: só abre quando nenhum processo usa o .exe
      System::Call 'kernel32::CreateFileW(w "$INSTDIR\${APP_EXE}", i 0x40000000, i 0, p 0, i 3, i 0x80, p 0) p .r3 ?e'
      Pop $4
      ${If} $3 P<> -1
        System::Call 'kernel32::CloseHandle(p r3)'
        Return
      ${EndIf}
      ; Só vale esperar por arquivo em uso (32 = compartilhamento, 33 = trava). Outro erro,
      ; como somente leitura ou permissão, não é o app aberto: File/Delete mostram o motivo real.
      ${If} $4 <> 32
      ${AndIf} $4 <> 33
        Return
      ${EndIf}
    ${EndIf}

    Sleep 250
    IntOp $2 $2 + 1
  ${LoopUntil} $2 >= 40

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
  ; Instalador de 32 bits: HKLM\SOFTWARE\... cai em WOW6432Node no Windows 64 bits, como manda a doc
  SetRegView 32
  ReadRegStr $1 HKLM "SOFTWARE\${WEBVIEW2_KEY}" "pv"
  SetRegView default
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
  ; Sem administrador, o bootstrapper instala só para o usuário; ele baixa o runtime da Microsoft
  DetailPrint "Instalando o Microsoft Edge WebView2 Runtime..."
  InitPluginsDir
  File "/oname=$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" "${WEBVIEW2_BOOTSTRAPPER}"
  ExecWait '"$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" /silent /install' $1
  ; O código de saída não é documentado: o que vale é detectar de novo
  Call DetectWebView2
  ${If} $R0 = 1
    Return
  ${EndIf}
!endif
  MessageBox MB_YESNO|MB_ICONINFORMATION "O ${APP_NAME} precisa do Microsoft Edge WebView2 Runtime para mostrar a janela, e ele não foi encontrado.$\r$\n$\r$\nAbrir a página da Microsoft para baixar e instalar? A bandeja do app funciona mesmo sem ele." /SD IDNO IDNO done
  ExecShell "open" "${WEBVIEW2_PAGE}"
  done:
FunctionEnd

; --- Seções ---------------------------------------------------------------------------------

Section "${APP_NAME}" SecApp
  SectionIn RO
  Call CloseRunningApp
  !insertmacro INSTALL_APP_FILES
  SetOutPath "$INSTDIR"
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

  ; Uma entrada "iniciar com o Windows" que já existia passa a abrir esta instalação
  ReadRegStr $0 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $0 != ""
    WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}" --hidden'
  ${EndIf}

  Call EnsureWebView2
SectionEnd

Section /o "Atalho na área de trabalho" SecDesktop
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section /o "Iniciar com o Windows, escondido na bandeja" SecStartup
  ; Mesmo valor que o app grava em Ajustes (StartupCommand.For). Apagar o StartupApproved
  ; desfaz um "Desabilitado" antigo do Gerenciador de Tarefas, senão o Windows ignoraria o Run.
  WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}" --hidden'
  DeleteRegValue HKCU "${STARTUP_APPROVED_KEY}" "${RUN_VALUE}"
SectionEnd

Function .onInit
  SetShellVarContext current
  ; O app é win-x64: roda em x64 e, no Windows 11, em ARM64 pela emulação de x64
  ${If} ${IsNativeAMD64}
  ${ElseIf} ${IsNativeARM64}
  ${AndIf} ${AtLeastWin11}
  ${Else}
    MessageBox MB_ICONSTOP "O ${APP_NAME} precisa do Windows de 64 bits (x64, ou ARM64 com Windows 11)." /SD IDOK
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "O ${APP_NAME} precisa do Windows 10 ou mais novo." /SD IDOK
    Abort
  ${EndIf}

  ReadRegStr $PreviousDir HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $PreviousDir != ""
  ${AndIf} ${FileExists} "$PreviousDir\${APP_EXE}"
    StrCpy $INSTDIR $PreviousDir
  ${Else}
    StrCpy $PreviousDir ""
  ${EndIf}
FunctionEnd

Function SkipDirectoryOnUpgrade
  ${If} $PreviousDir != ""
    Abort
  ${EndIf}
FunctionEnd

; --- Desinstalação -------------------------------------------------------------------------

Function un.onInit
  SetShellVarContext current
FunctionEnd

Section "Uninstall"
  Call un.CloseRunningApp

  ; Só os arquivos que o instalador colocou. Se o .exe resistir, nada mais é removido:
  ; apagar o registro antes deixaria uma instalação órfã, sem como desinstalar.
  retry_delete:
  ; Delete marca erro quando um arquivo existe e não sai (arquivo ausente não conta)
  ClearErrors
  !insertmacro UNINSTALL_APP_FILES
  ${If} ${Errors}
  ${OrIf} ${FileExists} "$INSTDIR\${APP_EXE}"
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Não deu para apagar os arquivos do ${APP_NAME}. Feche o ${APP_NAME} pela bandeja (botão direito no ícone, Sair) e clique em Repetir." /SD IDCANCEL IDRETRY retry_delete
    Abort
  ${EndIf}
  Delete "$INSTDIR\${UNINSTALL_EXE}"
  ; Sem /r: a pasta só sai se ficar vazia (pode ser uma pasta escolhida pelo usuário)
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
  DeleteRegValue HKCU "${STARTUP_APPROVED_KEY}" "${RUN_VALUE}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  ; Cache de versões antigas, publicadas como arquivo único que se extraía no %TEMP%
  RMDir /r "$TEMP\.net\${APP_NAME}"

  ; Configurações (%AppData%\AkiraCore) e dados do WebView2 (%LocalAppData%\AkiraCore):
  ; só com confirmação, e nunca no modo silencioso
  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Apagar também as configurações salvas do ${APP_NAME}?" /SD IDNO IDNO keep
  ; Os processos do WebView2 podem segurar a pasta por um instante depois que o app fecha
  Sleep 1000
  RMDir /r "$APPDATA\${APP_NAME}"
  RMDir /r "$LOCALAPPDATA\${APP_NAME}"
  keep:
SectionEnd
