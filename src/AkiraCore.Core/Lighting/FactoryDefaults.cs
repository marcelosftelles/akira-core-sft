namespace AkiraCore.Core.Lighting;

/// <summary>O que a redefinição de fábrica conseguiu fazer no canal atual.</summary>
public enum FactoryResetScope
{
    LightingOnly,      // cabo: só a iluminação padrão (o oficial não tem comando de reset no cabo)
    DeviceAndLighting, // dongle: reset do firmware (0F) + iluminação padrão
}

/// <summary>Padrões de fábrica do app oficial.</summary>
public static class FactoryDefaults
{
    /// <summary>
    /// Efeito ativo após o reset: default_mode=11 (Rolando), default_brightness=5 e
    /// default_speed=3 do rgb-keyboard.xml. Cada efeito é semeado por fcn.0042b280 com o
    /// bloco de 0x575200: direção 0, colorful 1 (arco-íris) e color_value 0x0000FF (vermelho).
    /// </summary>
    public static LightingConfig Lighting { get; } =
        new(EffectCatalog.ByKey("rolling"), new Rgb(0xFF, 0x00, 0x00),
            brightness: 5, speed: 3, direction: 0, rainbow: true);
}
