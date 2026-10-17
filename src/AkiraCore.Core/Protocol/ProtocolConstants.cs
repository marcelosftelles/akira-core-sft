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
    public const int DongleLastChunkLength = 16;
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
