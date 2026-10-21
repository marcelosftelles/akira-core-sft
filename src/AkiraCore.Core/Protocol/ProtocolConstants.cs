namespace AkiraCore.Core.Protocol;

/// <summary>
/// Valores extraídos do DeviceDriver.exe oficial (v1.0.0.0) e do rgb-keyboard.xml.
/// </summary>
public static class ProtocolConstants
{
    // Bloco de iluminação: mesmo conteúdo nos dois canais, só muda o envelope
    public const int LightBlockLength = 16;
    public const int SignatureIndex = 14;
    public const byte SignatureLow = 0xAA;
    public const byte SignatureHigh = 0x55;
    // Modo 0x80 = iluminação personalizada por LED (fcn.0044b790 e fcn.0044bd20).
    // Antes eu tinha lido como "só brilho"; o fluxo de cores por tecla mostrou o real papel.
    public const byte CustomModeFlag = 0x80;
    public const int BrightnessIndex = 9;
    public const int BrightnessMax = 5;
    public const int SpeedMax = 5;

    public const byte ReportId = 0x00;
}

/// <summary>Canal com fio: feature reports de 65 bytes em transação (fcn.0044ed60).</summary>
public static class FeatureProtocol
{
    public const int ReportLength = 65;      // Report ID + 64 bytes de payload
    public const int PayloadLength = 64;
    public const byte CommandHeader = 0x04;
    public const int AckIndex = 4;           // o app oficial confere report[4] == 1
    public static readonly TimeSpan CommandDelay = TimeSpan.FromMilliseconds(35);
}

/// <summary>Qual conjunto de LEDs o bloco controla.</summary>
public enum LightTarget
{
    Keys,       // iluminação das teclas
    SideStrips, // fitas laterais
}

public enum Opcode : byte
{
    Begin = 0x18,
    End = 0x02,
    Finalize = 0xF0,   // sempre enviado no fluxo oficial; função exata desconhecida
    Lighting = 0x13,
    PerKeyColors = 0x23,
    KeyRemap = 0x11,       // camada normal (fcn.00418550 com parâmetro 0)
    FnKeyRemap = 0x27,     // camada Fn (fcn.00418550 com parâmetro 1)
    DeviceSettings = 0x17, // fcn.004141a0
    MacroBegin = 0x19,     // fcn.0042d5d0; abre a gravação de macros sem o 04 18
    MacroData = 0x15,      // fcn.0042d5d0; [8] = nº de pacotes de 64 bytes
}

/// <summary>Camada do remapeamento: a normal ou a ativa enquanto o Fn está segurado.</summary>
public enum KeyLayer
{
    Normal,
    Fn,
}

/// <summary>
/// Remapeamento de teclas (fcn.00418550 no cabo, fcn.00418df0 e fcn.00419130 no dongle).
/// Mesmo formato do buffer de cores por LED: 576 bytes, 4 bytes por tecla na posição
/// key_index × 4, AA 55 no fim. O app oficial zera 0x30C bytes mas só envia 0x240.
/// </summary>
public static class KeyRemapProtocol
{
    public const int BufferLength = 576;
    public const int SignatureOffset = 574;
    public const int FeatureChunks = 9;
    public const byte DongleHeader = 0x10;        // camada normal; a Fn usa 0x12 (0x10 + 2 × camada)
    public const byte DongleFnHeader = 0x12;
    // No dongle a tarefa 6 dorme 10 ms antes de enviar as camadas (fcn.00434840)
    public static readonly TimeSpan DongleSettleDelay = TimeSpan.FromMilliseconds(10);
}

/// <summary>
/// Macros (fcn.0042d5d0 no cabo, fcn.0042db50 no dongle). Buffer de até 0xE00 bytes:
/// 100 cabeçalhos de 4 bytes com a posição de cada macro e, a partir de 0x190, os eventos.
/// </summary>
public static class MacroProtocol
{
    public const int BufferCapacity = 0xE00;
    public const int SlotCount = 100;
    public const int DataStart = 0x190;
    public const int BlockHeaderLength = 8;      // nº de eventos (16 bits) + 6 bytes zerados
    public const int EventLength = 4;
    public const int MinDelayMs = 10;            // atrasos menores viram 10 ms (cmovg em 0x42d916)
    public const int MaxDelayMs = 0xFFFF;        // o atraso ocupa 16 bits
    // Limite do app oficial: (nº de macros + nº de ações gravadas) × 8 ≤ 0xA8C no cabo.
    // No dongle o limite é 0xE10, mas usamos o do cabo nos dois para caber em qualquer canal.
    public const int SizeLimit = 0xA8C;
    public const byte KeyDown = 0xB0;
    public const byte KeyUp = 0x30;
    public const byte MouseDown = 0x90;
    public const byte MouseUp = 0x10;
    public const byte Delay = 0x50;
    public static readonly TimeSpan FeatureStepDelay = TimeSpan.FromMilliseconds(30);
    public const byte DongleHeader = 0x09;
    public const int DongleChunkData = 28;
    public static readonly TimeSpan DongleChunkDelay = TimeSpan.FromMilliseconds(2);
}

