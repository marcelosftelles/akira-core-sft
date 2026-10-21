# Protocolo do AKIRA CORE

Notas técnicas do protocolo (iluminação, teclas, macros e configurações gerais), reconstruído por engenharia reversa estática do
`DeviceDriver.exe` v1.0.0.0 (instalador oficial `AKIRA_CORE_Driver-1_0_0_0.exe`). Cada offset de
byte tem teste em `tests/AkiraCore.Core.Tests` citando a função de origem no binário oficial.

## Canais suportados

| Conexão | VID:PID | Interface | Formato |
|---|---|---|---|
| Cabo USB | 0C45:8006 | MI_02 (page 0xFF13), feature report de 65 bytes | Transação 18, 13, dados, 02, F0 |
| Dongle 2.4G | 05AC:024F | MI_03 (page 0xFF60), input/output de 33 bytes | Report único `05 10 00` + bloco + checksum, confirmado por eco |
| Bluetooth | n/a | não suportado (nem pelo app oficial) | |

Com cabo e dongle conectados, o cabo tem prioridade (igual ao app oficial).

## Bloco de iluminação

16 bytes, iguais nos dois canais: `[0]` modo, `[1..3]` R G B, `[8]` arco-íris, `[9]` brilho 0..5,
`[10]` velocidade 0..5, `[11]` direção, `[14..15]` `AA 55`. Modo 0 desliga a luz.

**Cabo:** transação `04 18` (ACK) → `04 13` com `[8]` = nº de pacotes (ACK) → dados →
`04 02` (ACK) → `04 F0`. ACK = GetFeature com `report[4] == 1`. 35 ms entre envios.

**Dongle:** output report de 33 bytes com conteúdo em `[1..31]` e checksum de 8 bits em `[32]`.
A confirmação é um input report ecoando `[1..3]`.

## Cores por LED (modo personalizado)

Modo `0x80` + buffer de 576 bytes, 4 bytes por LED `[índice, R, G, B]` na posição índice × 4,
assinatura `AA 55` no fim. Cabo: transação com opcode `0x23` anunciando 9 pacotes. Dongle: 21 reports
`14 <tamanho> <pedaço>` com 28 bytes de dados cada (o último com 16).

O layout oficial usa 82 dos índices 1 a 121; os demais (ver `LedMap.UnmappedIndices`) são candidatos
às fitas dos cantos e à mini fita. O Localizador de LED do Laboratório serve para mapeá-los.

## Remapeamento de teclas

Duas tabelas de 576 bytes, uma por camada (normal e Fn segurado), no mesmo formato do buffer de
cores por LED: 4 bytes por tecla na posição `key_index × 4`, `AA 55` no fim. Slot zerado = tecla
original. O `key_index` vem do `rgb-keyboard.xml` e, neste modelo, é igual ao `light_index`.

| Canal | Envio | Função |
|---|---|---|
| Cabo | `04 18` → `04 11` (normal) ou `04 27` (Fn) com `[8] = 9` → 9 pacotes → `04 02` → `04 F0` (com ACK) | `fcn.00418550` |
| Dongle | 21 reports `10` (normal) ou `12` (Fn) `<tamanho> <pedaço>` + 28 bytes, 2 ms entre eles | `fcn.00418df0` |

| Ação (combo do app oficial) | Slot |
|---|---|
| Original | `00 00 00 00` |
| Desativar tecla (tipo 1) | `05 03 00 00` |
| Função de tecla / atalho do Windows (tipos 2 e 7) | `02 <modificadores> <código HID> 00`; modificador sozinho vira bit com código 0 |
| Macro (tipo 3) | `06 <posição da macro> <modo> <repetições>`; modo 0 uma vez, 1 N vezes, 2 até apertar de novo, 3 enquanto segurar |
| Mouse (tipo 5) | `01 01 <botão>` (01 esquerdo, 02 direito, 04 meio, 03 duplo clique, 08 e 10 extras) ou `01 03 01` / `01 03 FF` (rolagem) |
| Multimídia (tipo 6) | `03 <código Consumer> 00 00` (CD, B7, B6, B5, E9, EA, E2) |

Fora do app: "Multitecla" (tipo 13, `07 ...`), multimídia estendida (tipo 12) e abrir programa,
site ou texto (tipos 8 a 10, `05 02 ...`), que dependem do app oficial rodando. O Fn não é
remapeado.

## Macros

Um buffer com todas as macros (até 100), igual nos dois canais:

- Bytes 0 a 399: 100 cabeçalhos de 4 bytes. O da macro *i* guarda a posição do bloco dela (16 bits).
  Macro vazia: `FF FF FF FF`. Posição sem macro: zerada.
- A partir de `0x190`, um bloco por macro: nº de eventos (16 bits), 6 bytes zerados e eventos de
  4 bytes `[atraso baixo, atraso alto, código, tipo]`.
- Cada ação vira dois eventos: a ação (`B0` tecla apertada, `30` solta, `90`/`10` botão do mouse
  com os bits HID) e uma pausa (`50`) de no mínimo 10 ms.
- Limite do oficial: (nº de macros + nº de ações) × 8 ≤ 2700.

