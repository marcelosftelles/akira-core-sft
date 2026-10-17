# AkiraCore: contexto para continuar o trabalho

App próprio em C# (.NET 8) para controlar o teclado **AKIRA CORE SK11A** (ADAMANTIUN GAMER)
no lugar do driver oficial. O protocolo foi reconstruído por engenharia reversa estática do
`DeviceDriver.exe` v1.0.0.0 (instalador `AKIRA_CORE_Driver-1_0_0_0.exe`,
SHA-256 `6e691f42e2a16873188f989f7249aea97f20c9e703ad43dabf3a95b65ff0b351`).

## Convenções

- Nomes de classes, métodos e variáveis em inglês; comentários em português do Brasil.
- Toda regra de protocolo fica em `AkiraCore.Core` (sem dependência de Windows, testável em Linux).
- `AkiraCore.App` só tem tela (WinForms), P/Invoke HID e o Laboratório.
- Todo offset de byte novo ganha teste em `tests/AkiraCore.Core.Tests` citando a função de origem
  no binário oficial (ex.: `fcn.0042b170`).
- Segurança: só comandos de iluminação. **Nunca** implementar nada do fluxo de atualização de
  firmware (`FirmwareUpdateTool.zip`). Opcodes desconhecidos não são enviados às cegas.

## Build e testes

```
dotnet test                                  # roda em qualquer SO
dotnet run --project src/AkiraCore.App       # só executa no Windows
```

O teste em hardware é feito pelo usuário no Windows; o ambiente de nuvem só compila e roda testes.

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

**Em investigação: fitas dos cantos (esquerdo e direito) e mini fita perto do knob.**
O usuário as controla por atalho do teclado; o software oficial não suporta.

Descartado, com evidência:
- Comando lateral oficial oculto (`sidelight="0"`, valores `0x20..0x29`, `05 01 00` no dongle):
  no cabo pisca e ignora; no dongle `05 01` apaga as teclas, ou seja, mexe na iluminação das
  teclas e não nas fitas.
- Canal de avisos (coleção 0xFFFF, prefixo `05 A6`): o atalho das fitas não gera nenhum report.
- Varredura dos bytes 0 a 5 do bloco no `05 01`: só pisca, nada fixa.
- Driver do AKIRA PRO (outra plataforma, Sinowealth 0x258A) e versão mais nova do SK11 (não existe).

Hipótese ativa: as fitas são LEDs da matriz **sem tecla** (`LedMap.UnmappedIndices`).
Ferramenta pronta: Laboratório → seção 4 → "Varrer LEDs sem tecla".
**Próximo passo: o usuário rodar a varredura e informar quais índices acendem cada zona.**

Se encontrar os índices: criar `ExtraZone` (canto esquerdo, canto direito, mini fita) no Core,
UI com cor por zona, e explicar o trade-off (no modo `0x80` as teclas ficam com cor fixa).
Se não encontrar: concluir que as fitas são controladas só pelo firmware.

## Pendências menores

- Faixa real do campo direção (hoje 0 ou 1).
- Significado do opcode `0xF0` (enviado porque o oficial sempre envia).
- `SideLightCatalog` e o envio lateral só existem no Laboratório; remover se a hipótese da matriz se confirmar.
