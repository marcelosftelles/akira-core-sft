using System.Reflection;
using System.Text.RegularExpressions;

namespace AkiraCore.App.Web;

/// <summary>
/// Serve os arquivos da pasta Ui, embutidos no executável, num endereço que não sai
/// da máquina. Nada é baixado da internet: a página só carrega o que está aqui.
/// </summary>
public static partial class EmbeddedUi
{
    public const string Origin = "https://akira.ui";
    public const string StartPage = Origin + "/index.html";

    // Só nomes simples: impede pedir qualquer outro recurso do assembly
    [GeneratedRegex(@"^/([a-z0-9-]+\.(html|css|js))$")]
    private static partial Regex AllowedPath();

    public static bool IsOwnUrl(string uri) =>
        uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Retorna o conteúdo e o Content-Type, ou null se não for um arquivo da interface.</summary>
    public static (Stream Content, string ContentType)? Open(string uri)
    {
        if (!IsOwnUrl(uri))
        {
            return null;
        }
        var match = AllowedPath().Match(new Uri(uri).AbsolutePath);
        if (!match.Success)
        {
            return null;
        }
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ui/" + match.Groups[1].Value);
        if (stream is null)
        {
            return null;
        }
        var type = match.Groups[2].Value switch
        {
            "html" => "text/html; charset=utf-8",
            "css" => "text/css; charset=utf-8",
            _ => "text/javascript; charset=utf-8",
        };
        return (stream, type);
    }
}
