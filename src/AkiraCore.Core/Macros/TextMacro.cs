using System.Globalization;
using System.Text;
using AkiraCore.Core.Keys;

namespace AkiraCore.Core.Macros;

/// <summary>
/// Como digitar um caractere no layout atual do Windows: a tecla (código HID) e o que segurar.
/// <see cref="DeadKey"/>: a tecla só marca o acento e espera a próxima (´ ~ ^ no ABNT2 e no
/// US-Internacional).
/// </summary>
public readonly record struct KeyStroke(byte Usage, bool Shift = false, bool AltGr = false, bool DeadKey = false);

/// <summary>Layout de teclado consultado para digitar texto. O app usa o do Windows.</summary>
public interface ITextLayout
{
    KeyStroke? Find(char c);
}

/// <summary>Ações geradas e os caracteres que o layout não sabe digitar.</summary>
public sealed record TextMacroResult(IReadOnlyList<MacroEvent> Events, string Skipped);

/// <summary>
/// Transforma um texto em ações de macro, para o teclado digitá-lo sozinho, mesmo com o app
/// fechado. O "Enviar texto" do oficial (tipo 10, <c>05 02</c>) depende do app aberto; aqui o
/// texto vira teclas apertadas e soltas, guardadas no buffer de macros do firmware.
/// O teclado manda posições de tecla, então o resultado só vale para o layout em que foi gerado.
/// </summary>
public static class TextMacro
{
    public const int MaxLength = 400;

    private const byte ShiftLeft = 0xE1;
    private const byte AltRight = 0xE6; // AltGr nos layouts que têm
    private const byte Enter = 0x28;
    private const byte Tab = 0x2B;
    private const byte Space = 0x2C;

    // Acento combinante (Unicode) e a tecla de acento que o produz
    private static readonly Dictionary<char, char> Accents = new()
    {
        ['́'] = '´', ['̀'] = '`', ['̂'] = '^', ['̃'] = '~', ['̈'] = '¨',
    };

    /// <param name="delayMs">Pausa depois de cada ação (o firmware usa no mínimo 10 ms).</param>
    /// <exception cref="ArgumentException">Texto vazio ou longo demais.</exception>
    public static TextMacroResult Build(string text, ITextLayout layout, int delayMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(layout);
        if (text.Length == 0)
        {
            throw new ArgumentException("Digite o texto da macro.", nameof(text));
        }
        if (text.Length > MaxLength)
        {
            throw new ArgumentException($"O texto pode ter até {MaxLength} caracteres.", nameof(text));
        }

        var events = new List<MacroEvent>();
        var skipped = new StringBuilder();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            var strokes = Strokes(c, layout);
            if (strokes is null)
            {
                if (skipped.ToString().IndexOf(c) < 0)
                {
                    skipped.Append(c);
                }
                continue;
            }
            foreach (var stroke in strokes)
            {
                Press(events, stroke, delayMs);
            }
        }
        return new TextMacroResult(events, skipped.ToString());
    }

    private static IReadOnlyList<KeyStroke>? Strokes(char c, ITextLayout layout)
    {
        switch (c)
        {
            case '\n': return [new KeyStroke(Enter)];
            case '\t': return [new KeyStroke(Tab)];
            case '\r': return [];
        }

        if (Usable(layout.Find(c)) is { } direct)
        {
            // Tecla de acento sozinha só sai depois de um espaço
            return direct.DeadKey ? [direct, new KeyStroke(Space)] : [direct];
        }

        // Letra acentuada sem tecla própria: tecla de acento e depois a letra (á = ´ + a)
        var parts = c.ToString().Normalize(NormalizationForm.FormD);
        if (parts.Length == 2 && Accents.TryGetValue(parts[1], out var accentKey)
            && CharUnicodeInfo.GetUnicodeCategory(parts[0]) != UnicodeCategory.NonSpacingMark
            && Usable(layout.Find(accentKey)) is { DeadKey: true } accent
            && Usable(layout.Find(parts[0])) is { DeadKey: false } letter)
        {
            return [accent, letter];
        }
        return null;
    }

    // Só teclas que a macro aceita: o firmware recebe o código HID e a validação recusa o resto
    private static KeyStroke? Usable(KeyStroke? stroke) =>
        stroke is { } s && HidKeys.ByUsage(s.Usage) is not null ? s : null;

    private static void Press(List<MacroEvent> events, KeyStroke stroke, int delayMs)
    {
        if (stroke.AltGr)
        {
            events.Add(new MacroEvent(MacroEventKind.KeyDown, AltRight, delayMs));
        }
        if (stroke.Shift)
        {
            events.Add(new MacroEvent(MacroEventKind.KeyDown, ShiftLeft, delayMs));
        }
        events.Add(new MacroEvent(MacroEventKind.KeyDown, stroke.Usage, delayMs));
        events.Add(new MacroEvent(MacroEventKind.KeyUp, stroke.Usage, delayMs));
        if (stroke.Shift)
        {
            events.Add(new MacroEvent(MacroEventKind.KeyUp, ShiftLeft, delayMs));
        }
        if (stroke.AltGr)
        {
            events.Add(new MacroEvent(MacroEventKind.KeyUp, AltRight, delayMs));
        }
    }
}
