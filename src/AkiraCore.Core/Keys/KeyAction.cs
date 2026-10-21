namespace AkiraCore.Core.Keys;

/// <summary>Bits de modificador do slot de remapeamento (mesma ordem do relatório HID de teclado).</summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    LeftCtrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    LeftWin = 0x08,
    RightCtrl = 0x10,
    RightShift = 0x20,
    RightAlt = 0x40,
    RightWin = 0x80,
}

/// <summary>Teclas multimídia do app oficial (tipo 6 em fcn.00418550): código da página Consumer.</summary>
public enum MediaKey : byte
{
    PlayPause = 0xCD,
    Stop = 0xB7,
    Previous = 0xB6,
    Next = 0xB5,
    VolumeUp = 0xE9,
    VolumeDown = 0xEA,
    Mute = 0xE2,
}

/// <summary>Funções de mouse do app oficial (tipo 5 em fcn.00418550, itens 1 a 8 do combo).</summary>
public enum MouseFunction
{
    LeftClick,
    MiddleClick,
    RightClick,
    DoubleClick,
    ScrollUp,
    ScrollDown,
    Button4,
    Button5,
}

/// <summary>Como a macro toca (índice do combo em fcn.00412448, gravado no byte 2 do slot).</summary>
public enum MacroPlayback : byte
{
    Once = 0,
    Repeat = 1,          // N vezes, N no byte 3 (1 a 255)
    UntilPressedAgain = 2,
    WhileHeld = 3,
}

/// <summary>
/// O que uma tecla faz depois do remapeamento. Cada ação vira os 4 bytes do slot
/// da tecla na tabela de 576 bytes (fcn.00418550).
/// </summary>
public abstract record KeyAction
{
    public abstract byte[] ToSlot();
}

/// <summary>Volta a tecla ao comportamento original: slot zerado.</summary>
public sealed record DefaultKeyAction : KeyAction
{
    public static DefaultKeyAction Instance { get; } = new();

    public override byte[] ToSlot() => [0, 0, 0, 0];
}

/// <summary>"Desativar tecla" do menu oficial: tipo 1, que o oficial grava como 05 03 (fcn.00418550).</summary>
public sealed record DisabledKeyAction : KeyAction
{
    public static DisabledKeyAction Instance { get; } = new();

    public override byte[] ToSlot() => [0x05, 0x03, 0, 0];
}

/// <summary>
/// Outra tecla, com ou sem modificadores: 02, modificadores, código HID, 00.
/// Um modificador como destino (ex.: Caps Lock vira Ctrl) entra como bit, com código 0,
/// como o oficial faz quando não há outros modificadores (0x418760).
/// </summary>
public sealed record KeyComboAction : KeyAction
{
    public KeyComboAction(KeyModifiers modifiers, byte usage)
    {
        if (usage != 0 && HidKeys.ByUsage(usage) is null)
        {
            throw new ArgumentException($"Tecla de destino desconhecida: 0x{usage:X2}.", nameof(usage));
        }
        if (usage == 0 && modifiers == KeyModifiers.None)
        {
            throw new ArgumentException("Escolha uma tecla ou um modificador.", nameof(usage));
        }
        Modifiers = modifiers;
        Usage = usage;
    }

    public KeyModifiers Modifiers { get; }
    public byte Usage { get; }

    public override byte[] ToSlot()
    {
        var modifiers = Modifiers;
        var usage = Usage;
        if (HidKeys.IsModifier(usage))
        {
            modifiers |= (KeyModifiers)(1 << (usage - 0xE0));
            usage = 0;
        }
        return [0x02, (byte)modifiers, usage, 0];
    }
}

/// <summary>Tecla multimídia: 03, código Consumer, 00, 00.</summary>
public sealed record MediaKeyAction : KeyAction
{
    public MediaKeyAction(MediaKey key)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentException("Tecla multimídia desconhecida.", nameof(key));
        }
        Key = key;
    }

    public MediaKey Key { get; }

    public override byte[] ToSlot() => [0x03, (byte)Key, 0, 0];
}

/// <summary>
/// Função de mouse: 01 + dois bytes da tabela do oficial. 01 xx = botões (bits HID: 01 esquerdo,
/// 02 direito, 04 meio, 08 e 10 botões extras; 03 é o duplo clique); 03 01 / 03 FF = rolagem.
/// </summary>
public sealed record MouseKeyAction : KeyAction
{
    public MouseKeyAction(MouseFunction function)
    {
        if (!Enum.IsDefined(function))
        {
            throw new ArgumentException("Função de mouse desconhecida.", nameof(function));
        }
        Function = function;
    }

    public MouseFunction Function { get; }

    public override byte[] ToSlot() => Function switch
    {
        MouseFunction.LeftClick => [0x01, 0x01, 0x01, 0],
        MouseFunction.MiddleClick => [0x01, 0x01, 0x04, 0],
        MouseFunction.RightClick => [0x01, 0x01, 0x02, 0],
        MouseFunction.DoubleClick => [0x01, 0x01, 0x03, 0],
        MouseFunction.ScrollUp => [0x01, 0x03, 0x01, 0],
        MouseFunction.ScrollDown => [0x01, 0x03, 0xFF, 0],
        // O oficial chama o item 7 (bit 0x08) de "Botão 5" e o 8 (0x10) de "Botão 4"; pela
        // convenção HID, 0x08 é o botão 4 (voltar) e 0x10 o 5 (avançar). Confirmar em hardware.
        MouseFunction.Button4 => [0x01, 0x01, 0x08, 0],
        _ => [0x01, 0x01, 0x10, 0],
    };
}

/// <summary>
/// Dispara uma macro: 06, posição da macro na lista enviada, modo, repetições.
/// As repetições só valem no modo <see cref="MacroPlayback.Repeat"/>; nos outros vão zeradas.
/// </summary>
public sealed record MacroKeyAction : KeyAction
{
    public MacroKeyAction(int macroIndex, MacroPlayback playback, int repeat = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(macroIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(macroIndex, Protocol.MacroProtocol.SlotCount);
        if (!Enum.IsDefined(playback))
        {
            throw new ArgumentException("Modo de repetição desconhecido.", nameof(playback));
        }
        if (playback == MacroPlayback.Repeat)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(repeat, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(repeat, 255);
        }
        MacroIndex = macroIndex;
        Playback = playback;
        Repeat = playback == MacroPlayback.Repeat ? repeat : 0;
    }

    public int MacroIndex { get; }
    public MacroPlayback Playback { get; }
    public int Repeat { get; }

    public override byte[] ToSlot() => [0x06, (byte)MacroIndex, (byte)Playback, (byte)Repeat];
}
