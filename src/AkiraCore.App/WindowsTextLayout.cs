using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Macros;

namespace AkiraCore.App;

/// <summary>
/// O layout de teclado ativo no Windows, para transformar texto em teclas. O teclado manda
/// posições de tecla, então "ç" só sai certo no mesmo layout em que a macro foi gerada.
/// </summary>
internal sealed class WindowsTextLayout : ITextLayout
{
    private const uint MapVkToVscEx = 4;
    private const uint DoNotChangeKeyboardState = 0x4; // Windows 10 1607+: consulta sem mexer no acento pendente
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    // Nomes dos layouts mais comuns por aqui; os outros usam o nome do idioma
    private static readonly Dictionary<string, string> KnownLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["00000416"] = "Português (Brasil ABNT)",
        ["00010416"] = "Português (Brasil ABNT2)",
        ["00000409"] = "Inglês (EUA)",
        ["00020409"] = "Estados Unidos-Internacional",
        ["00000816"] = "Português (Portugal)",
    };

    private readonly IntPtr _layout;

    private WindowsTextLayout(IntPtr layout, string name)
    {
        _layout = layout;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Layout ativo na thread da interface (o mesmo que o usuário está usando no app).</summary>
    public static WindowsTextLayout Current()
    {
        var layout = GetKeyboardLayout(0);
        var id = new StringBuilder(9);
        var name = GetKeyboardLayoutNameW(id) && KnownLayouts.TryGetValue(id.ToString(), out var known)
            ? known
            : LanguageName((int)(layout.ToInt64() & 0xFFFF));
        return new WindowsTextLayout(layout, name);
    }

    public KeyStroke? Find(char c)
    {
        var result = VkKeyScanExW(c, _layout);
        if (result == -1)
        {
            return null;
        }
        var vk = (uint)(result & 0xFF);
        var state = (result >> 8) & 0xFF;
        var shift = (state & 1) != 0;
        var ctrl = (state & 2) != 0;
        var alt = (state & 4) != 0;
        // Ctrl ou Alt sozinhos são atalhos, não texto; Ctrl + Alt é o AltGr
        if ((state & ~0x07) != 0 || ctrl != alt)
        {
            return null;
        }
        var scan = MapVirtualKeyExW(vk, MapVkToVscEx, _layout);
        if (scan == 0 || ScanCodes.ToUsage((int)scan) is not { } usage)
        {
            return null;
        }
        return new KeyStroke(usage, shift, ctrl && alt, IsDeadKey(vk, scan, shift, ctrl && alt));
    }

    // ToUnicodeEx devolve -1 para tecla de acento; pela combinação exata (o ^ do US-Internacional
    // é Shift + 6, e o 6 sozinho não é acento)
    private bool IsDeadKey(uint vk, uint scan, bool shift, bool altGr)
    {
        var keys = new byte[256];
        if (shift)
        {
            keys[VkShift] = 0x80;
        }
        if (altGr)
        {
            keys[VkControl] = 0x80;
            keys[VkMenu] = 0x80;
        }
        var buffer = new StringBuilder(8);
        return ToUnicodeEx(vk, scan & 0xFF, keys, buffer, buffer.Capacity, DoNotChangeKeyboardState, _layout) < 0;
    }

    private static string LanguageName(int languageId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageId).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return $"layout 0x{languageId:X4}";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutNameW(StringBuilder name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanExW(char ch, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        StringBuilder buffer, int bufferSize, uint flags, IntPtr layout);
}
