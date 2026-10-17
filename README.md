# AKIRA CORE

App próprio para controlar a iluminação do teclado AKIRA CORE (ADAMANTIUN GAMER) no Windows.
Protocolo reconstruído por engenharia reversa do driver oficial v1.0.0.0.

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

Botão na janela principal, baseado na "Redefinição de fábrica" do app oficial (`fcn.00414870`):

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
| `src/AkiraCore.App` | Interface WinForms, ícone na bandeja e acesso HID via P/Invoke. |
| `tests/AkiraCore.Core.Tests` | Testes dos pacotes byte a byte (xUnit). |

## Rodar

```
dotnet test
dotnet run --project src/AkiraCore.App
```

Feche o app oficial pela bandeja antes de usar. Marque "Simular" para ver os bytes sem enviar.

## Gerar o executável

```
dotnet publish src/AkiraCore.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Para iniciar escondido na bandeja: `AkiraCore.exe --hidden`.

## Suposições ainda não confirmadas em hardware

- Ordem das cores no pacote (R, G, B). Se vermelho acender azul, marque "Corrigir cores".
- Faixa do campo de direção (o app usa 0 ou 1).
- Função exata do comando 0xF0 (enviado porque o app oficial sempre envia).
