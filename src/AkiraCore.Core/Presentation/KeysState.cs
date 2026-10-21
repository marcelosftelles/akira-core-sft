using AkiraCore.Core.Device;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Macros;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Presentation;

/// <summary>
/// Uma tecla remapeada, como a tela guarda. <see cref="Action"/> é "disabled", "key", "media",
/// "mouse" ou "macro"; os outros campos só valem para o tipo correspondente. A macro é
/// referenciada pelo id, porque a posição dela na lista muda quando outra é apagada.
/// </summary>
public sealed record KeyAssignmentState(
    int Key,
    string Layer,
    string Action,
    int Modifiers = 0,
    int Usage = 0,
    string? Media = null,
    string? Mouse = null,
    string? MacroId = null,
    string? Playback = null,
    int Repeat = 0)
{
    public KeyLayer ToLayer() => Layer switch
    {
        "normal" => KeyLayer.Normal,
        "fn" => KeyLayer.Fn,
        _ => throw new ArgumentException($"Camada desconhecida: {Layer}."),
    };

    /// <exception cref="ArgumentException">Ação incompleta, valor fora da faixa ou macro inexistente.</exception>
    public KeyAction ToAction(IReadOnlyList<MacroState> macros) => Action switch
    {
        "disabled" => DisabledKeyAction.Instance,
        "key" => new KeyComboAction(ToModifiers(Modifiers), ToByte(Usage, "tecla")),
        "media" => new MediaKeyAction(KeyOptions.ParseMedia(Media)),
        "mouse" => new MouseKeyAction(KeyOptions.ParseMouse(Mouse)),
        "macro" => new MacroKeyAction(MacroPosition(macros), KeyOptions.ParsePlayback(Playback), Repeat),
        _ => throw new ArgumentException($"Ação desconhecida: {Action}."),
    };

    private int MacroPosition(IReadOnlyList<MacroState> macros)
    {
        for (var i = 0; i < macros.Count; i++)
        {
            if (macros[i].Id == MacroId)
            {
                return i;
            }
        }
        throw new ArgumentException("A macro desta tecla não existe mais.");
    }

    private static KeyModifiers ToModifiers(int value) =>
        value is >= 0 and <= 0xFF ? (KeyModifiers)value : throw new ArgumentException("Modificadores inválidos.");

    private static byte ToByte(int value, string what) =>
        value is >= 0 and <= 0xFF ? (byte)value : throw new ArgumentException($"Código de {what} inválido.");
}

/// <summary>Monta as duas tabelas de remapeamento a partir do que a tela guardou.</summary>
public static class KeyProfile
{
    /// <exception cref="ArgumentException">Alguma tecla inválida ou repetida na mesma camada.</exception>
    public static (KeyRemapTable Normal, KeyRemapTable Fn) Build(
        IEnumerable<KeyAssignmentState> assignments, IReadOnlyList<MacroState> macros)
    {
        var normal = new KeyRemapTable();
        var fn = new KeyRemapTable();
        var seen = new HashSet<(int, KeyLayer)>();
        foreach (var assignment in assignments)
        {
            var layer = assignment.ToLayer();
            if (!seen.Add((assignment.Key, layer)))
            {
                throw new ArgumentException($"A tecla {assignment.Key} aparece duas vezes na mesma camada.");
            }
            (layer == KeyLayer.Fn ? fn : normal).Set(assignment.Key, assignment.ToAction(macros));
        }
        return (normal, fn);
    }
}

/// <summary>Uma ação da macro: "down", "up", "mouse-down" ou "mouse-up", código e pausa depois dela.</summary>
public sealed record MacroEventState(string Kind, int Code, int Delay)
{
    public MacroEvent ToEvent() => new(Kind switch
    {
        "down" => MacroEventKind.KeyDown,
        "up" => MacroEventKind.KeyUp,
        "mouse-down" => MacroEventKind.MouseDown,
        "mouse-up" => MacroEventKind.MouseUp,
        _ => throw new ArgumentException($"Ação de macro desconhecida: {Kind}."),
    }, Code is >= 0 and <= 0xFF ? (byte)Code : throw new ArgumentException("Código inválido na macro."), Delay);

    public static MacroEventState From(MacroEvent e) => new(e.Kind switch
    {
        MacroEventKind.KeyDown => "down",
        MacroEventKind.KeyUp => "up",
        MacroEventKind.MouseDown => "mouse-down",
        _ => "mouse-up",
    }, e.Code, e.DelayMs);
}

public sealed record MacroState(string Id, string Name, IReadOnlyList<MacroEventState> Events)
{
    public const int NameMaxLength = 40;

    /// <exception cref="ArgumentException">Nome vazio ou longo demais, ou ação inválida.</exception>
    public Macro ToMacro()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Macro sem identificador.");
        }
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > NameMaxLength)
        {
            throw new ArgumentException($"O nome da macro precisa ter de 1 a {NameMaxLength} caracteres.");
        }
        ArgumentNullException.ThrowIfNull(Events);
        return new Macro(Name.Trim(), Events.Select(e => e.ToEvent()).ToList());
    }

    public static IReadOnlyList<Macro> ToMacros(IReadOnlyList<MacroState> macros)
    {
        if (macros.Select(m => m.Id).Distinct().Count() != macros.Count)
        {
            throw new ArgumentException("Duas macros com o mesmo identificador.");
        }
        var result = macros.Select(m => m.ToMacro()).ToList();
        MacroEncoder.Encode(result); // valida limite de quantidade e de tamanho
        return result;
    }
}

