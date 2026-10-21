# AkiraCore: contexto para continuar o trabalho

App próprio em C# (.NET 8) para controlar o teclado **AKIRA CORE SK11A** (ADAMANTIUN GAMER)
no lugar do driver oficial. O protocolo foi reconstruído por engenharia reversa estática do
`DeviceDriver.exe` v1.0.0.0 (instalador `AKIRA_CORE_Driver-1_0_0_0.exe`,
SHA-256 `6e691f42e2a16873188f989f7249aea97f20c9e703ad43dabf3a95b65ff0b351`).

## Convenções

- Nomes de classes, métodos e variáveis em inglês; comentários em português do Brasil.
- Toda regra de protocolo fica em `AkiraCore.Core` (sem dependência de Windows, testável em Linux).
- `AkiraCore.App` só tem a janela que hospeda a interface, bandeja, P/Invoke (HID em `Hid/`,
  Windows em `Platform/`), o laço ao vivo (`Live/`) e o Laboratório.
- A interface principal é HTML/CSS/JS em `src/AkiraCore.App/Ui` (sem bibliotecas, sem rede, CSP
  `'self'`), embutida no .exe e servida pelo `EmbeddedUi` em `https://akira.ui` dentro de um WebView2.
  O Laboratório continua WinForms (ferramenta técnica).
