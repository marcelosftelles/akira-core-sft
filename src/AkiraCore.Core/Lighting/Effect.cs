namespace AkiraCore.Core.Lighting;

/// <summary>
/// Bits de config_func gravados pelo app oficial em t_light_data.
/// Significado inferido pelo padrão dos efeitos.
/// </summary>
[Flags]
public enum EffectCapabilities
{
    None = 0,
    Brightness = 0x01,
    Speed = 0x02,
    Direction = 0x04,
    DirectionAlt = 0x08,   // só no Rolagem; o oficial mostra outro par de direções
    Color = 0x10,
    Rainbow = 0x20,
}

public sealed record Effect(byte Mode, string Key, string Label, EffectCapabilities Capabilities)
{
    public bool Supports(EffectCapabilities capability) => Capabilities.HasFlag(capability);
    public override string ToString() => Label;
}

public static class EffectCatalog
{
    private const EffectCapabilities Standard =
        EffectCapabilities.Brightness | EffectCapabilities.Speed |
        EffectCapabilities.Color | EffectCapabilities.Rainbow;

    private const EffectCapabilities Directional = Standard | EffectCapabilities.Direction;

    // Tabela semeada pelo app oficial no primeiro uso (função 0x42b280)
    public static IReadOnlyList<Effect> All { get; } =
    [
        new(1, "static", "Estático", EffectCapabilities.Brightness | EffectCapabilities.Color | EffectCapabilities.Rainbow),
        new(2, "single-on", "Acender ao tocar", Standard),
        new(3, "single-off", "Apagar ao tocar", Standard),
        new(4, "glittering", "Brilhante", Standard),
        new(5, "fluttering", "Queda", Standard),
        new(6, "colourful", "Colorido", EffectCapabilities.Brightness | EffectCapabilities.Speed),
        new(7, "breath", "Respiração", Standard),
        new(8, "spectrum", "Espectro", EffectCapabilities.Brightness | EffectCapabilities.Speed),
        new(9, "outward", "Para fora", Standard),
        new(10, "scrolling", "Rolagem", Standard | EffectCapabilities.DirectionAlt),
        new(11, "rolling", "Rolando", Directional),
        new(12, "rotating", "Rotacionando", Directional),
        new(13, "explode", "Explodir", Standard),
        new(14, "launch", "Lançamento", Standard),
        new(15, "ripples", "Ondulações", Standard),
        new(16, "flowing", "Fluindo", Directional),
        new(17, "pulsating", "Pulsante", Standard),
        new(18, "tilt", "Inclinação", Directional),
        new(19, "shuttle", "Vaivém", Standard),
        new(0, "off", "Desligado", EffectCapabilities.None),
    ];

    public static Effect Off => All[^1];

    public static Effect ByKey(string key) =>
        All.FirstOrDefault(e => e.Key == key)
        ?? throw new ArgumentException($"Efeito desconhecido: {key}", nameof(key));
}