/// <summary>
/// Configurações gerais (fcn.004141a0 no cabo, fcn.00414410 no dongle). Bloco de 16 bytes:
/// [0] 00, [1] 01, [2] desliga Alt+Tab, [3] desliga Alt+F4, [4] desliga Win, [5] modo do Fn,
/// [6] suspensão, [8] tempo de resposta. No cabo o AA 55 fica no fim do payload (62);
/// no dongle, no fim do bloco (14), como no bloco de iluminação.
/// </summary>
public static class DeviceSettingsProtocol
{
    public const int BlockLength = 16;
    public const int DisableAltTabIndex = 2;
    public const int DisableAltF4Index = 3;
    public const int DisableWinIndex = 4;
    public const int FnModeIndex = 5;
    public const int SleepIndex = 6;
    public const int KeyResponseIndex = 8;
    public const int FeatureSignatureOffset = 62;
    public static readonly byte[] DongleHeader = [0x07, 0x10, 0x00];
}

/// <summary>
/// Cores por LED (fcn.0044b8a0 no cabo, fcn.0044bd20 no dongle).
/// Buffer de 576 bytes: cada LED ocupa 4 bytes [índice, R, G, B] na posição índice × 4.
/// </summary>
public static class PerKeyProtocol
{
    public const int BufferLength = 576;
    public const int SignatureOffset = 574;       // AA 55 nos dois últimos bytes
    public const int MaxLedIndex = 142;           // o slot 143 colide com a assinatura
    public const int FeatureChunks = 9;           // 9 feature reports de 64 bytes
    public const byte DongleHeader = 0x14;
    public const int DongleChunkData = 28;
    public const int DongleChunks = 21;           // 20 × 28 + 1 × 16 = 576
    public static readonly TimeSpan DongleChunkDelay = TimeSpan.FromMilliseconds(2);
}

/// <summary>Canal do dongle 2.4G: output report de 33 bytes com checksum (fcn.0044f480).</summary>
public static class DongleProtocol
{
    public const int ReportLength = 33;      // Report ID + 32 bytes
    public static readonly byte[] LightingHeader = [0x05, 0x10, 0x00];   // fcn.0042b170
    public static readonly byte[] SideLightHeader = [0x05, 0x01, 0x00];  // fcn.00434900
    public const int BlockOffset = 4;        // bloco de iluminação começa no byte 4
    // "Redefinição de fábrica" oficial no 2.4G (fcn.00414870 → tarefa 14, fcn.00434ee0):
    // report único com 0F, depois Sleep(50) e a iluminação padrão. No cabo não existe
    // comando equivalente: o app oficial só reenvia os padrões.
    public const byte FactoryResetCommand = 0x0F;
    public static readonly TimeSpan FactoryResetSettleDelay = TimeSpan.FromMilliseconds(50);
    public const int ChecksumIndex = 32;     // soma dos bytes anteriores, 8 bits
    public const int ReadAttempts = 20;
    public static readonly TimeSpan WriteRetryDelay = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan PostWriteDelay = TimeSpan.FromMilliseconds(5);
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(5);
    public static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(10);
}

/// <summary>
/// Nível da bateria, só no 2.4G (fcn.004357d0, chamada a cada 15 s e ao conectar):
/// report <c>20 01</c> pela transação normal do dongle (fcn.0044f480). A resposta ecoa
/// <c>20 01 00</c> e traz o nível em porcentagem no byte seguinte; 0 é ignorado e acima
/// de 100 vira 100. O oficial não lê estado de carga: chama SetBatteryInfo(nível, 0)
/// sempre, e o 0 é justamente "sem o raio de carregando" no BatteryCtrl da mui.dll.
/// </summary>
public static class BatteryProtocol
{
    public static readonly byte[] DongleQuery = [0x20, 0x01];
    public const int ResponseCommandIndex = 1;  // Report ID no índice 0; o oficial lê [0] sem ele
    public const int ResponseLevelIndex = 4;    // o oficial lê [3] sem o Report ID
    public const int MaxLevel = 100;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
}
