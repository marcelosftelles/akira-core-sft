namespace AkiraCore.Core.Lighting;

/// <summary>
/// Bits de config_func gravados pelo app oficial em t_light_data (fcn.0042b280). O que cada
/// um mostra na tela sai de fcn.0042aa10: 0x01 brilho, 0x02 velocidade, 0x04 setas
/// direita/esquerda, 0x08 setas cima/baixo, 0x20 "Colorido" e 0x10 o painel de cor.
/// </summary>
[Flags]
public enum EffectCapabilities
{
    None = 0,
    Brightness = 0x01,
    Speed = 0x02,
    Direction = 0x04,
    DirectionAlt = 0x08,   // só no Rolagem: setas para cima e para baixo
    Color = 0x10,
    Rainbow = 0x20,
}

/// <summary>
/// Valor que o oficial grava no campo direção (fcn.0042c500). Os botões são criados em
/// fcn.00437540: 0x5b seta para a direita, 0x5c esquerda, 0x5d cima, 0x5e baixo.
/// </summary>
public enum EffectDirection
{
    Left = 0,
    Right = 1,
    Up = 2,
    Down = 3,
}

public sealed record Effect(byte Mode, string Key, string Label, EffectCapabilities Capabilities)
{
    public bool Supports(EffectCapabilities capability) => Capabilities.HasFlag(capability);

    /// <summary>Setas que o oficial mostra para o efeito (fcn.0042aa10), na ordem da tela.</summary>
    public IReadOnlyList<EffectDirection> Directions =>
        Supports(EffectCapabilities.Direction) ? [EffectDirection.Left, EffectDirection.Right]
        : Supports(EffectCapabilities.DirectionAlt) ? [EffectDirection.Up, EffectDirection.Down]
        : [];

    /// <summary>
    /// Seta marcada quando nenhuma corresponde ao valor gravado. O efeito nasce com direção 0
    /// (fcn.0042b280): no Rolagem isso aparece como "para baixo", mas o 0 só muda quando o
    /// usuário clica numa seta (fcn.0042aa10 marca cima só com 2; esquerda com tudo que não é 1).
    /// </summary>
    public EffectDirection? FallbackDirection =>
        Supports(EffectCapabilities.Direction) ? EffectDirection.Left
        : Supports(EffectCapabilities.DirectionAlt) ? EffectDirection.Down
        : null;

    /// <summary>Valores que o oficial pode gravar: o 0 semeado ou o de uma das setas do efeito.</summary>
    public bool AcceptsDirection(int value) =>
        value == 0 || Directions.Contains((EffectDirection)value);

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
