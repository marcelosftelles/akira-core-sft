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
}
