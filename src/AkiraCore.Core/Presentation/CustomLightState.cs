using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Presentation;

/// <summary>
/// Um esquema de cores por tecla ("Iluminação do usuário" do oficial, t_userlight_data e
/// t_userlightrgb_data). <see cref="Colors"/> vai do índice da tecla (key_index, igual ao
/// light_index neste modelo) para #RRGGBB; tecla fora do mapa fica apagada, como no oficial.
/// </summary>
public sealed record CustomLightState(
    string Id,
    string Name,
    int Brightness,
    IReadOnlyDictionary<int, string> Colors)
{
    public const int NameMaxLength = 40;
    public const int MaxSchemes = 50;

    /// <exception cref="ArgumentException">Nome, brilho, tecla ou cor inválidos.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Esquema de cores sem identificador.");
        }
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > NameMaxLength)
        {
            throw new ArgumentException($"O nome do esquema precisa ter de 1 a {NameMaxLength} caracteres.");
        }
        if (Brightness is < 0 or > ProtocolConstants.BrightnessMax)
        {
            throw new ArgumentException($"Brilho do esquema fora da faixa (0 a {ProtocolConstants.BrightnessMax}).");
        }
        if (Colors is null)
        {
            throw new ArgumentException("Esquema de cores sem a lista de cores.");
        }
        foreach (var (index, color) in Colors)
        {
            if (KeyboardLayout.ByIndex(index) is null)
            {
                throw new ArgumentException($"O teclado não tem a tecla {index}.");
            }
            ParseColor(color);
        }
    }

    /// <summary>Quadro de cores pronto para o modo personalizado.</summary>
    /// <param name="swapRedBlue">Mesma correção de cor dos efeitos (Ajustes › Corrigir cores).</param>
    public PerKeyFrame ToFrame(bool swapRedBlue = false)
    {
        Validate();
        var frame = new PerKeyFrame();
        foreach (var (index, hex) in Colors)
        {
            var color = ParseColor(hex);
            frame.Set(index, swapRedBlue ? new Rgb(color.B, color.G, color.R) : color);
        }
        return frame;
    }

    /// <exception cref="ArgumentException">Lista grande demais, id repetido ou esquema inválido.</exception>
    public static void ValidateAll(IReadOnlyList<CustomLightState> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        if (schemes.Count > MaxSchemes)
        {
            throw new ArgumentException($"Guarde no máximo {MaxSchemes} esquemas de cores.");
        }
        if (schemes.Select(s => s?.Id).Distinct().Count() != schemes.Count)
        {
            throw new ArgumentException("Dois esquemas de cores com o mesmo identificador.");
        }
        foreach (var scheme in schemes)
        {
            (scheme ?? throw new ArgumentException("Esquema de cores vazio.")).Validate();
        }
    }

    private static Rgb ParseColor(string? hex)
    {
        try
        {
            return Rgb.FromHex(hex ?? "");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ArgumentException($"Cor inválida no esquema: {hex}.");
        }
    }
}