- Contrato da interface: a página manda `ready`, `apply {lighting}`, `setLight {on, lighting}`,
  `setOption {key, value}`, `setKeys {assignments}`, `saveMacros {macros, assignments}`,
  `setDevice {settings}`, `factoryReset`, `diagnose`, `openLab`, `saveCustomLights {schemes}`,
  `useCustomLight {id}`, `textMacro {text, delay}`, `exportBackup`, `importBackup`,
  `exportMacros {macros}`, `importMacros`, `exportColors {id}`, `importColors`, `switchProfile {id}`,
  `createProfile {name}`, `renameProfile {id, name}`, `deleteProfile {id}`, `setLive {live}`,
  `liveTimer {action}` (start, pause, stop), `testAlert {color, times}`, `setMirror {on}`,
  `setRules {rules}`, `setHotkeys {hotkeys}`, `setAutoProfile {auto}`, `setProfileApps {id, apps}` e
  `pickFile {purpose}`; o app responde `state` (com os blocos
  `keyboard {catalog, assignments, macros, settings}`, `customLights {schemes, activeId}`,
  `profiles {list (com apps), activeId, auto}`, `live {settings, catalog, lastMode, status}`,
  `automation {rules, hotkeys, hotkeyActions}` e `mirror`), `device` (com `battery {level, cable}`),
  `status {kind, text}`, `log {title, lines}`, `customLights`, `profiles`,
  `macroText {events, skipped, layout}`, `macrosImported {macros}`, `liveFrame {colors}` (quadro
  que vai ao teclado, LED → #RRGGBB), `liveStatus {status}`, `mirror {colors, problem}`,
  `automation {automation}`, `filePicked {purpose, path}` e `key {scan, down}` (tecla tocada no
  AKIRA, scan code do conjunto 1 com `0xE000` para prefixo E0; mapa em `SCAN_CODES` no `layout.js`
  e `KeyboardLayout.LedOfScanCode` no Core).
  O Raw Input de teclado aceita um destino por processo: há um `RawKeyboardMonitor` só, preso à
  janela principal; prévia (janela visível), Laboratório e a cena ao vivo com teclas "acender ao
  tocar" pedem escuta com `Listen()`. Validação em `AkiraCore.Core.Presentation`
  (`LightingState`, `EffectView`, `KeysState.cs`): estado inválido nunca vira envio.
  As páginas Teclas e Macros e o grupo Teclado de Ajustes ficam em `Ui/keys.js`; Cores por tecla
  em `Ui/colors.js`; Ao vivo em `Ui/live.js`; regras de luz e atalhos globais em `Ui/automation.js`. Arquivos exportados (backup, macros, cores) são JSON com campo `format`,
  validados em `ConfigFiles`. Perfis: os campos de `AppSettings` são o perfil ativo em uso e cada
  gravação copia para a lista (`MainForm.Profiles.cs`); macros e esquemas de cores são globais.
  Texto em macro: `TextMacro` no Core, layout do Windows em `WindowsTextLayout` (App).
  Revisão da interface no Windows: `scratchpad` com servidor Node estático e Edge headless
  (`--screenshot` ou CDP); não há Python nesta máquina.
- `MainForm` é dividido em parciais: `MainForm.cs` (janela, mensagens, envio), `.Library.cs`
  (esquemas, arquivos), `.Profiles.cs`, `.Live.cs` (modo ao vivo, espelho, cronômetro, avisos) e
  `.Automation.cs` (regras de luz, perfil automático, atalhos, teclas do app, linha de comando).
  Todo envio ao teclado passa pelo `_keyboardLock`; o laço ao vivo pega o semáforo por quadro com
  `Wait(0)` e pula o quadro se outro envio estiver no meio.
- Visual: estilo Ajustes do macOS, fonte do sistema (Segoe UI Variable), um azul de destaque, tema
  segue o Windows. A prévia do teclado é aproximada; geometria em `Ui/layout.js`, gerada do
  `rgb-keyboard.xml` oficial.
- Todo offset de byte novo ganha teste em `tests/AkiraCore.Core.Tests` citando a função de origem
  no binário oficial (ex.: `fcn.0042b170`).
- Segurança: comandos de iluminação, o reset de fábrica oficial (`0F` no dongle, pedido do
  usuário em 17/10/2026) e, a pedido do usuário em 20/10/2026, remapeamento de teclas, macros e
  configurações gerais, todos copiados de funções do binário oficial. **Nunca** implementar nada do fluxo de atualização de
  firmware (`FirmwareUpdateTool.zip`). Opcodes desconhecidos não são enviados às cegas: tudo
  se baseia no que o instalador oficial já mapeia.

## Build e testes

```
dotnet test                                  # roda em qualquer SO
dotnet run --project src/AkiraCore.App       # só executa no Windows
```

`dotnet akira` é atalho para o `dotnet run` acima: `tools/dotnet-akira.cmd`, com `tools` no PATH do
usuário (o dotnet procura `dotnet-<nome>` no PATH quando o comando não existe).

O teste em hardware é feito pelo usuário no Windows; o ambiente de nuvem só compila e roda testes.
Para revisar a interface aqui: `python3 -m http.server` dentro de `src/AkiraCore.App/Ui` e
screenshots com o Playwright do ambiente (`/opt/node-tools/node_modules/playwright`); o
`dev-mock.js` faz o papel do app (parâmetros listados no README).
O pacote do WebView2 referencia o controle WPF sempre; o `.csproj` remove essa referência
(target `RemoveWebView2Wpf`) para não ter o aviso MSB3277.
No Linux, o `dotnet-sdk-8.0` do Ubuntu não traz os alvos de WinForms: copie a pasta
`Sdks/Microsoft.NET.Sdk.WindowsDesktop` do pacote `dotnet-sdk-8.0` da Microsoft
(packages.microsoft.com) para dentro do SDK instalado, senão `src/AkiraCore.App` não compila.

## Dispositivo

| Conexão | VID:PID | Coleção de comandos | Como é escolhida |
|---|---|---|---|
| Cabo USB | 0C45:8006 | MI_02, page 0xFF13, feature 65 | feature report == 65 bytes |
| Dongle 2.4G | 05AC:024F | MI_03, page 0xFF60, in/out 33 | input == output == 33 bytes |
| Bluetooth | n/a | não suportado nem pelo oficial | |

O `config.xml` oficial diz MI_00 para o cabo, mas está errado: não filtrar por interface.
Prioridade: cabo antes do dongle (igual ao oficial). Regra em `ChannelClassifier`.

## Protocolo

**Bloco de iluminação (16 bytes, igual nos dois canais):**
`[0]` modo, `[1..3]` R G B (ordem COLORREF; nenhuma inversão relatada em hardware), `[8]` arco-íris,
`[9]` brilho 0..5, `[10]` velocidade 0..5, `[11]` direção, `[14..15]` `AA 55`.
Modo 0 = desligado (envia só o byte 0). Modo `0x80` = personalizado por LED (byte 9 = brilho).
Tabela dos 20 efeitos em `EffectCatalog` (extraída de `fcn.0042b280`).

**Cabo (feature reports de 65 bytes, Report ID 0):**
transação `04 18` (ACK) → `04 <op>` com `[8]` = nº de pacotes (ACK) → dados (sem ACK)
→ `04 02` (ACK) → `04 F0` (sem ACK). ACK = GetFeature com `report[4] == 1`. 35 ms entre envios.
Iluminação: op `0x13`, 1 pacote. Cores por LED: op `0x23`, 9 pacotes.

**Dongle (output report de 33 bytes, Report ID 0):**
conteúdo em `[1..31]`, checksum de 8 bits (soma de `[0..31]`) em `[32]`. Confirmação = input
report ecoando `[1..3]`; até 20 leituras de 5 ms com 10 ms de pausa; 1 retentativa de escrita.
Iluminação: `05 10 00` + bloco. Cores por LED: 21 reports `14 <tam> <pedaço>` + 28 bytes
(último com 16), 2 ms entre eles.

**Cores por LED:** buffer de 576 bytes, LED `i` em `i*4` como `[i, R, G, B]`, `AA 55` em 574.
Índices válidos 1..142. Layout oficial usa 82 índices entre 1 e 121 (matriz 6 × 18).

**Teclas, macros e configurações gerais (20/10/2026, sem teste em hardware ainda):**
remapeamento = tabela de 576 bytes por camada, slot de 4 bytes em `key_index × 4`
(`fcn.00418550` cabo `04 11`/`04 27`; `fcn.00418df0` dongle `10`/`12` em pedaços de 28).
Macros = buffer com 100 cabeçalhos + blocos a partir de `0x190` (`fcn.0042d5d0` cabo
`04 19`/`04 15`; `fcn.0042db50` dongle `09`). Configurações = bloco de 16 bytes
(`fcn.004141a0` cabo `04 17`; `fcn.00414410` dongle `07 10 00`). Detalhes em `docs/protocolo.md`.
O oficial monta `04 11`/`04 27` com `cmovne`, por isso a varredura do T5 não os achou; o `04 2B`
dela era o atalho Alt + Tab dentro da tabela. Reset no cabo agora reenvia macros, teclas,
iluminação e configurações, na ordem de `fcn.00434ee0`.
Ferramenta de análise usada: disassembler descartável em .NET com a biblioteca Iced
(instalador baixado de `adamantiun.com.br`, mesmo SHA-256).

**Bateria (21/10/2026, analisado com a REA/Ghidra, sem teste em hardware ainda):** só no dongle,
`fcn.004357d0`: report `20 01` pela transação normal; resposta ecoa `20 01 00` e traz o nível
(0..100) em `[4]` (com Report ID; o oficial lê `[3]` porque `fcn.00451190` tira o ID 0). A cada
15 s e ao conectar. O protocolo não informa carga: o oficial passa sempre 0 em
`SetBatteryInfo(nível, carregando)` da `mui.dll`. O app mostra "carregando" quando o cabo aparece
no Windows e lê o nível pelo dongle mesmo com o cabo ligado. Diagnóstico mostra a resposta crua.
Instalador original fica em `ExeOriginal/` (fora do git). Engenharia reversa nova: usar a REA.

## Roteiro de features (pedido do usuário em 21/10/2026: tudo, por fases)

**Fase 1, com o protocolo que já existia (feita em 21/10/2026, sem teste em hardware):** aviso de
bateria fraca (`BatteryAlert`, balão da bandeja), backup completo e importar/exportar macros e
esquemas (`ConfigFiles`), texto que vira macro no firmware (`TextMacro` + `WindowsTextLayout`;
acento por tecla morta, AltGr = Alt direito), página Cores por tecla (`CustomLightState`, modo
`0x80` + `0x23`/`14`; pintar um esquema já o usa no teclado; escolher efeito sai dele) e perfis
(`ProfileState`; seletor na barra lateral, lista em Ajustes e submenu na bandeja).

**Fase 2 (implementada em 21/10/2026, sem teste em hardware):**
- `fcn.00434230` é o laço de comandos do oficial. Em espera, roda os modos contínuos: 3 =
  `fcn.0042adb0` (espelho dos LEDs pelo `04 F5`, só cabo), 6 = `fcn.00427f60` (luz personalizada
  em tempo real) e 7 = `fcn.004332c0` (ritmo musical).
- `04 20` no cabo (`FeatureLink.SendStreamFrame`): `04 20` com `[8]` = 8 (ACK) → 8 pacotes de
  64 bytes sem ACK, pausa de 5 ms (`fcn.0044ef50`) → `04 02` (ACK). Sem `04 18` e sem `04 F0`.
  Quadro de 0x200 bytes = lista corrida de `[índice, R, G, B]` na ordem do desenho, zeros no resto
  (`LiveFrame.ToStreamFrame`). O Ghidra descartou os blocos que montam o quadro no AKIRA; o formato
  vem de duas fontes independentes da plataforma irmã AULA F108 Pro (capturas USB do app oficial):
  `Punkster81/AULA-F108-Driver` e o controlador AulaF108Pro do OpenRGB
  (`hcode10/OpenRGB-With-AulaF108Pro-support`). Ambas batem com o `[8]` e o tamanho lidos no
  binário do AKIRA. `[8]` = 3 (0xC0) é a variante dos teclados monocromáticos, como no `04 F5`.
  O OpenRGB reenvia o cabeçalho a cada 2 s: sem quadro novo o teclado pode voltar sozinho ao efeito.
- `04 F5` no cabo (`FeatureLink.ReadLedColors`): `04 F5` com `[8]` = 9 → ACK (até 10 leituras de
  5 ms) → 9 GET_REPORT → `04 02`. Tabela igual à do `04 23`; `PacketBuilder.ParseLedColors`.
- Ritmo musical: áudio pelo WASAPI loopback (`Live/AudioCapture.cs`, COM direto, sem biblioteca),
  FFT e faixas em `SpectrumAnalyzer` (Core). Cabo: barras desenhadas no PC (`MusicVisualizer`) e
  enviadas pelo `04 20`, como `fcn.004328c0`. Dongle (`fcn.00432b00`): report `0B 1C 00`, `[4]`
  modo de frente, `[5]` modo de fundo, `[6]` brilho e, a partir de `[7]`, a altura de cada coluna.
  **A confirmar na REA:** quantas colunas o oficial preenche e a escala da altura (o app usa 18
  colunas e 0 a 6, calibráveis no Laboratório e salvos em `DongleMusic` no settings.json), e os
  valores dos modos de frente e fundo (o app manda 0 e 0).
- `1F` no dongle (`DongleLink.SendKeyboardReset`, só no Laboratório): é o "Reset Keyboard" do
  oficial (`fcn.00414600`, textos 165/166). No cabo o mesmo botão roda a tarefa 6 (regrava as
  tabelas de teclas), então a hipótese é que o `1F` zere o remapeamento no firmware. Confirmar.
- Página Ao vivo (`LiveSettingsState`, `LiveEngine`, `LiveRunner`): Teclas animadas (luz
  personalizada do oficial: fixa, respirar, piscar, espectro, onda, acender ao tocar), Ritmo
  musical, Cor da tela, Uso da CPU e Cronômetro (F1 a F12). Um modo por vez, global (fora dos
  perfis). Escolher efeito ou esquema sai do modo ao vivo. Ao sair do app, a luz normal é
  reenviada. Avisos (`AlertFlash`): no cabo piscam pelo `04 20` (nada gravado); no dongle a cor
  fica fixa pelo tempo do aviso e a luz volta.
- Espelho (`LedMirror`): Iluminação › "Mostrar o teclado de verdade", lê o `04 F5` a cada 200 ms
  com a janela à vista e sem modo ao vivo transmitindo.
- Fora de escopo: tela TFT (`screen="0"`): `fcn.00422ac0`/`00423360` (GIF, `04 72` e `7F 03`) e
  `fcn.00423a80`/`00423bb0` (relógio, `04 28` e `0C 10`). O "handshake" do Punkster81
  (`04 18`, `04 28`, `00 01 5A …`) é a sincronia do relógio dessa tela; o app não manda.

**Fase 3 (implementada em 21/10/2026, sem teste no Windows):**
- Perfil automático (`ProfileApps`, `ForegroundWatcher` com `SetWinEventHook`): cada perfil tem
  `Apps` (executáveis); troca com 1,5 s de espera e ignora a janela do próprio AkiraCore. A troca
  de perfil só regrava o que muda (teclas, configurações, luz).
- Regras de luz (`LightRules`, `SessionWatcher`): apagar com o Windows bloqueado
  (`WTSRegisterSessionNotification`), com a tela desligada (`GUID_CONSOLE_DISPLAY_STATE`) e
  horário noturno (apagar ou brilho 1). Passam por cima em `CurrentLightingAction` sem mudar a
  configuração.
- Atalhos globais (`Hotkeys`, `GlobalHotkeys` com `RegisterHotKey`): ligar/desligar luz, efeito
  seguinte/anterior, brilho, perfil seguinte/anterior, modo ao vivo e abrir a janela.
- Teclas com ação do app (`AppKeyActions`, ação "app" em `KeyAssignmentState`): abrir programa,
  site, digitar texto (SendInput Unicode, depois de soltar Ctrl/Alt/Shift), multimídia estendida,
  bloquear, suspender e desligar (30 s, apertar de novo cancela). A tecla vira um atalho reservado
  pelo formato `02` já confirmado: F13 a F24 com Ctrl+Alt+Shift, Ctrl+Alt, Ctrl+Shift ou Alt+Shift
  (48 vagas), registrado pelo app. Nada do slot `05 02` do oficial é enviado.
- Linha de comando (`CommandLine` no Core, `Program.cs`, `CommandChannel`): pipe nomeado só do
  usuário e da sessão. Sem instância aberta, abre uma na bandeja e entrega o pedido; quem chamou
  recebe a resposta e o código de saída (0 ok, 1 erro, 2 argumento inválido).
## Situação atual (21/10/2026)

**Próximo passo da Fase 2:** usuário testa no Windows, na ordem: Laboratório seção 6 (ler cores
`04 F5` com um efeito animado; quadro de teste `04 20`; barras `0B` no dongle ajustando colunas e
altura; `1F`), depois a página Ao vivo e a automação. Na REA, ler `fcn.00432b00` para fechar
colunas, escala e modos do ritmo no dongle, e conferir o quadro em `fcn.00427f60`.

## Situação anterior (17/10/2026)

Funcionando em hardware: efeitos das teclas pelo cabo e pelo dongle, cor, brilho, velocidade,
desligar, bandeja, configuração salva.

**Em investigação: fitas dos cantos (esquerdo e direito) e mini fita perto do knob.**
O usuário as controla por atalho do teclado; o software oficial não suporta.
Detalhes, evidências e roteiro de testes em `docs/investigacao-fitas.md`.

Descartado, com evidência:
- Comando lateral oficial oculto (`sidelight="0"`, valores `0x20..0x29`, `05 01 00` no dongle):
  no cabo pisca e ignora; no dongle `05 01` apaga as teclas, ou seja, mexe na iluminação das
  teclas e não nas fitas.
- Canal de avisos (coleção 0xFFFF, prefixo `05 A6`): o atalho das fitas não gera nenhum report.
- Varredura dos bytes 0 a 5 do bloco no `05 01`: só pisca, nada fixa.
- Driver do AKIRA PRO (outra plataforma, Sinowealth 0x258A) e versão mais nova do SK11 (não existe).
- Usuário relata que o Laboratório (incluindo LEDs sem tecla) não trouxe resultado; falta
  confirmar se rodou "Varrer todos (1 a 142)".

Evidência externa: o AULA F108 Pro (github.com/parsiya/f108-pro) é a mesma plataforma
(Suoai, dongle 05AC:024F idêntico, mesmo erro de MI_00, funções do driver 0x60 a 0x70 depois
das nossas). Lá as fitas e a barra de luz são **só firmware** (atalhos Fn) e o teclado dá ACK
nos comandos laterais sem fazer nada. Hipótese mais provável para o AKIRA: igual.

Ferramentas novas no Laboratório para fechar a questão:
- Seção 1: monitor de todas as coleções HID legíveis + teclas do AKIRA via Raw Input + marcadores.
- Seção 5: foto do estado (GET_REPORT só leitura e, opcional, ACKs completos reaplicando o
  efeito) com diff contra a foto anterior. Regras em `AkiraCore.Core.Diagnostics`.

Atalhos do manual: laterais = Fn + Shift esq. (modo), Fn + Z (cor), Fn + Alt esq. (brilho);
loading bar (mini fita) = Fn + Shift dir., Fn + /?, Fn + Alt dir. Mesmas teclas do F108 Pro.

Instalador analisado (T5): sem imagem de firmware; `04 F5` (ler as 576 bytes de cores por
LED) em `fcn.0042adb0`; reset de fábrica oficial mapeado e implementado
(`KeyboardClient.RestoreFactoryDefaults`, botão "Restaurar padrão de fábrica"): `0F` no dongle
+ iluminação padrão; no cabo só a iluminação padrão, porque o oficial não tem comando.

**Próximo passo (fitas):** usuário roda T0 a T3 e envia os logs. A leitura `04 F5` já está no
Laboratório (seção 6, só cabo): comparar a tabela antes e depois de `Fn + Z` e `Fn + /?` (a
leitura lista também LEDs sem tecla que estiverem acesos).

Se encontrar os índices: criar `ExtraZone` (canto esquerdo, canto direito, mini fita) no Core,
UI com cor por zona, e explicar o trade-off (no modo `0x80` as teclas ficam com cor fixa).
Se tudo der negativo: concluir que as fitas são controladas só pelo firmware e documentar os
atalhos no app.

## Pendências menores

- Prévia dos efeitos: aproximação sem descrição oficial. Rolagem é vertical (único com o par de
  direções alternativo, bit `0x08`). O usuário relatou em 20/10/2026 que alguns efeitos não batem
  com o teclado; falta saber quais e como aparecem de verdade (incluindo se velocidade 0 é a mais
  lenta e se brilho 0 apaga).

- Faixa real do campo direção (hoje 0 ou 1).
- Significado do opcode `0xF0` (enviado porque o oficial sempre envia).
- `SideLightCatalog` e o envio lateral só existem no Laboratório; remover se a hipótese da matriz se confirmar.
