# Investigação: fitas dos cantos e mini fita do knob

Objetivo: controlar pelo AkiraCore as três zonas que hoje só mudam por atalho do teclado
(fita do canto esquerdo, fita do canto direito, mini fita ao lado do knob).

## Resumo

A hipótese mais provável, com base na plataforma irmã, é que as três zonas são controladas
**só pelo firmware**, sem comando USB. Isso ainda não está provado para o AKIRA. Os testes
abaixo decidem a questão em ordem de custo, do mais barato ao mais caro.

## Atalhos do teclado (manual do AKIRA)

| Zona | Modo | Cor | Brilho |
|---|---|---|---|
| LEDs laterais (fitas dos cantos) | Fn + Shift esquerdo | Fn + Z | Fn + Alt esquerdo |
| Loading bar (mini fita do knob) | Fn + Shift direito | Fn + /? | Fn + Alt direito |

São as mesmas teclas do AULA F108 Pro (lá: barra de luz com Shift/Alt esquerdo e Z; fitas
laterais com Shift/Alt direito e /?), com os nomes das zonas trocados. Mais um sinal de que o
firmware é o mesmo. Se uma zona apagar, o brilho ou o modo ciclou até "desligado": repita o
atalho de brilho e depois o de modo até ela voltar.

## O que já foi descartado (com evidência)

| Hipótese | Resultado | Fonte |
|---|---|---|
| Comando lateral oficial oculto (`0x20..0x29`, `sidelight="0"`) | Cabo: pisca e ignora. Dongle `05 01`: apaga as teclas | Teste em hardware |
| Aviso do firmware no canal 0xFFFF (`05 A6`) | O atalho não gera report | Monitor antigo |
| Varredura dos bytes 0 a 5 do bloco no `05 01` | Só pisca, nada fixa | Laboratório |
| Driver do AKIRA PRO (Sinowealth) e versão nova do SK11 | Outra plataforma / não existe | Pesquisa |

## Evidência nova: AULA F108 Pro, mesma plataforma

