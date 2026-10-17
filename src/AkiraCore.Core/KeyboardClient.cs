using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core;

/// <summary>
/// Camada de domínio: transforma intenções ("aplicar respiração verde")
/// em blocos de iluminação. Não sabe se o canal é cabo ou dongle.
/// </summary>
public sealed class KeyboardClient(IKeyboardLink link)
{
    public void ApplyLighting(LightingConfig config) => link.SendLightBlock(config.ToLightBlock());

    public void SetSideLight(SideLightMode mode) =>
        link.SendLightBlock(PacketBuilder.SideLightBlock(mode.Value), LightTarget.SideStrips);

    /// <summary>
    /// Envia um bloco montado à mão, para experimentos. Só aceita o tamanho do
    /// bloco de iluminação e força a assinatura: nunca vira outro comando.
    /// </summary>
    public void SendExperimentalBlock(byte[] block, LightTarget target)
    {
        if (block.Length != ProtocolConstants.LightBlockLength)
        {
            throw new ArgumentException($"O bloco precisa ter {ProtocolConstants.LightBlockLength} bytes.", nameof(block));
        }
        var copy = (byte[])block.Clone();
        copy[ProtocolConstants.SignatureIndex] = ProtocolConstants.SignatureLow;
        copy[ProtocolConstants.SignatureIndex + 1] = ProtocolConstants.SignatureHigh;
        link.SendLightBlock(copy, target);
    }

    /// <summary>
    /// Modo personalizado: liga o modo 0x80 e envia a cor de cada LED.
    /// Neste modo as teclas deixam de seguir efeitos e ficam com cores fixas.
    /// </summary>
    public void ApplyPerKeyColors(PerKeyFrame frame, int brightness = ProtocolConstants.BrightnessMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(brightness, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(brightness, ProtocolConstants.BrightnessMax);
        link.SendLightBlock(PacketBuilder.CustomModeBlock(brightness));
        link.SendPerKeyColors(frame.ToBuffer());
    }
}
