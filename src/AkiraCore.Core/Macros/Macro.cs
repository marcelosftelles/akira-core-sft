using AkiraCore.Core.Keys;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Macros;

public enum MacroEventKind
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
}

/// <summary>
/// Código do botão como vai no buffer. O app oficial guarda 1 (esquerdo), 2 (meio) e 3 (direito)
/// e converte para 01, 04 e 02 (fcn.0042d5d0, 0x42d886): os bits do relatório HID de mouse.
/// </summary>
public enum MouseButton : byte
{
    Left = 0x01,
    Right = 0x02,
    Middle = 0x04,
}

/// <summary>
/// Uma ação gravada e a pausa depois dela. Tecla: código HID; mouse: <see cref="MouseButton"/>.
/// </summary>
public sealed record MacroEvent
{
    public MacroEvent(MacroEventKind kind, byte code, int delayMs)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Tipo de ação desconhecido.", nameof(kind));
        }
        var isKey = kind is MacroEventKind.KeyDown or MacroEventKind.KeyUp;
        if (isKey && HidKeys.ByUsage(code) is null)
        {
            throw new ArgumentException($"Tecla desconhecida na macro: 0x{code:X2}.", nameof(code));
        }
        if (!isKey && !Enum.IsDefined((MouseButton)code))
        {
            throw new ArgumentException($"Botão de mouse desconhecido: {code}.", nameof(code));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(delayMs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delayMs, MacroProtocol.MaxDelayMs);
        Kind = kind;
        Code = code;
        DelayMs = delayMs;
    }

    public MacroEventKind Kind { get; }
    public byte Code { get; }
    public int DelayMs { get; }
}

public sealed record Macro(string Name, IReadOnlyList<MacroEvent> Events);

/// <summary>
/// Monta o buffer de macros do app oficial (fcn.0042d5d0 e fcn.0042db50 geram o mesmo buffer):
/// <list type="bullet">
/// <item>100 cabeçalhos de 4 bytes; o da macro i guarda a posição do bloco dela (16 bits).
/// Macro sem ações fica com FF FF FF FF; posições sem macro ficam zeradas.</item>
/// <item>A partir de 0x190, um bloco por macro: nº de eventos (16 bits), 6 bytes zerados e os
/// eventos de 4 bytes [atraso baixo, atraso alto, código, tipo].</item>
/// <item>Cada ação vira dois eventos: a ação (B0/30 tecla, 90/10 mouse) e um atraso (50) de no
/// mínimo 10 ms.</item>
/// </list>
/// </summary>
public static class MacroEncoder
{
    /// <summary>Quanto as macros ocupam, na conta que o oficial usa para recusar o envio.</summary>
    public static int Size(IReadOnlyList<Macro> macros) =>
        (macros.Count + macros.Sum(m => m.Events.Count)) * 8;

    /// <returns>Os dados até o fim do último bloco, sem preenchimento nem assinatura.</returns>
    /// <exception cref="ArgumentException">Mais de 100 macros ou acima do limite de tamanho.</exception>
    public static byte[] Encode(IReadOnlyList<Macro> macros)
    {
        if (macros.Count > MacroProtocol.SlotCount)
        {
            throw new ArgumentException($"O teclado guarda no máximo {MacroProtocol.SlotCount} macros.", nameof(macros));
        }
        if (Size(macros) > MacroProtocol.SizeLimit)
        {
            throw new ArgumentException("As macros não cabem na memória do teclado. Apague ações ou macros.", nameof(macros));
        }

        var buffer = new byte[MacroProtocol.BufferCapacity];
        var offset = MacroProtocol.DataStart;
        for (var i = 0; i < macros.Count; i++)
        {
            var events = macros[i].Events;
            if (events.Count == 0)
            {
                buffer.AsSpan(i * 4, 4).Fill(0xFF);
                continue;
            }
            WriteUInt16(buffer, i * 4, offset);
            WriteUInt16(buffer, offset, events.Count * 2);
            var position = offset + MacroProtocol.BlockHeaderLength;
            foreach (var e in events)
            {
                buffer[position + 2] = e.Code;
                buffer[position + 3] = e.Kind switch
                {
                    MacroEventKind.KeyDown => MacroProtocol.KeyDown,
                    MacroEventKind.KeyUp => MacroProtocol.KeyUp,
                    MacroEventKind.MouseDown => MacroProtocol.MouseDown,
                    _ => MacroProtocol.MouseUp,
                };
                position += MacroProtocol.EventLength;

                WriteUInt16(buffer, position, Math.Max(MacroProtocol.MinDelayMs, e.DelayMs));
                buffer[position + 3] = MacroProtocol.Delay;
                position += MacroProtocol.EventLength;
            }
            offset = position;
        }
        return buffer[..offset];
    }

    private static void WriteUInt16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