| Canal | Envio | Função |
|---|---|---|
| Cabo | `04 19` → `04 15` com `[8]` = pacotes → 30 ms → dados → 30 ms → `04 02`. Os dados ocupam `n/64 + 1` pacotes (resto zero) ou `n/64 + 2`, com `AA 55` nos dois últimos bytes | `fcn.0042d5d0` |
| Dongle | Pedaços `09 <tamanho> <pedaço>` + 28 bytes, mesma regra de um pedaço a mais com o `AA 55`; o último declara como tamanho o resto da divisão por 28 | `fcn.0042db50` |

## Configurações gerais

Bloco de 16 bytes: `[1] = 01`, `[2]` bloqueia Alt+Tab, `[3]` Alt+F4, `[4]` tecla Windows,
`[5]` modo do Fn (0 segurar, 1 alternar), `[6]` suspensão (0 nunca, 1 = 1 min, 2 = 5 min,
3 = 30 min), `[8]` tempo de resposta (1 a 5; 0 é o que o oficial manda quando nunca foi escolhido).

| Canal | Envio | Função |
|---|---|---|
| Cabo | `04 18` → `04 17` com `[2] = 1` e `[8] = 1` → dados (bytes 0 a 13 do bloco, `AA 55` no byte 62) → `04 02` | `fcn.004141a0` |
| Dongle | Report único `07 10 00` + bloco (`AA 55` no byte 14 dele) | `fcn.00414410` |

O teclado não devolve nada disso: o app guarda o que configurou em `settings.json`, como o
oficial guarda no banco SQLite dele.

## Restaurar padrão de fábrica

Baseado na "Redefinição de fábrica" do app oficial (`fcn.00414870`):

| Canal | O que é enviado |
|---|---|
| Dongle 2.4G | Report `0F` (reset do firmware: apaga teclas, macros e luz guardadas), 50 ms, iluminação padrão |
| Cabo USB | O app oficial não tem comando de reset no cabo e reenvia os padrões (`fcn.00434ee0`). Aqui, na mesma ordem: macros vazias, as duas tabelas de teclas vazias, iluminação padrão e configurações gerais zeradas. Cores por LED e luz lateral ficam de fora |

Iluminação padrão: Rolando (modo 11), arco-íris, vermelho, brilho 5, velocidade 3.

## Bateria

Só no dongle (`fcn.004357d0`, analisado com a REA/Ghidra em 21/10/2026). O oficial pergunta ao
conectar e depois a cada 15 s, apenas quando o canal ativo é o 2.4G:

| Direção | Bytes (com Report ID em `[0]`) |
|---|---|
| Pedido | `00 20 01 00 … 00` + checksum em `[32]` (= `21`), pela transação normal do dongle (`fcn.0044f480`) |
| Resposta | `00 20 01 00 NN …`: eco de `[1..3]` e o nível `NN` em porcentagem |

A leitura do oficial (`fcn.00451190`) tira o Report ID 0, então no código dele o nível aparece em
`[3]`. Nível 0 é ignorado (mantém o anterior); acima de 100 vira 100.

**Carregando:** o protocolo do oficial não tem esse dado. Ele chama `BatteryCtrl::SetBatteryInfo(nível, 0)`
sempre, e na `mui.dll` o segundo argumento 1 é o que desenha o raio (`icon_charge.png`); nunca é usado.
O AkiraCore considera "carregando" quando o cabo (0C45:8006) aparece no Windows e lê o nível pelo
dongle mesmo com o cabo ligado. Em Ajustes › Diagnóstico sai a resposta crua da bateria: comparar
com e sem o cabo mostra se algum outro byte indica carga.

## Fitas laterais

O app oficial tem a página "Modo de luz lateral" pronta, mas desligada no XML deste modelo
(`sidelight="0"`). O firmware aceita 10 predefinições no byte 0 do bloco (0x20 a 0x29).
No cabo vai na mesma transação das teclas; no dongle o cabeçalho muda para `05 01 00`.

**Status:** no cabo, o firmware deste teclado pisca e ignora esses valores; no dongle, `05 01` mexe
na iluminação das teclas, não nas fitas. Por isso a função ficou só no Laboratório, junto com:

- Monitor de atalhos: escuta todas as coleções HID que o Windows deixa abrir (inclusive os
  avisos `05 A6 ...` da coleção 0xFFFF) e as teclas do AKIRA via Raw Input, com marcadores
  para anotar cada atalho apertado.
- Bloco experimental: envia 16 bytes livres pelo canal de iluminação, com assinatura forçada.
- Foto do estado: lê o que o teclado devolve (GET_REPORT, e opcionalmente os ACKs completos)
  e mostra o que mudou desde a foto anterior.

A investigação completa, com a evidência do AULA F108 Pro (mesma plataforma, onde as fitas são
só firmware) e o roteiro de testes, está em [`investigacao-fitas.md`](investigacao-fitas.md).

## Suposições ainda não confirmadas em hardware

- Faixa do campo de direção (o app usa 0 ou 1).
- Função exata do comando `0xF0` (enviado porque o app oficial sempre envia).
- Ordem das cores (R, G, B): nenhuma inversão relatada até agora. Se aparecer, há a opção
  Ajustes › Corrigir cores.
- Remapeamento, macros e configurações gerais: formatos confirmados no binário, ainda sem teste
  em hardware. Em especial: botões 4 e 5 do mouse (o oficial chama o bit `0x08` de "Botão 5";
  pela convenção HID ele é o 4) e o efeito do tempo de resposta 0.
