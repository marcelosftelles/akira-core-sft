using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Live;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Presentation;

/// <summary>Animação e cor de uma tecla na luz personalizada em tempo real.</summary>
public sealed record LiveKeyState(string Effect, string Color);

/// <summary>Cena da luz personalizada: velocidade 0 a 5 e o que cada tecla faz (índice do LED).</summary>
public sealed record LiveSceneState(int Speed, IReadOnlyDictionary<int, LiveKeyState> Keys);

/// <summary>Style: "rainbow", "gradient" ou "color". Sensitivity de 1 a 10.</summary>
public sealed record MusicOptionsState(string Style, string Color, bool Background, int Sensitivity);

/// <summary>Smoothing de 0 (instantâneo) a 10 (bem suave). Vivid realça as cores da tela.</summary>
public sealed record ScreenOptionsState(int Smoothing, bool Vivid);

/// <summary>Style: "bar" (fileira dos números) ou "whole" (teclado inteiro).</summary>
public sealed record CpuOptionsState(string Style, string Low, string High);

/// <summary>Minutos do cronômetro (1 a 180) e a cor da barra.</summary>
public sealed record TimerOptionsState(int Minutes, string Color);

/// <summary>
/// Página Ao vivo: efeitos calculados no PC enquanto o app está aberto. Só um modo por vez.
/// O oficial tem dois (luz personalizada e ritmo musical); tela, CPU e cronômetro são do AkiraCore.
/// </summary>
public sealed record LiveSettingsState(
    string Mode,
    int Brightness,
    LiveSceneState Scene,
    MusicOptionsState Music,
    ScreenOptionsState Screen,
    CpuOptionsState Cpu,
    TimerOptionsState Timer)
{
    public const int MaxTimerMinutes = 180;

    public static LiveSettingsState Default { get; } = new(
        LiveModes.Off,
        ProtocolConstants.BrightnessMax,
        new LiveSceneState(3, new Dictionary<int, LiveKeyState>()),
        new MusicOptionsState("rainbow", "#00E5FF", true, 5),
        new ScreenOptionsState(4, true),
        new CpuOptionsState("bar", "#00FF40", "#FF0000"),
        new TimerOptionsState(25, "#00E5FF"));

    /// <exception cref="ArgumentException">Modo desconhecido ou valor fora da faixa.</exception>
    public void Validate()
    {
        if (LiveModes.All.All(m => m.Key != Mode))
        {
            throw new ArgumentException($"Modo ao vivo desconhecido: {Mode}.");
        }
        Range(Brightness, 0, ProtocolConstants.BrightnessMax, "Brilho");
        var scene = Scene ?? throw new ArgumentException("Falta a cena da luz personalizada.");
        Range(scene.Speed, 0, ProtocolConstants.SpeedMax, "Velocidade");
        foreach (var (led, key) in scene.Keys ?? throw new ArgumentException("Falta a lista de teclas da cena."))
        {
            if (KeyboardLayout.ByIndex(led) is null)
            {
                throw new ArgumentException($"O teclado não tem a tecla {led}.");
            }
            if (key is null)
            {
                throw new ArgumentException($"Tecla {led} sem animação.");
            }
            ParseAnimation(key.Effect);
            ParseColor(key.Color);
        }
        var music = Music ?? throw new ArgumentException("Faltam as opções do ritmo musical.");
        ParseMusicStyle(music.Style);
        ParseColor(music.Color);
        Range(music.Sensitivity, 1, 10, "Sensibilidade");
        Range((Screen ?? throw new ArgumentException("Faltam as opções da tela.")).Smoothing, 0, 10, "Suavidade");
        var cpu = Cpu ?? throw new ArgumentException("Faltam as opções da CPU.");
        ParseCpuStyle(cpu.Style);
        ParseColor(cpu.Low);
        ParseColor(cpu.High);
        var timer = Timer ?? throw new ArgumentException("Faltam as opções do cronômetro.");
        Range(timer.Minutes, 1, MaxTimerMinutes, "Minutos do cronômetro");
        ParseColor(timer.Color);
    }

    // A correção de cor (Ajustes › Corrigir cores) vale no envio do quadro, não aqui
    public AnimatedScene CreateScene() => new(
        Scene.Keys.ToDictionary(k => k.Key, k => new AnimatedKey(ParseAnimation(k.Value.Effect), ParseColor(k.Value.Color))),
        Scene.Speed);

    public MusicVisualizer CreateMusic() =>
        new(ParseMusicStyle(Music.Style), ParseColor(Music.Color), Music.Background);

    /// <summary>Suavidade 0 a 10 vira de 0 a 0,5 s de transição.</summary>
    public ScreenAmbient CreateScreen() => new(Screen.Smoothing * 0.05, Screen.Vivid);

    public CpuMeter CreateCpu() => new(ParseCpuStyle(Cpu.Style), ParseColor(Cpu.Low), ParseColor(Cpu.High));

    public Rgb TimerColor => ParseColor(Timer.Color);

    public static KeyAnimation ParseAnimation(string? key) =>
        LiveModes.Animations.FirstOrDefault(a => a.Key == key)?.Value
        ?? throw new ArgumentException($"Animação desconhecida: {key}.");

    private static MusicStyle ParseMusicStyle(string? key) => key switch
    {
        "rainbow" => MusicStyle.Rainbow,
        "gradient" => MusicStyle.Gradient,
        "color" => MusicStyle.Solid,
        _ => throw new ArgumentException($"Estilo de ritmo musical desconhecido: {key}."),
    };

    private static CpuStyle ParseCpuStyle(string? key) => key switch
    {
        "bar" => CpuStyle.Bar,
        "whole" => CpuStyle.Whole,
        _ => throw new ArgumentException($"Estilo de CPU desconhecido: {key}."),
    };

    private static Rgb ParseColor(string? hex)
    {
        try
        {
            return Rgb.FromHex(hex ?? "");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ArgumentException($"Cor inválida: {hex}.");
        }
    }

    private static void Range(int value, int min, int max, string what)
    {
        if (value < min || value > max)
        {
            throw new ArgumentException($"{what} fora da faixa ({min} a {max}).");
        }
    }
}