O projeto [parsiya/f108-pro](https://github.com/parsiya/f108-pro) fez engenharia reversa do
driver do AULA F108 Pro. Os sinais de que é a mesma plataforma do AKIRA:

| Item | AULA F108 Pro | AKIRA CORE |
|---|---|---|
| Fabricante | Dongguan Suoai (索艾) | white label, mesmo software |
| Chip | Sonix SN32F2xx (VID 0C45) | VID 0C45 |
| Dongle 2.4G | `05AC:024F`, MI_03 | `05AC:024F`, MI_03 (idêntico) |
| Filtro do cabo no `config.xml` | `MI_00`, mas o real é outra interface | `MI_00`, mas o real é MI_02 |
| Transação do cabo | `04 18`, `04 13`, dados, `04 02`, `04 F0`, 35 ms | igual |
| Página lateral | `sidelight="0"` no `rgb-keyboard.xml` | igual |
| Tabela de efeitos | `FUN_0042b2f0` | `fcn.0042b280` (mesmo código, 0x70 antes) |
| Iluminação 2.4G (`05 10`) | `FUN_0042b1e0` | `fcn.0042b170` (0x70 antes) |
| Envio da transação USB | `FUN_0044edc0` | `fcn.0044ed60` (0x60 antes) |
| Envio do dongle | `FUN_0044f4e0` | `fcn.0044f480` (0x60 antes) |
| Cores por LED, cabo | `FUN_0044b910` | `fcn.0044b8a0` (0x70 antes) |
| Pacote `80` antes das cores | `FUN_0044b800` ("sidelight sender" para eles) | `fcn.0044b790`: é o modo personalizado `0x80`, confirmado em hardware |

Conclusão deles para o F108 Pro (não testada no AKIRA):

- As fitas laterais e a barra de luz respondem **só a atalhos Fn**. O código lateral do
  driver existe para outros modelos e fica desligado por `sidelight="0"`.
- O teclado devolve ACK para os comandos laterais e **não faz nada**. ACK prova que o
  pacote chegou, não que a função existe.
- Não há comando para ler configuração; o protocolo é só escrita. Exceções: a resposta do
  `04 02` traz 2 bytes além do ACK, e existe o `04 F5` ("ler cores por tecla", `FUN_0042ae20`
  no binário deles), ainda não localizado no binário do AKIRA.

Diferença de detalhe: eles listam os modos laterais como `0x1F..0x28`; nós, como `0x20..0x29`
(item N recebe `0x1F + N`). Uma das duas leituras tem erro de um. Vale testar `0x1F` (teste T0).

## Por que um "sniffer" no PC provavelmente não verá nada

O atalho percorre `tecla → firmware do teclado → LEDs`, tudo dentro do teclado. O PC só vê o
que o firmware decide mandar pelo USB. É como testar com `cy.intercept` uma regra que roda
inteira no backend: se não há chamada HTTP, o interceptador não tem o que mostrar.

Mesmo assim, vale provar isso uma vez: se aparecer qualquer report ou mudança de estado, o
caminho por software existe. Se não aparecer nada, o único lugar onde a "comunicação" existe
é a placa, e aí o instrumento certo é um analisador lógico, não um sniffer USB.

## Roteiro de testes

Feche o app oficial antes. Anote os atalhos que mexem em cada zona (precisamos dessa lista).

**T0. Fitas estão na matriz de LEDs? (5 min, Laboratório seção 4)**
1. Cabo USB, clique "Varrer todos (1 a 142)" com 2 s por LED.
2. Olhe só as três zonas. Anote qualquer índice que acenda nelas.
3. Seção 2, no cabo: envie o bloco `1F 00 00 00 00 00 00 00 00 00 00 00 00 00 AA 55` e observe.

**T1. O atalho sai do teclado? (5 min, Laboratório seção 1)**
1. "Iniciar monitor". Confira a lista "Escutando HID".
2. Digite o atalho no campo (ex.: `Fn + Shift direito`), clique "Marcar", aperte o atalho.
3. Repita para cada atalho, 3 vezes, com pausa de 3 s. Aperte também uma tecla comum (`A`)
   como controle: ela precisa gerar linhas TECLA.
4. "Salvar log..." e me envie o arquivo.

**T2. O firmware expõe o estado das fitas? (5 min, Laboratório seção 5)**
1. "Tirar foto do estado" (só leitura).
2. Mude uma zona pelo atalho. Tire outra foto. Repita para cada atalho.
3. Repita tudo com "Incluir respostas completas" marcado (reaplica o efeito da janela
   principal e guarda os 65 bytes de cada ACK).
4. Salve o log.

**T3. Captura USB completa (20 min, prova definitiva no barramento)**
1. Instale o [Wireshark](https://www.wireshark.org/download.html) marcando a opção USBPcap.
   Reinicie o Windows.
2. Abra o Wireshark. Nas opções da interface USBPcap, deixe ligado "Inject already connected
   devices descriptors". Descubra qual `USBPcapN` mostra tráfego quando você digita no AKIRA.
3. Inicie a captura nessa interface. Fique 5 s parado. Aperte `A` uma vez (referência).
4. Aperte cada atalho das fitas com 5 s de intervalo, anotando a ordem.
5. Pare, salve como `.pcapng` e me envie. Filtro útil: `usb.idVendor == 0x0c45` para achar o
   endereço do teclado, depois `usb.device_address == N && usb.data_len > 0`.

O USBPcap vê inclusive as coleções que o Windows esconde do app (teclado e mouse).

**T4. Placa (opcional, para quem abre teclado)**
1. Fotografe os dois lados da placa, com as marcações dos chips legíveis, e os conectores das
   fitas.
2. Conte os fios de cada fita: 3 fios (5 V, GND, dados) indica LED endereçável tipo WS2812;
   4 fios (V+, R, G, B) indica RGB analógico por PWM.
3. Com o multímetro em continuidade, siga a trilha de dados até um chip. Se chega no Sonix
   principal, o firmware principal controla as fitas. Se chega em outro chip, há um
   controlador separado e o PC não alcança as fitas.
4. Se tiver analisador lógico (clone de 24 MHz serve): capture a linha de dados no PulseView
   enquanto aperta os atalhos e salve o `.sr`. O decodificador `rgb_led_ws281x` mostra a cor
   de cada LED. Confirme antes que as entradas do analisador aceitam 5 V.

**T5. Instalador oficial (feito em 17/10/2026)**

Instalador recebido, SHA-256 igual ao do `CLAUDE.md`. Extraído com `innoextract`, analisado
com capstone (sem executar nada). Resultados:

1. **Sem imagem de firmware:** o pacote não tem pasta `firmware` e o XML tem
   `<firmware version="120" file="" url="" />`. Não há o que analisar do lado do teclado.
2. **`04 F5` (ler cores por LED) existe**, em `fcn.0042adb0`, exatamente onde o deslocamento
   previa. Só no cabo. Fluxo: `04 F5` com `[8] = 9` (ou 3 no modo monocromático) → espera o
   ACK (até 10 leituras, 5 ms) → 9 GET_REPORT de 64 bytes (576 bytes, a mesma tabela do `04 23`)
   → `04 02`. Não usa `04 18`. Próximo passo: ler essa tabela antes e depois de `Fn + Z` /
   `Fn + /?` para ver se a cor das fitas aparece em algum índice sem tecla.
3. **Todos os comandos que o driver monta** (`04 xx` e `05 xx`): `04 02`, `04 13`, `04 15`,
   `04 17`, `04 18`, `04 19`, `04 20`, `04 23`, `04 28`, `04 2B`, `04 72`, `04 F0`, `04 F5`,
   `05 01`, `05 02`, `05 03`, `05 10`, `7F 03`, além de `0F` e `1F` no dongle (reset) e
   `07 10` (configurações gerais no dongle). Nenhum outro comando lateral além do já testado.
4. **Reset de fábrica oficial** (`fcn.00414870`, textos 167/168, tarefa 14 em `fcn.00434ee0`):
   no cabo não há comando de reset, o app só volta o banco local ao padrão e reenvia tudo
   (macros, teclas, cores por LED, iluminação, configurações gerais). No 2.4G envia o report
   `0F` (reset do firmware), espera 50 ms e reenvia a iluminação padrão. O "Reset Keyboard"
   (`fcn.00414600`, textos 165/166) usa a tarefa 6 no cabo e o report `1F` no dongle.
   Implementado como "Restaurar padrão de fábrica" na janela principal (só iluminação e o `0F`).

## Árvore de decisão

| Resultado | Conclusão | Próximo passo |
|---|---|---|
| T0 acende alguma zona | Fitas estão na matriz | Criar `ExtraZone` no Core e cor por zona na UI |
| T1 ou T3 mostram report no atalho | Firmware avisa o PC | Decodificar o report e procurar o comando inverso |
| T2 mostra byte mudando | Estado exposto | Mapear o byte; procurar o comando de escrita |
| Tudo negativo, T4 aponta o Sonix | Firmware principal controla, sem comando conhecido | T5 com análise do firmware; chance baixa |
| Tudo negativo, T4 aponta outro chip | Controlador separado | Encerrar: documentar os atalhos no app |

Firmware alternativo (SonixQMK) controlaria tudo, mas troca o firmware, perde o 2.4G e pode
inutilizar o teclado. Está fora do escopo pelas regras do projeto.

## Onde o REA entra

[REA](https://github.com/morluto/rea) é a ferramenta de engenharia reversa usada como guia.
Neste ambiente de nuvem roda pela CLI (`npx rea-agents`), sem motor nativo instalado.

| Material | Ferramenta REA |
|---|---|
| Captura `.pcapng` do T3 | `inspect_packet_capture` (TShark) |
| Gravação `.sr` do T4 | `decode_signal_recording` (sigrok, decodificador WS281x) |
| Instalador e `DeviceDriver.exe` do T5 | `open_binary` / `inspect_pe_resources`, análise nativa com Ghidra |
| Imagem de firmware do T5 | `inspect_firmware_regions` (Binwalk/Unblob) |

## Ferramentas do Laboratório usadas aqui

| Seção | O que faz | Envia algo? |
|---|---|---|
| 1. Monitor de atalhos | Reports HID de todas as coleções + teclas via Raw Input, com marcadores | Não |
| 4. Localizador de LED | Acende um índice por vez no modo `0x80` | Sim, iluminação |
| 5. Foto do estado | GET_REPORT de cada coleção com feature e diff com a foto anterior | Não (sem a opção) |
| 5. com respostas completas | Reaplica o efeito atual e guarda os ACKs inteiros | Sim, o efeito atual |
