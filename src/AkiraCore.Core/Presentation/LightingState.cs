using AkiraCore.Core.Lighting;

namespace AkiraCore.Core.Presentation;

/// <summary>
/// Estado de iluminação como a tela o vê (JSON da interface). A validação real
/// fica no LightingConfig: valor fora da faixa nunca chega ao firmware.
/// </summary>
public sealed record LightingState(
    string Effect,
    string Color,
    int Brightness,
    int Speed,
    bool Reverse,
    bool Rainbow)
{
    /// <exception cref="ArgumentException">Efeito desconhecido ou valor fora da faixa.</exception>
    /// <exception cref="FormatException">Cor que não é #RRGGBB.</exception>
    public LightingConfig ToConfig(bool swapRedBlue = false)
    {
        // JSON sem o campo chega como null: vira erro tratado, não NullReferenceException
        ArgumentNullException.ThrowIfNull(Effect);
        ArgumentNullException.ThrowIfNull(Color);
        return new LightingConfig(EffectCatalog.ByKey(Effect), Rgb.FromHex(Color), Brightness, Speed,
            direction: Reverse ? 1 : 0, // faixa real da direção ainda não confirmada (0 ou 1)
            rainbow: Rainbow)
        {
            SwapRedBlue = swapRedBlue,
        };
    }

    public static LightingState From(LightingConfig config) =>
        new(config.Effect.Key,
            $"#{config.Color.R:X2}{config.Color.G:X2}{config.Color.B:X2}",
            config.Brightness,
            config.Speed,
            config.Direction != 0,
            config.Rainbow);
}

/// <summary>Um efeito como a tela precisa: nome e quais controles fazem sentido.</summary>
public sealed record EffectView(
    string Key,
    string Label,
    int Mode,
    bool Color,
    bool Rainbow,
    bool Brightness,
    bool Speed,
    bool Direction)
{
    public static EffectView From(Effect effect) =>
        new(effect.Key, effect.Label, effect.Mode,
            effect.Supports(EffectCapabilities.Color),
            effect.Supports(EffectCapabilities.Rainbow),
            effect.Supports(EffectCapabilities.Brightness),
            effect.Supports(EffectCapabilities.Speed),
            effect.Supports(EffectCapabilities.Direction) || effect.Supports(EffectCapabilities.DirectionAlt));

    /// <summary>Catálogo na ordem da tela; "Desligado" fica de fora (é o switch de luz).</summary>
    public static IReadOnlyList<EffectView> Catalog { get; } =
        EffectCatalog.All.Where(e => e.Mode != 0).Select(From).ToList();
}