/// <summary>Um modo da página Ao vivo e por qual canal ele funciona.</summary>
public sealed record LiveMode(string Key, string Label, bool Cable, bool Dongle);

public static class LiveModes
{
    public const string Off = "off";
    public const string Scene = "scene";
    public const string Music = "music";
    public const string Screen = "screen";
    public const string Cpu = "cpu";
    public const string Timer = "timer";

    // Só o ritmo musical tem caminho no 2.4G (o firmware desenha); o resto usa o 04 20 do cabo
    public static IReadOnlyList<LiveMode> All { get; } =
    [
        new(Off, "Desligado", true, true),
        new(Scene, "Teclas animadas", true, false),
        new(Music, "Ritmo musical", true, true),
        new(Screen, "Cor da tela", true, false),
        new(Cpu, "Uso da CPU", true, false),
        new(Timer, "Cronômetro", true, false),
    ];

    public static IReadOnlyList<Option<KeyAnimation>> Animations { get; } =
    [
        new("static", "Fixa", KeyAnimation.Static),
        new("breath", "Respirar", KeyAnimation.Breath),
        new("blink", "Piscar", KeyAnimation.Blink),
        new("spectrum", "Espectro", KeyAnimation.Spectrum),
        new("wave", "Onda de arco-íris", KeyAnimation.Wave),
        new("reactive", "Acender ao tocar", KeyAnimation.Reactive),
    ];

    public static LiveMode ByKey(string? key) =>
        All.FirstOrDefault(m => m.Key == key) ?? throw new ArgumentException($"Modo ao vivo desconhecido: {key}.");

    /// <summary>O canal conectado consegue rodar o modo? <paramref name="channel"/>: "usb" ou "dongle".</summary>
    public static bool Supports(string mode, string? channel) => ByKey(mode) switch
    {
        { Key: Off } => true,
        var m => channel == "dongle" ? m.Dongle : channel == "usb" && m.Cable,
    };

    /// <summary>Tudo que a página precisa para montar a tela Ao vivo.</summary>
    public static object CatalogView() => new
    {
        modes = All.Select(m => new { key = m.Key, label = m.Label, cable = m.Cable, dongle = m.Dongle }),
        animations = Animations.Select(a => new { key = a.Key, label = a.Label }),
        maxTimerMinutes = LiveSettingsState.MaxTimerMinutes,
    };
}
