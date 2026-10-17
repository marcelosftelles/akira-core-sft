using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AkiraCore.App.Web;

/// <summary>
/// Barra de título no mesmo tom da barra lateral da interface (Windows 11).
/// No Windows 10 os atributos novos são ignorados e a janela fica com a barra padrão.
/// </summary>
internal static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwcpRound = 2;

    // Mesmos valores de --chrome e --ink do app.css
    public static Color Chrome(bool dark) => dark ? Color.FromArgb(0x23, 0x23, 0x25) : Color.FromArgb(0xEC, 0xEC, 0xEE);
    public static Color Ground(bool dark) => dark ? Color.FromArgb(0x1C, 0x1C, 0x1E) : Color.FromArgb(0xF5, 0xF5, 0xF7);
    private static Color Ink(bool dark) => dark ? Color.FromArgb(0xF5, 0xF5, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);

    /// <summary>Tema "Modo do app" do Windows (Configurações › Personalização › Cores).</summary>
    public static bool IsDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    public static void Apply(IntPtr hwnd, bool dark)
    {
        Set(hwnd, DwmwaUseImmersiveDarkMode, dark ? 1 : 0);
        Set(hwnd, DwmwaWindowCornerPreference, DwmwcpRound);
        Set(hwnd, DwmwaCaptionColor, ToColorRef(Chrome(dark)));
        Set(hwnd, DwmwaBorderColor, ToColorRef(Chrome(dark)));
        Set(hwnd, DwmwaTextColor, ToColorRef(Ink(dark)));
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static void Set(IntPtr hwnd, int attribute, int value) =>
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
