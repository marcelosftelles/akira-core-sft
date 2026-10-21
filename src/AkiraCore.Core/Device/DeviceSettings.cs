using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Device;

/// <summary>Modo do Fn (fn_switch): o oficial grava 0 no botão "Momentâneo" e 1 no "Alternar" (fcn.00419910 e fcn.00419a90).</summary>
public enum FnMode : byte
{
    Momentary = 0,
    Toggle = 1,
}

/// <summary>Tempo para o teclado dormir: valores do combo de suspensão (fcn.0043b630, itens 142 a 145).</summary>
public enum SleepTimeout : byte
{
    Never = 0,
    OneMinute = 1,
    FiveMinutes = 2,
    ThirtyMinutes = 3,
}

/// <summary>
/// Configurações gerais do teclado (fcn.004141a0 no cabo, fcn.00414410 no dongle).
/// O oficial manda sempre todas juntas.
/// </summary>
/// <param name="KeyResponse">
/// Nível do tempo de resposta (debounce), 1 a 5: o combo oficial grava 1 a 5. Zero é o que o
/// oficial manda enquanto o usuário nunca escolheu (valor vazio vira "0"), e fica como "padrão".
/// </param>
public sealed record DeviceSettings(
    bool DisableWinKey,
    bool DisableAltF4,
    bool DisableAltTab,
    FnMode FnMode,
    SleepTimeout Sleep,
    int KeyResponse)
{
    public const int KeyResponseMax = 5;

    /// <summary>O que o oficial envia num banco recém-criado: tudo zerado.</summary>
    public static DeviceSettings Default { get; } =
        new(false, false, false, FnMode.Momentary, SleepTimeout.Never, 0);

    /// <exception cref="ArgumentException">Valor fora das opções do app oficial.</exception>
    public byte[] ToBlock()
    {
        if (!Enum.IsDefined(FnMode))
        {
            throw new ArgumentException("Modo do Fn desconhecido.", nameof(FnMode));
        }
        if (!Enum.IsDefined(Sleep))
        {
            throw new ArgumentException("Tempo de suspensão desconhecido.", nameof(Sleep));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(KeyResponse);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(KeyResponse, KeyResponseMax);

        var block = new byte[DeviceSettingsProtocol.BlockLength];
        block[1] = 0x01; // fixo: word 0x0100 em 0x414238
        block[DeviceSettingsProtocol.DisableAltTabIndex] = DisableAltTab ? (byte)1 : (byte)0;
        block[DeviceSettingsProtocol.DisableAltF4Index] = DisableAltF4 ? (byte)1 : (byte)0;
        block[DeviceSettingsProtocol.DisableWinIndex] = DisableWinKey ? (byte)1 : (byte)0;
        block[DeviceSettingsProtocol.FnModeIndex] = (byte)FnMode;
        block[DeviceSettingsProtocol.SleepIndex] = (byte)Sleep;
        block[DeviceSettingsProtocol.KeyResponseIndex] = (byte)KeyResponse;
        block[ProtocolConstants.SignatureIndex] = ProtocolConstants.SignatureLow;
        block[ProtocolConstants.SignatureIndex + 1] = ProtocolConstants.SignatureHigh;
        return block;
    }
}
