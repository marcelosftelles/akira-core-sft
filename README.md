# AKIRA CORE

App próprio para controlar a iluminação do teclado AKIRA CORE (ADAMANTIUN GAMER) no Windows.
Protocolo reconstruído por engenharia reversa do driver oficial v1.0.0.0.

## Interface

Visual inspirado nos Ajustes do macOS: barra lateral com os 19 efeitos, painel com uma prévia
animada do teclado (desenhada com as posições das 82 teclas do `rgb-keyboard.xml` oficial) e
grupos com cor, arco-íris, brilho, velocidade e direção. Não há botão "Aplicar": a mudança vai para
o teclado ao soltar o controle. Tema claro e escuro seguem o Windows.

A tela é HTML/CSS/JS (`src/AkiraCore.App/Ui`) dentro de um WebView2, embutida no executável e
servida em `https://akira.ui` sem tocar na rede. Precisa do
[Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/consumer/), que já vem no
Windows 11 e no Windows 10 atualizado; sem ele o app avisa e a bandeja continua funcionando.

Para revisar o visual sem Windows: sirva a pasta `Ui` (`python -m http.server` dentro dela) e abra
`index.html` num navegador. Sem WebView2 a página usa o `dev-mock.js`, que aceita `?view=strips`,
`?view=settings`, `?device=dongle|none`, `?off=1`, `?effect=static&rainbow=0&color=%2300E5FF`,
`?log=1` e `?dialog=1`.

## Canais suportados

| Conexão | Interface | Formato |
|---|---|---|
| Cabo USB | MI_02 (page 0xFF13), feature report de 65 bytes | Transação 18, 13, dados, 02, F0 |
| Dongle 2.4G | MI_03 (page 0xFF60), input/output de 33 bytes | Report único `05 10 00` + bloco + checksum, confirmado por eco |
| Bluetooth | não suportado (nem pelo app oficial) | |

O bloco de iluminação (16 bytes) é o mesmo nos dois canais.

## Fitas laterais

O app oficial tem a página "Modo de luz lateral" pronta, mas desligada no XML deste modelo
(`sidelight="0"`). O firmware aceita 10 predefinições no byte 0 do bloco (0x20 a 0x29).
No cabo vai na mesma transação das teclas; no dongle o cabeçalho muda para `05 01 00`.
Não há cor livre nem controle separado de cada lado.

**Status:** no cabo, o firmware deste teclado pisca e ignora esses valores. Por isso a função
saiu da tela principal e foi para o Laboratório, junto com:

- Monitor de atalhos: escuta todas as coleções HID que o Windows deixa abrir (inclusive os
  avisos `05 A6 ...` da coleção 0xFFFF) e as teclas do AKIRA via Raw Input, com marcadores
  para anotar cada atalho apertado.
- Bloco experimental: envia 16 bytes livres pelo canal de iluminação, com assinatura forçada.
- Foto do estado: lê o que o teclado devolve (GET_REPORT, e opcionalmente os ACKs completos)
  e mostra o que mudou desde a foto anterior.

A investigação completa, com a evidência do AULA F108 Pro (mesma plataforma, onde as fitas são
só firmware) e o roteiro de testes, está em [`docs/investigacao-fitas.md`](docs/investigacao-fitas.md).

## Restaurar padrão de fábrica

Em Ajustes › Padrão de fábrica, baseado na "Redefinição de fábrica" do app oficial (`fcn.00414870`):

| Canal | O que é enviado |
|---|---|
| Dongle 2.4G | Report `0F` (reset do firmware: apaga teclas, macros e luz guardadas), 50 ms, iluminação padrão |
| Cabo USB | Só a iluminação padrão. O app oficial não tem comando de reset no cabo |

Iluminação padrão: Rolando (modo 11), arco-íris, vermelho, brilho 5, velocidade 3.
As fitas não têm comando no PC; use os atalhos: laterais com Fn + Shift/Z/Alt esquerdos,
loading bar com Fn + Shift/"/?"/Alt direitos.

## Cores por LED (modo personalizado)

Modo `0x80` + buffer de 576 bytes, 4 bytes por LED `[índice, R, G, B]` na posição índice × 4,
assinatura `AA 55` no fim. Cabo: transação com opcode `0x23` anunciando 9 pacotes. Dongle: 21 reports
`14 <tamanho> <pedaço>` com 28 bytes de dados cada (o último com 16).

