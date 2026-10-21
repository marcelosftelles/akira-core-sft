namespace AkiraCore.Core.Keys;

/// <summary>
/// Uma tecla do teclado padrão USB (página 0x07). <see cref="Code"/> é o
/// KeyboardEvent.code do navegador, usado pela interface para gravar macros.
/// </summary>
public sealed record HidKey(string Code, byte Usage, string Label, string Group);

/// <summary>
/// Teclas que o app aceita como destino de remapeamento e em macros. Os códigos são os
/// de uso HID (USB HID Usage Tables, página Keyboard/Keypad), os mesmos que o app oficial
/// grava nas tabelas (fcn.00451400 converte o código virtual do Windows nesses valores).
/// </summary>
public static class HidKeys
{
    public static IReadOnlyList<HidKey> All { get; } = Build();

    private static readonly Dictionary<byte, HidKey> ByUsageMap = All.ToDictionary(k => k.Usage);
    private static readonly Dictionary<string, HidKey> ByCodeMap = All.ToDictionary(k => k.Code);

    public static HidKey? ByUsage(byte usage) => ByUsageMap.GetValueOrDefault(usage);

    public static HidKey? ByCode(string code) => ByCodeMap.GetValueOrDefault(code);

    /// <summary>Ctrl, Shift, Alt e Win (0xE0..0xE7): no remapeamento viram bits de modificador.</summary>
    public static bool IsModifier(byte usage) => usage is >= 0xE0 and <= 0xE7;

    private static List<HidKey> Build()
    {
        var keys = new List<HidKey>();
        void Add(string code, int usage, string label, string group) => keys.Add(new(code, (byte)usage, label, group));

        for (var i = 0; i < 26; i++)
        {
            var letter = (char)('A' + i);
            Add($"Key{letter}", 0x04 + i, letter.ToString(), "Letras");
        }
        for (var i = 1; i <= 9; i++)
        {
            Add($"Digit{i}", 0x1D + i, i.ToString(), "Números");
        }
        Add("Digit0", 0x27, "0", "Números");

        Add("Enter", 0x28, "Enter", "Edição");
        Add("Escape", 0x29, "Esc", "Edição");
        Add("Backspace", 0x2A, "Backspace", "Edição");
        Add("Tab", 0x2B, "Tab", "Edição");
        Add("Space", 0x2C, "Espaço", "Edição");
        Add("CapsLock", 0x39, "Caps Lock", "Edição");
        Add("Insert", 0x49, "Insert", "Edição");
        Add("Delete", 0x4C, "Delete", "Edição");
        Add("ContextMenu", 0x65, "Menu", "Edição");

        Add("Minus", 0x2D, "-", "Símbolos");
        Add("Equal", 0x2E, "=", "Símbolos");
        Add("BracketLeft", 0x2F, "[", "Símbolos");
        Add("BracketRight", 0x30, "]", "Símbolos");
        Add("Backslash", 0x31, "\\", "Símbolos");
        Add("Semicolon", 0x33, ";", "Símbolos");
        Add("Quote", 0x34, "'", "Símbolos");
        Add("Backquote", 0x35, "`", "Símbolos");
        Add("Comma", 0x36, ",", "Símbolos");
        Add("Period", 0x37, ".", "Símbolos");
        Add("Slash", 0x38, "/", "Símbolos");
        // Teclas que só existem no ABNT2: \| ao lado do Shift esquerdo e /? ao lado do direito
        Add("IntlBackslash", 0x64, "\\ (ABNT2)", "Símbolos");
        Add("IntlRo", 0x87, "/ (ABNT2)", "Símbolos");

        for (var i = 1; i <= 12; i++)
        {
            Add($"F{i}", 0x39 + i, $"F{i}", "Funções");
        }
        for (var i = 13; i <= 24; i++)
        {
            Add($"F{i}", 0x5B + i, $"F{i}", "Funções");
        }
        Add("PrintScreen", 0x46, "Print Screen", "Funções");
        Add("ScrollLock", 0x47, "Scroll Lock", "Funções");
        Add("Pause", 0x48, "Pause", "Funções");

        Add("Home", 0x4A, "Home", "Navegação");
        Add("PageUp", 0x4B, "Page Up", "Navegação");
        Add("End", 0x4D, "End", "Navegação");
        Add("PageDown", 0x4E, "Page Down", "Navegação");
        Add("ArrowRight", 0x4F, "→", "Navegação");
        Add("ArrowLeft", 0x50, "←", "Navegação");
        Add("ArrowDown", 0x51, "↓", "Navegação");
        Add("ArrowUp", 0x52, "↑", "Navegação");

        Add("NumLock", 0x53, "Num Lock", "Teclado numérico");
        Add("NumpadDivide", 0x54, "Num /", "Teclado numérico");
        Add("NumpadMultiply", 0x55, "Num *", "Teclado numérico");
        Add("NumpadSubtract", 0x56, "Num -", "Teclado numérico");
        Add("NumpadAdd", 0x57, "Num +", "Teclado numérico");
        Add("NumpadEnter", 0x58, "Num Enter", "Teclado numérico");
        for (var i = 1; i <= 9; i++)
        {
            Add($"Numpad{i}", 0x58 + i, $"Num {i}", "Teclado numérico");
        }
        Add("Numpad0", 0x62, "Num 0", "Teclado numérico");
        Add("NumpadDecimal", 0x63, "Num ,", "Teclado numérico");

        Add("ControlLeft", 0xE0, "Ctrl esquerdo", "Modificadores");
        Add("ShiftLeft", 0xE1, "Shift esquerdo", "Modificadores");
        Add("AltLeft", 0xE2, "Alt esquerdo", "Modificadores");
        Add("MetaLeft", 0xE3, "Win esquerdo", "Modificadores");
        Add("ControlRight", 0xE4, "Ctrl direito", "Modificadores");
        Add("ShiftRight", 0xE5, "Shift direito", "Modificadores");
        Add("AltRight", 0xE6, "Alt direito", "Modificadores");
        Add("MetaRight", 0xE7, "Win direito", "Modificadores");
        return keys;
    }
}
