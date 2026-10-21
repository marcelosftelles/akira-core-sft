# AkiraCore: contexto para continuar o trabalho

App próprio em C# (.NET 8) para controlar o teclado **AKIRA CORE SK11A** (ADAMANTIUN GAMER)
no lugar do driver oficial. O protocolo foi reconstruído por engenharia reversa estática do
`DeviceDriver.exe` v1.0.0.0 (instalador `AKIRA_CORE_Driver-1_0_0_0.exe`,
SHA-256 `6e691f42e2a16873188f989f7249aea97f20c9e703ad43dabf3a95b65ff0b351`).

## Convenções

- Nomes de classes, métodos e variáveis em inglês; comentários em português do Brasil.
- Toda regra de protocolo fica em `AkiraCore.Core` (sem dependência de Windows, testável em Linux).
- `AkiraCore.App` só tem a janela que hospeda a interface, bandeja, P/Invoke HID e o Laboratório.
- A interface principal é HTML/CSS/JS em `src/AkiraCore.App/Ui` (sem bibliotecas, sem rede, CSP
  `'self'`), embutida no .exe e servida pelo `EmbeddedUi` em `https://akira.ui` dentro de um WebView2.
  O Laboratório continua WinForms (ferramenta técnica).
- Contrato da interface: a página manda `ready`, `apply {lighting}`, `setLight {on, lighting}`,
  `setOption {key, value}`, `setKeys {assignments}`, `saveMacros {macros, assignments}`,
  `setDevice {settings}`, `factoryReset`, `diagnose`, `openLab`, `saveCustomLights {schemes}`,
  `useCustomLight {id}`, `textMacro {text, delay}`, `exportBackup`, `importBackup`,
  `exportMacros {macros}`, `importMacros`, `exportColors {id}`, `importColors`, `switchProfile {id}`,
  `createProfile {name}`, `renameProfile {id, name}`, `deleteProfile {id}`; o app responde `state`
  (com os blocos `keyboard {catalog, assignments, macros, settings}`, `customLights {schemes, activeId}`
  e `profiles {list, activeId}`), `device` (com `battery {level, cable}`), `status {kind, text}`,
  `log {title, lines}`, `customLights`, `profiles`, `macroText {events, skipped, layout}`,
  `macrosImported {macros}` e `key {scan, down}` (tecla tocada no AKIRA, scan code
  do conjunto 1 com `0xE000` para prefixo E0; mapa em `SCAN_CODES` no `layout.js`).
  O Raw Input de teclado aceita um destino por processo: há um `RawKeyboardMonitor` só, preso à
  janela principal; prévia (janela visível) e Laboratório pedem escuta com `Listen()`. Validação em `AkiraCore.Core.Presentation`
  (`LightingState`, `EffectView`, `KeysState.cs`): estado inválido nunca vira envio.
  As páginas Teclas e Macros e o grupo Teclado de Ajustes ficam em `Ui/keys.js`; Cores por tecla
  em `Ui/colors.js`. Arquivos exportados (backup, macros, cores) são JSON com campo `format`,
  validados em `ConfigFiles`. Perfis: os campos de `AppSettings` são o perfil ativo em uso e cada
  gravação copia para a lista (`MainForm.Profiles.cs`); macros e esquemas de cores são globais.
  Texto em macro: `TextMacro` no Core, layout do Windows em `WindowsTextLayout` (App).
  Revisão da interface no Windows: `scratchpad` com servidor Node estático e Edge headless
  (`--screenshot` ou CDP); não há Python nesta máquina.
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

**Fase 2, protocolo mapeado na REA em 21/10/2026 e ainda não implementado:**
- `fcn.00434230` é o laço de comandos do oficial. Em espera, roda os modos contínuos: 3 =
  `fcn.0042adb0` (lê `04 F5` a cada ciclo e pinta o desenho da tela: espelho real dos LEDs, só
  cabo), 6 = `fcn.00427f60` (luz personalizada em tempo real) e 7 = `fcn.004332c0` (ritmo musical).
- Ritmo musical: áudio pelo WASAPI (CLSID do MMDeviceEnumerator em `0x574de4`). Dongle
  `fcn.00432b00`: report `0B 1C 00`, `[4]` modo de frente, `[5]` modo de fundo, `[6]` brilho e,
  a partir de `[7]`, a altura de cada coluna (o firmware desenha). Cabo `fcn.004328c0`.
- Cabo, tempo real e música: `04 20` com `[8]` = 3 (ou 8), quadro de 0xC0 (ou 0x200) bytes por
  `fcn.0044ef50`, depois `04 02`. O formato do quadro está nos blocos que o Ghidra descartou:
  ler o assembly de `fcn.00427f60`/`fcn.004328c0`.
- Comando `1F` no dongle (item `0xd` do laço), provavelmente "Reiniciar teclado"; confirmar.
- Fora de escopo: tela TFT (`screen="0"`): `fcn.00422ac0`/`00423360` (GIF, `04 72` e `7F 03`) e
  `fcn.00423a80`/`00423bb0` (relógio, `04 28` e `0C 10`).

**Fase 3, novas:** perfil automático por programa, regras de luz (bloqueio, tela desligada,
horário), atalhos globais, linha de comando e efeitos vivos pelo `04 20` (tela, CPU, avisos).

## Situação atual (17/10/2026)

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

**Próximo passo:** usuário roda T0 a T3 e envia os logs. Depois, implementar a leitura `04 F5`
no Laboratório (só cabo) e comparar a tabela antes e depois de `Fn + Z` e `Fn + /?`.

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
