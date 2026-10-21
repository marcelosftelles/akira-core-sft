using AkiraCore.Core.Device;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Live;
using AkiraCore.Core.Macros;
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

    /// <summary>
    /// Mesmo fluxo da "Redefinição de fábrica" oficial (fcn.00414870 → tarefa 14, fcn.00434ee0),
    /// na parte que este app controla. No dongle: reset do firmware (0F) e iluminação padrão.
    /// No cabo, onde não há comando de reset, a ordem do oficial: macros (vazias), teclas das
    /// duas camadas (sem remapeamento), iluminação e configurações gerais (zeradas, como num
    /// banco novo). O oficial ainda reenvia cores por LED e luz lateral, que ficam de fora.
    /// </summary>
    /// <param name="swapRedBlue">Mantém a correção de cor local do usuário.</param>
    public FactoryResetScope RestoreFactoryDefaults(bool swapRedBlue = false)
    {
        var lighting = FactoryDefaults.Lighting with { SwapRedBlue = swapRedBlue };
        if (link.HasDeviceFactoryReset)
        {
            link.SendFactoryReset();
            ApplyLighting(lighting);
            return FactoryResetScope.DeviceAndLighting;
        }

        ApplyMacros([]);
        ApplyKeyRemap(new KeyRemapTable(), new KeyRemapTable());
        ApplyLighting(lighting);
        ApplyDeviceSettings(DeviceSettings.Default);
        return FactoryResetScope.Defaults;
    }

    /// <summary>
    /// Nível da bateria em porcentagem, como o oficial lê (fcn.004357d0). Null quando o canal não
    /// informa (cabo) ou a resposta não traz nível.
    /// </summary>
    public int? ReadBatteryLevel() =>
        link.HasBatteryQuery ? PacketBuilder.DongleBatteryLevel(link.QueryBattery()) : null;

    /// <summary>Quadro do fluxo em tempo real (04 20, só cabo). Vai direto aos LEDs, sem gravar.</summary>
    public void SendLiveFrame(LiveFrame frame, bool swapRedBlue = false) =>
        link.SendStreamFrame(frame.ToStreamFrame(swapRedBlue));

    /// <summary>
    /// Cores que os LEDs mostram agora (04 F5, fcn.0042adb0). Null no dongle, que não tem a leitura.
    /// </summary>
    public IReadOnlyDictionary<int, Rgb>? ReadLedColors(bool swapRedBlue = false)
    {
        if (!link.HasColorReadback)
        {
            return null;
        }
        var colors = PacketBuilder.ParseLedColors(link.ReadLedColors());
        return swapRedBlue ? colors.ToDictionary(c => c.Key, c => ColorMath.SwapRedBlue(c.Value)) : colors;
    }

    /// <summary>
    /// Ritmo musical desenhado pelo firmware do dongle (fcn.00432b00). As alturas já vêm na escala
    /// e no número de colunas escolhidos (ver <see cref="MusicProtocol"/>).
    /// </summary>
    public void SendMusicHeights(ReadOnlySpan<byte> heights, int brightness, byte frontMode = 0, byte backMode = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(brightness, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(brightness, ProtocolConstants.BrightnessMax);
        link.SendMusicReport(PacketBuilder.DongleMusic(frontMode, backMode, (byte)brightness, heights));
    }

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
    /// Remapeamento das duas camadas, como a tarefa 6 do oficial (fcn.00434840): normal e depois Fn.
    /// Tecla sem ação na tabela volta ao comportamento original.
    /// </summary>
    public void ApplyKeyRemap(KeyRemapTable normal, KeyRemapTable fn)
    {
        link.SendKeyRemap(KeyLayer.Normal, normal.ToBuffer());
        link.SendKeyRemap(KeyLayer.Fn, fn.ToBuffer());
    }

    /// <summary>Grava todas as macros; a posição de cada uma é a usada pelas teclas que a disparam.</summary>
    public void ApplyMacros(IReadOnlyList<Macro> macros) => link.SendMacros(MacroEncoder.Encode(macros));

    public void ApplyDeviceSettings(DeviceSettings settings) => link.SendDeviceSettings(settings.ToBlock());

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
