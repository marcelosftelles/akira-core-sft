using AkiraCore.Core.Lighting;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Presentation;

/// <summary>
/// Ajustes guardados de um efeito, como uma linha de t_light_data do oficial: cada efeito
/// lembra os seus, e escolher um efeito na lista volta aos ajustes dele (fcn.0042aa10).
/// </summary>
public sealed record EffectSettingsState(string Color, int Brightness, int Speed, int Direction, bool Rainbow)
{
    /// <summary>
    /// Como o oficial semeia cada efeito (fcn.0042b280): brilho e velocidade do rgb-keyboard.xml
    /// (default_brightness=5, default_speed=3) e o bloco de 0x575200: direção 0, colorful 1 e
    /// color_value 0x0000FF (vermelho em COLORREF).
    /// </summary>
    public static EffectSettingsState Default { get; } = new("#FF0000", 5, 3, 0, true);

    /// <summary>
    /// Ajustes válidos por efeito, sem os que não passam na validação (arquivo editado à mão
    /// ou de versão antiga). Efeito sem entrada usa <see cref="Default"/>.
    /// </summary>
    public static Dictionary<string, EffectSettingsState> Sanitize(
        IReadOnlyDictionary<string, EffectSettingsState>? saved)
    {
        var result = new Dictionary<string, EffectSettingsState>();
        foreach (var (key, settings) in saved ?? new Dictionary<string, EffectSettingsState>())
        {
            try
            {
                var state = LightingState.For(key, settings).Normalized();
                state.ToConfig();
                result[key] = state.Settings;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or NullReferenceException)
            {
                // entrada inválida: o efeito volta ao padrão semeado
            }
        }
        return result;
    }

    /// <exception cref="ArgumentException">Efeito desconhecido ou ajuste fora da faixa.</exception>
    public static void ValidateAll(IReadOnlyDictionary<string, EffectSettingsState>? saved)
    {
        foreach (var (key, settings) in saved ?? new Dictionary<string, EffectSettingsState>())
        {
            if (key is null || settings is null)
            {
                throw new ArgumentException("Ajuste de efeito vazio.");
            }
            try
            {
                LightingState.For(key, settings).ToConfig();
            }
            catch (FormatException ex)
            {
                throw new ArgumentException(ex.Message);
            }
        }
    }
}

/// <summary>
/// Estado de iluminação como a tela o vê (JSON da interface): o efeito escolhido e os ajustes
/// dele. A validação de protocolo fica no LightingConfig: valor fora da faixa nunca chega ao
/// firmware.
/// </summary>
public sealed record LightingState(
    string Effect,
    string Color,
    int Brightness,
    int Speed,
    int Direction,
    bool Rainbow)
{
    /// <summary>
    /// Os sliders de brilho e velocidade do oficial vão de 1 a 5 (fcn.00437540 grava 1 no mínimo
    /// e 5 no máximo do MSlider; arrastar e rolar nunca saem dessa faixa).
    /// </summary>
    public const int LevelMin = 1;

    public EffectSettingsState Settings => new(Color, Brightness, Speed, Direction, Rainbow);

    public static LightingState For(string effect, EffectSettingsState settings) =>
        new(effect, settings.Color, settings.Brightness, settings.Speed, settings.Direction, settings.Rainbow);

    /// <exception cref="ArgumentException">Efeito desconhecido, direção que o efeito não tem ou valor fora da faixa.</exception>
    /// <exception cref="FormatException">Cor que não é #RRGGBB.</exception>
    public LightingConfig ToConfig(bool swapRedBlue = false)
    {
        // JSON sem o campo chega como null: vira erro tratado, não NullReferenceException
        ArgumentNullException.ThrowIfNull(Effect);
        ArgumentNullException.ThrowIfNull(Color);
        var effect = EffectCatalog.ByKey(Effect);
        if (!effect.AcceptsDirection(Direction))
        {
            throw new ArgumentException($"Direção {Direction} não existe no efeito {effect.Label}.");
        }
        return new LightingConfig(effect, Rgb.FromHex(Color), Brightness, Speed, Direction, Rainbow)
        {
            SwapRedBlue = swapRedBlue,
        };
    }

    /// <summary>
    /// O que o oficial teria gravado para este efeito: controle que o efeito não mostra fica
    /// com o valor semeado (ele nunca muda, e é o que vai no bloco), brilho e velocidade
    /// ficam entre 1 e 5 e direção que o efeito não tem volta a 0. Usado ao carregar
    /// arquivos de versões antigas, que aceitavam 0 e um "invertido" para todos.
    /// </summary>
    /// <exception cref="ArgumentException">Efeito desconhecido.</exception>
    public LightingState Normalized()
    {
        ArgumentNullException.ThrowIfNull(Effect);
        var effect = EffectCatalog.ByKey(Effect);
        var seed = EffectSettingsState.Default;
        return this with
        {
            Color = effect.Supports(EffectCapabilities.Color) ? Color : seed.Color,
            Brightness = effect.Supports(EffectCapabilities.Brightness)
                ? Math.Clamp(Brightness, LevelMin, ProtocolConstants.BrightnessMax) : seed.Brightness,
            Speed = effect.Supports(EffectCapabilities.Speed)
                ? Math.Clamp(Speed, LevelMin, ProtocolConstants.SpeedMax) : seed.Speed,
            Direction = effect.AcceptsDirection(Direction) ? Direction : seed.Direction,
            Rainbow = effect.Supports(EffectCapabilities.Rainbow) ? Rainbow : seed.Rainbow,
        };
    }

    public static LightingState From(LightingConfig config) =>
        new(config.Effect.Key,
            $"#{config.Color.R:X2}{config.Color.G:X2}{config.Color.B:X2}",
            config.Brightness,
            config.Speed,
            config.Direction,
            config.Rainbow);
}

/// <summary>Um efeito como a tela precisa: nome e quais controles o oficial mostra para ele.</summary>
public sealed record EffectView(
    string Key,
    string Label,
    int Mode,
    bool Color,
    bool Rainbow,
    bool Brightness,
    bool Speed,
    IReadOnlyList<int> Directions,
    int? DirectionFallback)
{
    public static EffectView From(Effect effect) =>
        new(effect.Key, effect.Label, effect.Mode,
            effect.Supports(EffectCapabilities.Color),
            effect.Supports(EffectCapabilities.Rainbow),
            effect.Supports(EffectCapabilities.Brightness),
            effect.Supports(EffectCapabilities.Speed),
            effect.Directions.Select(d => (int)d).ToList(),
            (int?)effect.FallbackDirection);

    /// <summary>Catálogo na ordem da tela; "Desligado" fica de fora (é o switch de luz).</summary>
    public static IReadOnlyList<EffectView> Catalog { get; } =
        EffectCatalog.All.Where(e => e.Mode != 0).Select(From).ToList();
}