O layout oficial usa 82 dos índices 1 a 121; os demais (ver `LedMap.UnmappedIndices`) são candidatos
às fitas dos cantos e à mini fita. O Localizador de LED do Laboratório serve para mapeá-los.

## Estrutura

| Projeto | Papel |
|---|---|
| `src/AkiraCore.Core` | Protocolo, efeitos e regras. Sem dependência de Windows. |
| `src/AkiraCore.App` | Janela WinForms que hospeda a interface HTML (WebView2), bandeja, Laboratório e HID via P/Invoke. |
| `tests/AkiraCore.Core.Tests` | Testes dos pacotes byte a byte (xUnit). |

## Rodar

```
dotnet test
dotnet run --project src/AkiraCore.App
```

Feche o app oficial pela bandeja antes de usar. Em Ajustes, "Simular envio" mostra os bytes sem enviar.

## Instalar

Baixe `AkiraCore-Setup-<versão>.exe` em Releases (ou gere com o script abaixo) e execute.

- Instala só para o seu usuário, em `%LocalAppData%\Programs\AkiraCore`, sem pedir administrador.
  Numa atualização, fecha o AkiraCore aberto e mantém a mesma pasta.
- Opções: atalho na área de trabalho e "Iniciar com o Windows, escondido na bandeja" (dá para
  mudar depois em Ajustes › Iniciar com o Windows).
- Se o Microsoft Edge WebView2 Runtime faltar, o instalador o instala quando foi gerado com o
  bootstrapper (os Releases sempre têm); senão, oferece abrir a
  [página do WebView2](https://developer.microsoft.com/microsoft-edge/webview2/consumer/).
- Desinstalar: Configurações › Aplicativos › Aplicativos instalados (no Windows 10: Aplicativos
  e recursos) › AkiraCore. As configurações salvas só são apagadas se você confirmar.

O instalador não é assinado digitalmente:

- O navegador pode avisar que o arquivo é pouco baixado; escolha **Manter**.
- O SmartScreen pode mostrar "O Windows protegeu o computador": clique em **Mais informações ›
  Executar assim mesmo**.
- Se o bloqueio vier do **Controle Inteligente de Aplicativos** (Windows 11), não há botão para
  continuar: só dá para usar desligando o recurso em Segurança do Windows › Controle de
  aplicativos e navegador. Resolver isso de vez exige assinatura de código (o Azure Artifact
  Signing ainda não aceita pessoa física no Brasil).

## Gerar o instalador

Precisa do .NET 8 SDK da Microsoft e do NSIS 3. No Linux/WSL use o SDK da Microsoft
(`dotnet-install.sh` ou packages.microsoft.com): o `dotnet-sdk-8.0` empacotado pelo Ubuntu não
traz os alvos de WinForms, e o script avisa.

```
bash build/build-installer.sh                                       # Linux, macOS, WSL, Git Bash
powershell -ExecutionPolicy Bypass -File build\build-installer.ps1   # Windows (winget install NSIS.NSIS)
```

O script roda os testes, publica o app para win-x64 de forma autossuficiente (não precisa de .NET
instalado no PC; o `AkiraCore.exe` vai com as DLLs nativas ao lado), tenta baixar o bootstrapper
do WebView2 e gera `dist/AkiraCore-Setup-<versão>.exe`. Sem internet para a Microsoft, o
instalador sai sem o bootstrapper, e a linha final `==> Pronto (SEM o bootstrapper do WebView2)`
avisa. A versão vem do
`<Version>` do `src/AkiraCore.App/AkiraCore.App.csproj`.

Para publicar uma versão: suba o `<Version>`, faça commit e crie a tag igual (`git tag v1.0.1`,
`git push origin v1.0.1`). O workflow `.github/workflows/installer.yml` gera o instalador, exige o
bootstrapper do WebView2 e cria o Release.

Versão portátil, sem instalador (um único .exe, roda de qualquer pasta):

```
dotnet publish src/AkiraCore.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist/portatil
```

Para iniciar escondido na bandeja: `AkiraCore.exe --hidden`.

## Suposições ainda não confirmadas em hardware

- Ordem das cores no pacote (R, G, B). Se vermelho acender azul, ligue Ajustes › Corrigir cores.
- Faixa do campo de direção (o app usa 0 ou 1).
- Função exata do comando 0xF0 (enviado porque o app oficial sempre envia).
