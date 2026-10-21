using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Automation;

/// <summary>Modificadores do RegisterHotKey do Windows (MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// Um atalho global: funciona com qualquer janela em foco. <see cref="Key"/> é o KeyboardEvent.code
/// da tecla (ex.: "KeyL", "F5"), o mesmo que a interface já usa nas macros.
/// </summary>
public sealed record HotkeyState(string Action, int Modifiers, string Key);

public static class Hotkeys
{
    public static IReadOnlyList<Option<string>> Actions { get; } =
    [
        new("toggleLight", "Ligar ou desligar a luz", "toggleLight"),
        new("nextEffect", "Próximo efeito", "nextEffect"),
        new("previousEffect", "Efeito anterior", "previousEffect"),
        new("brightnessUp", "Mais brilho", "brightnessUp"),
        new("brightnessDown", "Menos brilho", "brightnessDown"),
        new("nextProfile", "Próximo perfil", "nextProfile"),
        new("previousProfile", "Perfil anterior", "previousProfile"),
        new("liveToggle", "Ligar ou desligar o modo ao vivo", "liveToggle"),
        new("showWindow", "Abrir o AkiraCore", "showWindow"),
    ];

    private static readonly Dictionary<string, int> VirtualKeys = BuildVirtualKeys();

    /// <summary>Código virtual do Windows (VK_*) da tecla, ou null se não for aceita como atalho.</summary>
    public static int? VirtualKey(string? code) => code is not null && VirtualKeys.TryGetValue(code, out var vk) ? vk : null;

    /// <summary>"Ctrl + Alt + L" para mostrar na tela e nos avisos.</summary>
    public static string Describe(HotkeyState hotkey)
    {
        var mods = (HotkeyModifiers)hotkey.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyLabel(hotkey.Key));
        return string.Join(" + ", parts);
    }

    /// <exception cref="ArgumentException">Ação desconhecida, tecla não aceita, sem Ctrl/Alt/Win ou repetido.</exception>
    public static void ValidateAll(IReadOnlyList<HotkeyState> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        var combos = new HashSet<(int, string)>();
        var actions = new HashSet<string>();
        foreach (var hotkey in hotkeys)
        {
            if (hotkey is null || Actions.All(a => a.Key != hotkey.Action))
            {
                throw new ArgumentException($"Ação de atalho desconhecida: {hotkey?.Action}.");
            }
            if (!actions.Add(hotkey.Action))
            {
                throw new ArgumentException("Cada ação aceita um atalho só.");
            }
            if (VirtualKey(hotkey.Key) is null)
            {
                throw new ArgumentException($"Essa tecla não serve de atalho: {hotkey.Key}.");
            }
            var mods = (HotkeyModifiers)hotkey.Modifiers;
            if (hotkey.Modifiers is < 0 or > 0xF
                || (mods & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            {
                throw new ArgumentException("O atalho precisa de Ctrl, Alt ou Win, para não roubar uma tecla comum.");
            }
            if (IsReserved(hotkey.Key))
            {
                throw new ArgumentException("F13 a F24 ficam reservadas para as teclas com ação do app.");
            }
            if (!combos.Add((hotkey.Modifiers, hotkey.Key)))
            {
                throw new ArgumentException($"{Describe(hotkey)} está em duas ações.");
            }
        }
    }

    /// <summary>F13 a F24: o app as usa para as teclas que abrem programa, site etc.</summary>
    public static bool IsReserved(string? code) =>
        code is ['F', _, _] && int.TryParse(code.AsSpan(1), out var n) && n is >= 13 and <= 24;

    private static string KeyLabel(string code) => code switch
    {
        _ when code.StartsWith("Key", StringComparison.Ordinal) => code[3..],
        _ when code.StartsWith("Digit", StringComparison.Ordinal) => code[5..],
        _ when code.StartsWith("Numpad", StringComparison.Ordinal) => "Num " + code[6..],
        "ArrowUp" => "↑",
        "ArrowDown" => "↓",
        "ArrowLeft" => "←",
        "ArrowRight" => "→",
        "Space" => "Espaço",
        _ => code,
    };

    private static Dictionary<string, int> BuildVirtualKeys()
    {
        var map = new Dictionary<string, int>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[$"Key{c}"] = c;
        }
        for (var d = 0; d <= 9; d++)
        {
            map[$"Digit{d}"] = 0x30 + d;
            map[$"Numpad{d}"] = 0x60 + d;
        }
        for (var f = 1; f <= 24; f++)
        {
            map[$"F{f}"] = 0x6F + f;
        }
        map["NumpadMultiply"] = 0x6A;
        map["NumpadAdd"] = 0x6B;
        map["NumpadSubtract"] = 0x6D;
        map["NumpadDecimal"] = 0x6E;
        map["NumpadDivide"] = 0x6F;
        map["ArrowLeft"] = 0x25;
        map["ArrowUp"] = 0x26;
        map["ArrowRight"] = 0x27;
        map["ArrowDown"] = 0x28;
        map["PageUp"] = 0x21;
        map["PageDown"] = 0x22;
        map["End"] = 0x23;
        map["Home"] = 0x24;
        map["Insert"] = 0x2D;
        map["Delete"] = 0x2E;
        map["Space"] = 0x20;
        map["Enter"] = 0x0D;
        map["Escape"] = 0x1B;
        map["Backspace"] = 0x08;
        map["Tab"] = 0x09;
        map["Pause"] = 0x13;
        map["PrintScreen"] = 0x2C;
        map["ScrollLock"] = 0x91;
        map["Semicolon"] = 0xBA;
        map["Equal"] = 0xBB;
        map["Comma"] = 0xBC;
        map["Minus"] = 0xBD;
        map["Period"] = 0xBE;
        map["Slash"] = 0xBF;
        map["Backquote"] = 0xC0;
        map["IntlRo"] = 0xC1;
        map["BracketLeft"] = 0xDB;
        map["Backslash"] = 0xDC;
        map["BracketRight"] = 0xDD;
        map["Quote"] = 0xDE;
        map["IntlBackslash"] = 0xE2;
        return map;
    }
}