/// <summary>Configurações gerais como a tela guarda. Sleep: "never", "1m", "5m" ou "30m".</summary>
public sealed record DeviceSettingsState(
    bool DisableWin,
    bool DisableAltF4,
    bool DisableAltTab,
    bool FnToggle,
    string Sleep,
    int Response)
{
    /// <exception cref="ArgumentException">Valor fora das opções.</exception>
    public DeviceSettings ToSettings()
    {
        var settings = new DeviceSettings(DisableWin, DisableAltF4, DisableAltTab,
            FnToggle ? FnMode.Toggle : FnMode.Momentary, KeyOptions.ParseSleep(Sleep), Response);
        settings.ToBlock(); // valida as faixas
        return settings;
    }

    public static DeviceSettingsState From(DeviceSettings settings) =>
        new(settings.DisableWinKey, settings.DisableAltF4, settings.DisableAltTab,
            settings.FnMode == FnMode.Toggle,
            KeyOptions.Sleep.First(o => o.Value == settings.Sleep).Key,
            settings.KeyResponse);
}

/// <summary>Opção de uma lista da tela: chave do JSON, rótulo e o valor do Core.</summary>
public sealed record Option<T>(string Key, string Label, T Value);

/// <summary>As listas que a tela mostra, com rótulos em português, e a conversão de volta.</summary>
public static class KeyOptions
{
    public static IReadOnlyList<Option<MediaKey>> Media { get; } =
    [
        new("play-pause", "Tocar/pausar", MediaKey.PlayPause),
        new("stop", "Parar", MediaKey.Stop),
        new("previous", "Faixa anterior", MediaKey.Previous),
        new("next", "Próxima faixa", MediaKey.Next),
        new("volume-up", "Aumentar volume", MediaKey.VolumeUp),
        new("volume-down", "Diminuir volume", MediaKey.VolumeDown),
        new("mute", "Sem som", MediaKey.Mute),
    ];

    public static IReadOnlyList<Option<MouseFunction>> Mouse { get; } =
    [
        new("left", "Clique esquerdo", MouseFunction.LeftClick),
        new("right", "Clique direito", MouseFunction.RightClick),
        new("middle", "Clique do meio", MouseFunction.MiddleClick),
        new("double", "Clique duplo", MouseFunction.DoubleClick),
        new("scroll-up", "Rolar para cima", MouseFunction.ScrollUp),
        new("scroll-down", "Rolar para baixo", MouseFunction.ScrollDown),
        new("button4", "Botão 4 (voltar)", MouseFunction.Button4),
        new("button5", "Botão 5 (avançar)", MouseFunction.Button5),
    ];

    public static IReadOnlyList<Option<MacroPlayback>> Playback { get; } =
    [
        new("once", "Tocar uma vez", MacroPlayback.Once),
        new("repeat", "Repetir algumas vezes", MacroPlayback.Repeat),
        new("until-pressed", "Repetir até apertar de novo", MacroPlayback.UntilPressedAgain),
        new("while-held", "Repetir enquanto segurar", MacroPlayback.WhileHeld),
    ];

    public static IReadOnlyList<Option<SleepTimeout>> Sleep { get; } =
    [
        new("never", "Nunca", SleepTimeout.Never),
        new("1m", "1 minuto", SleepTimeout.OneMinute),
        new("5m", "5 minutos", SleepTimeout.FiveMinutes),
        new("30m", "30 minutos", SleepTimeout.ThirtyMinutes),
    ];

    public static MediaKey ParseMedia(string? key) => Parse(Media, key, "tecla multimídia");
    public static MouseFunction ParseMouse(string? key) => Parse(Mouse, key, "função de mouse");
    public static MacroPlayback ParsePlayback(string? key) => Parse(Playback, key, "modo de repetição");
    public static SleepTimeout ParseSleep(string? key) => Parse(Sleep, key, "tempo de suspensão");

    private static T Parse<T>(IReadOnlyList<Option<T>> options, string? key, string what) =>
        options.FirstOrDefault(o => o.Key == key) is { } option
            ? option.Value
            : throw new ArgumentException($"Escolha uma {what}.");
}

/// <summary>Tudo que a página precisa para montar as telas de teclas e macros.</summary>
public static class KeysCatalogView
{
    public static object Build() => new
    {
        keys = KeyboardLayout.All.Select(k => new { index = k.Index, label = k.Label, remappable = k.Remappable }),
        targets = HidKeys.All.Select(k => new { code = k.Code, usage = k.Usage, label = k.Label, group = k.Group }),
        media = KeyOptions.Media.Select(o => new { key = o.Key, label = o.Label }),
        mouse = KeyOptions.Mouse.Select(o => new { key = o.Key, label = o.Label }),
        playback = KeyOptions.Playback.Select(o => new { key = o.Key, label = o.Label }),
        sleep = KeyOptions.Sleep.Select(o => new { key = o.Key, label = o.Label }),
        maxMacros = Protocol.MacroProtocol.SlotCount,
        macroCapacity = Protocol.MacroProtocol.SizeLimit,
    };
}
