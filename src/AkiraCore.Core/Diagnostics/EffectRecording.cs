using System.Text.Json;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Diagnostics;

/// <summary>
/// Uma passagem da gravação de efeitos: o efeito com um conjunto de ajustes e por quanto tempo
/// ler os LEDs. Os ajustes partem dos padrões do oficial (fcn.0042b280: brilho 5, velocidade 3,
/// direção 0) e mudam um de cada vez, para cada diferença na gravação ter uma causa só.
/// </summary>
public sealed record RecordingVariant(LightingConfig Config, TimeSpan Duration)
{
    public string Describe()
    {
        var c = Config;
        var color = c.Rainbow ? "arco-íris" : $"#{c.Color.R:X2}{c.Color.G:X2}{c.Color.B:X2}";
        return $"{c.Effect.Label}, {color}, brilho {c.Brightness}, velocidade {c.Speed}, direção {c.Direction}";
    }
}

/// <summary>
/// Plano da gravação de efeitos pelo cabo: aplica cada efeito e lê o que os LEDs mostram com
/// 04 F5 várias vezes. Serve para conferir se a leitura acompanha a animação e para a prévia do
/// dongle (que não lê nada) imitar o teclado de verdade.
/// </summary>
public static class EffectRecordingPlan
{
    public static readonly Rgb SingleColor = new(255, 0, 0);
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan AnimatedDuration = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan StaticDuration = TimeSpan.FromSeconds(2);
    // Um efeito só: tempo para digitar à vontade nos que reagem a tecla
    public static readonly TimeSpan SingleEffectDuration = TimeSpan.FromSeconds(15);

    // Efeito usado para medir velocidade e brilho fora do padrão
    private const string ScaleProbe = "rolling";

    /// <param name="effectKey">Só esse efeito, com mais tempo por passagem; null grava todos.</param>
    public static IReadOnlyList<RecordingVariant> Build(string? effectKey = null)
    {
        var variants = new List<RecordingVariant>();
        foreach (var effect in EffectCatalog.All.Where(e => e.Mode != 0 && (effectKey is null || e.Key == effectKey)))
        {
            var duration = effectKey is not null ? SingleEffectDuration
                : effect.Supports(EffectCapabilities.Speed) ? AnimatedDuration : StaticDuration;
            void Add(bool rainbow, int brightness = ProtocolConstants.BrightnessMax, int speed = 3, int direction = 0) =>
                variants.Add(new RecordingVariant(
                    new LightingConfig(effect, SingleColor, brightness, speed, direction, rainbow), duration));

            // Colorido e Espectro não têm cor nem "Colorido": o firmware escolhe as cores
            var hasColor = effect.Supports(EffectCapabilities.Color);
            if (effect.Supports(EffectCapabilities.Rainbow) || !hasColor)
            {
                Add(rainbow: true);
            }
            if (hasColor)
            {
                Add(rainbow: false);
            }
            // Setas: o 0 semeado já foi; grava cada seta que não tem o mesmo valor
            foreach (var direction in effect.Directions.Where(d => d != 0))
            {
                Add(rainbow: true, direction: (int)direction);
            }
            if (effect.Key == ScaleProbe)
            {
                Add(rainbow: true, speed: 1);
                Add(rainbow: true, speed: 5);
                Add(rainbow: false, brightness: 1);
            }
        }
        return variants;
    }
}

/// <summary>Leituras de uma passagem: tempo desde o início, buffer cru de 04 F5 e teclas tocadas.</summary>
public sealed class RecordedVariant(RecordingVariant variant)
{
    public RecordingVariant Variant { get; } = variant;
    public List<(int Milliseconds, byte[] Buffer)> Frames { get; } = [];
    public List<(int Milliseconds, int ScanCode, bool Down)> Keys { get; } = [];
    public int FailedReads { get; set; }

    /// <summary>Quadros diferentes entre si: 1 com animação rodando quer dizer que a leitura não a acompanha.</summary>
    public int DistinctFrames =>
        Frames.Select(f => Convert.ToHexString(f.Buffer)).Distinct().Count();

    public string Summary() =>
        $"{Frames.Count} quadros ({DistinctFrames} diferentes), {FailedReads} leituras sem resposta, {Keys.Count} teclas";
}

/// <summary>Arquivo JSON da gravação, para o usuário mandar e a prévia ser ajustada em cima dele.</summary>
public static class EffectRecordingFile
{
    public const string Format = "akira-effect-recording";
    public const int Version = 1;

    public static string Serialize(IEnumerable<RecordedVariant> recorded, DateTimeOffset startedAt)
    {
        var document = new
        {
            format = Format,
            version = Version,
            startedAt = startedAt.ToString("O"),
            ledBuffer = "04 F5, 576 bytes; LED i em i*4 como [i, R, G, B] (sem trocar R e B)",
            variants = recorded.Select(r => new
            {
                effect = r.Variant.Config.Effect.Key,
                mode = r.Variant.Config.Effect.Mode,
                label = r.Variant.Config.Effect.Label,
                block = Convert.ToHexString(r.Variant.Config.ToLightBlock()),
                rainbow = r.Variant.Config.Rainbow,
                color = $"#{r.Variant.Config.Color.R:X2}{r.Variant.Config.Color.G:X2}{r.Variant.Config.Color.B:X2}",
                brightness = r.Variant.Config.Brightness,
                speed = r.Variant.Config.Speed,
                direction = r.Variant.Config.Direction,
                failedReads = r.FailedReads,
                distinctFrames = r.DistinctFrames,
                frames = r.Frames.Select(f => new { t = f.Milliseconds, leds = Convert.ToHexString(f.Buffer) }),
                keys = r.Keys.Select(k => new { t = k.Milliseconds, scan = k.ScanCode, down = k.Down }),
            }),
        };
        return JsonSerializer.Serialize(document);
    }
}
