using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Links;

public sealed class KeyboardException(string message) : Exception(message);

/// <summary>
/// Um canal de comunicação com o teclado. O conteúdo (bloco de iluminação)
/// é o mesmo; cada canal só sabe empacotar e confirmar do seu jeito.
/// </summary>
public interface IKeyboardLink : IDisposable
{
    string Description { get; }
    void SendLightBlock(ReadOnlySpan<byte> block, LightTarget target = LightTarget.Keys);

    /// <summary>Envia o buffer de 576 bytes com as cores de cada LED.</summary>
    void SendPerKeyColors(ReadOnlySpan<byte> buffer);

    /// <summary>Envia a tabela de 576 bytes de remapeamento de uma camada.</summary>
    void SendKeyRemap(KeyLayer layer, ReadOnlySpan<byte> table);

    /// <summary>Envia todas as macros: os dados até o fim do último evento, sem preenchimento.</summary>
    void SendMacros(ReadOnlySpan<byte> data);

    /// <summary>Envia o bloco de 16 bytes das configurações gerais.</summary>
    void SendDeviceSettings(ReadOnlySpan<byte> block);

    /// <summary>O canal tem o comando de reset de fábrica do próprio firmware (só o dongle).</summary>
    bool HasDeviceFactoryReset { get; }

    /// <summary>Reset de fábrica do firmware: apaga o que o teclado guardou (teclas, macros, luz).</summary>
    void SendFactoryReset();

    /// <summary>O canal informa o nível da bateria (só o dongle; o oficial não pergunta pelo cabo).</summary>
    bool HasBatteryQuery { get; }

    /// <summary>Pergunta o nível da bateria e devolve a resposta inteira, com Report ID no índice 0.</summary>
    byte[] QueryBattery();

    /// <summary>O canal lê as cores que os LEDs mostram agora (só o cabo, como no oficial).</summary>
    bool CanReadLedColors { get; }

    /// <summary>
    /// Lê o buffer de 576 bytes com a cor atual de cada LED, no formato do envio por LED.
    /// Null quando o teclado não confirma ou não manda nenhum pacote: o oficial só pula o quadro.
    /// </summary>
    byte[]? ReadLedColors();
}
