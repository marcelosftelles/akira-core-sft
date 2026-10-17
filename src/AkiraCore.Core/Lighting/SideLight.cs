namespace AkiraCore.Core.Lighting;

/// <summary>
/// Predefinições das fitas laterais. O firmware só aceita estes valores:
/// não há cor livre nem controle separado de esquerda e direita.
/// Origem: combo da página oculta "Modo de luz lateral" (fcn.00433e20),
/// item N recebe o valor 0x1F + N. A página vem desligada no XML (sidelight="0").
/// </summary>
public sealed record SideLightMode(byte Value, string Key, string Label)
{
    public override string ToString() => Label;
}

public static class SideLightCatalog
{
    public static IReadOnlyList<SideLightMode> All { get; } =
    [
        new(0x20, "flowing", "Fluindo (arco-íris)"),
        new(0x21, "red", "Vermelho"),
        new(0x22, "yellow", "Amarelo"),
        new(0x23, "green", "Verde"),
        new(0x24, "ice-blue", "Azul gelo"),
        new(0x25, "blue", "Azul"),
        new(0x26, "pink", "Rosa"),
        new(0x27, "white", "Branco"),
        new(0x28, "neon", "Neon"),
        new(0x29, "off", "Desligada"),
    ];

    public static SideLightMode ByKey(string key) =>
        All.FirstOrDefault(m => m.Key == key)
        ?? throw new ArgumentException($"Modo lateral desconhecido: {key}", nameof(key));
}
