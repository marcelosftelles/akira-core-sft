using System.Reflection;
using Microsoft.Win32;

namespace AkiraCore.App;

/// <summary>
/// Ícones embutidos (pasta Assets, gerados por tools/icones/gerar-icones.mjs): o do app,
/// colorido, e o da bandeja, numa cor só, que segue o tema da barra de tarefas.
/// </summary>
internal static class AppIcons
{
    /// <summary>Ícone do app com todos os tamanhos (janela, barra de tarefas, Alt + Tab).</summary>
    public static Icon App() => Load("AkiraCore.ico", null);

    /// <summary>Ícone da bandeja no tamanho que o Windows usa nela, claro ou escuro.</summary>
    public static Icon Tray() =>
        Load(IsDarkTaskbar() ? "tray-white.ico" : "tray-black.ico", SystemInformation.SmallIconSize);

    /// <summary>
    /// Tema da barra de tarefas ("Modo do Windows"), que pode ser diferente do tema dos apps.
    /// </summary>
    public static bool IsDarkTaskbar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        // Sem a chave (Windows antigo) a barra é escura
        return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
    }

    private static Icon Load(string name, Size? size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Assets/" + name)
            ?? throw new InvalidOperationException($"Ícone {name} não está embutido no executável");
        return size is { } s ? new Icon(stream, s) : new Icon(stream);
    }
}
