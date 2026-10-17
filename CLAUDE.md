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
  `setOption {key, value}`, `factoryReset`, `diagnose`, `openLab`; o app responde `state`, `device`,
  `status {kind, text}` e `log {title, lines}`. Validação em `AkiraCore.Core.Presentation`
  (`LightingState`, `EffectView`): estado inválido nunca vira envio.
- Visual: estilo Ajustes do macOS, fonte do sistema (Segoe UI Variable), um azul de destaque, tema
  segue o Windows. A prévia do teclado é aproximada; geometria em `Ui/layout.js`, gerada do
  `rgb-keyboard.xml` oficial.
- Todo offset de byte novo ganha teste em `tests/AkiraCore.Core.Tests` citando a função de origem
  no binário oficial (ex.: `fcn.0042b170`).
- Segurança: só comandos de iluminação, mais o reset de fábrica oficial (`0F` no dongle,
  pedido do usuário em 17/10/2026). **Nunca** implementar nada do fluxo de atualização de
  firmware (`FirmwareUpdateTool.zip`). Opcodes desconhecidos não são enviados às cegas: tudo
  se baseia no que o instalador oficial já mapeia.

## Build e testes

```
dotnet test                                  # roda em qualquer SO
dotnet run --project src/AkiraCore.App       # só executa no Windows
bash build/build-installer.sh                # gera dist/AkiraCore-Setup-<versão>.exe (precisa do NSIS)
```

Instalador: `installer/AkiraCore.nsi` (NSIS 3, por usuário em `%LocalAppData%\Programs\AkiraCore`,
sem admin, pt-BR, `AllowSkipFiles off`). Publicação win-x64 autossuficiente em arquivo único com as
DLLs nativas ao lado do .exe (`IncludeNativeLibrariesForSelfExtract=false`: nada é extraído no
%TEMP%); o build gera `artifacts/installer-files.nsh` com a lista exata de File/Delete. O NSIS
comprime (LZMA); não ligar `EnableCompressionInSingleFile` no instalador.
Versão única no `<Version>` do `AkiraCore.App.csproj` (o build deriva a x.y.z.w dos metadados);
tag `vX.Y.Z` igual dispara `.github/workflows/installer.yml`, que exige o bootstrapper do WebView2
e cria o Release. Builds feitos nesta nuvem não conseguem baixar o bootstrapper (proxy bloqueia a
Microsoft): para distribuir, use o Release do CI.
Nomes compartilhados entre app e instalador (não mudar só de um lado): mutex
`AkiraCore.SingleInstance` (fica vivo até o fim do processo), evento `AkiraCore.Exit` (o
instalador sinaliza e espera o .exe ficar gravável), valor `AkiraCore` em `HKCU\...\Run` com
`--hidden` (`StartupCommand`) e o mesmo nome em `...\Explorer\StartupApproved\Run`.
Ícone: `src/AkiraCore.App/Assets/akira.svg` → `node build/icon/build-icon.cjs`.
Testar o instalador aqui: `apt-get install wine wine64 wine32:i386 xvfb` (com
`dpkg --add-architecture i386`), prefixo novo com `WINEARCH=win64`, `Setup.exe /S` e
`Uninstall.exe /S` + `wineserver -w`. O app .NET em si não roda no Wine (CoreCLR falha); para
simular o app aberto, use um executável NSIS que cria o mutex e o evento.

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

## Situação atual (17/10/2026)

Funcionando em hardware: efeitos das teclas pelo cabo e pelo dongle, cor, brilho, velocidade,
desligar, bandeja, configuração salva.

Ainda não testado em hardware/Windows real: a interface WebView2 e o instalador (o instalador
passou numa matriz de regressão no Wine: instalar, atualizar e desinstalar com o app aberto
simulado, arquivo travado, entrada de inicialização; o app .NET em si não roda no Wine).

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

- Faixa real do campo direção (hoje 0 ou 1).
- Significado do opcode `0xF0` (enviado porque o oficial sempre envia).
- `SideLightCatalog` e o envio lateral só existem no Laboratório; remover se a hipótese da matriz se confirmar.
