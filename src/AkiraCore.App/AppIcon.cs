namespace AkiraCore.App;

/// <summary>
/// Ícone do app (Assets/akira.ico) embutido como recurso. Carregar do recurso é mais confiável
/// que extrair do .exe e permite pedir o tamanho certo para a bandeja.
/// </summary>
internal static class AppIcon
{
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Assets/akira.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, size);
    }
}
