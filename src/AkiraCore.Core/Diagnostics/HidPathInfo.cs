using System.Text.RegularExpressions;

namespace AkiraCore.Core.Diagnostics;

/// <summary>
/// Extrai interface (MI_xx) e coleção (COLxx) do caminho HID do Windows, só
/// para rotular o log. Não serve para escolher o canal: isso é papel do
/// ChannelClassifier, porque o config.xml oficial erra a interface do cabo.
/// </summary>
public static partial class HidPathInfo
{
    [GeneratedRegex(@"[&#]mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfacePattern();

    [GeneratedRegex(@"[&#]col([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionPattern();

    /// <summary>"\\?\hid#vid_0c45&amp;pid_8006&amp;mi_01&amp;col02#..." vira "MI_01 COL02".</summary>
    public static string InterfaceLabel(string path)
    {
        var mi = InterfacePattern().Match(path);
        var col = CollectionPattern().Match(path);
        var label = mi.Success ? $"MI_{mi.Groups[1].Value.ToUpperInvariant()}" : "MI_?";
        return col.Success ? $"{label} COL{col.Groups[1].Value.ToUpperInvariant()}" : label;
    }
}
