using System.Globalization;
using System.Text;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Live;
using AkiraCore.Core.Presentation;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Automation;

/// <summary>
/// O que a linha de comando pediu. Campos nulos não mudam nada. Perfil e esquema vão pelo nome
/// e são procurados na hora de executar, na instância que já está aberta.
/// </summary>
public sealed record CliRequest
{
    public bool Hidden { get; init; }
    public bool Help { get; init; }
    public bool Status { get; init; }
    public bool? Light { get; init; }
    public bool ToggleLight { get; init; }
    public string? Effect { get; init; }
    public string? Color { get; init; }
    public int? Brightness { get; init; }
    public int? Speed { get; init; }
    public bool? Rainbow { get; init; }
    public bool? Reverse { get; init; }
    public string? Profile { get; init; }
    public string? Scheme { get; init; }
    public string? Live { get; init; }
    public int? TimerMinutes { get; init; }
    public bool TimerStop { get; init; }
    public string? AlertColor { get; init; }
    public int AlertTimes { get; init; } = 3;

    /// <summary>Muda algo no teclado (além de abrir o app).</summary>
    public bool HasActions =>
        Status || Light is not null || ToggleLight || Effect is not null || Color is not null || Brightness is not null
        || Speed is not null || Rainbow is not null || Reverse is not null || Profile is not null || Scheme is not null
        || Live is not null || TimerMinutes is not null || TimerStop || AlertColor is not null;

    /// <summary>Mexe na iluminação de efeito (efeito, cor, brilho, velocidade, arco-íris, direção).</summary>
    public bool ChangesLighting =>
        Effect is not null || Color is not null || Brightness is not null || Speed is not null || Rainbow is not null
        || Reverse is not null;
}

/// <summary>
/// Linha de comando do AkiraCore, para automação (atalhos, scripts, pipelines). Aceita nomes em
/// português e em inglês. Com o app já aberto, o pedido vai para ele; senão o app abre na bandeja
/// e executa. Parse puro: o mesmo texto dá sempre o mesmo resultado.
/// </summary>
public static class CommandLine
{
    private static readonly Dictionary<string, string> ColorNames = new()
    {
        ["vermelho"] = "#FF0000", ["red"] = "#FF0000",
        ["laranja"] = "#FF6A00", ["orange"] = "#FF6A00",
        ["amarelo"] = "#FFD000", ["yellow"] = "#FFD000",
        ["verde"] = "#00FF40", ["green"] = "#00FF40",
        ["ciano"] = "#00E5FF", ["cyan"] = "#00E5FF",
        ["azul"] = "#0040FF", ["blue"] = "#0040FF",
        ["roxo"] = "#8A2BE2", ["purple"] = "#8A2BE2",
        ["rosa"] = "#FF2D95", ["pink"] = "#FF2D95",
        ["branco"] = "#FFFFFF", ["white"] = "#FFFFFF",
    };

    private static readonly Dictionary<string, string> LiveNames = new()
    {
        ["off"] = LiveModes.Off, ["desligado"] = LiveModes.Off, ["parar"] = LiveModes.Off,
        ["scene"] = LiveModes.Scene, ["teclas"] = LiveModes.Scene, ["teclas-animadas"] = LiveModes.Scene,
        ["music"] = LiveModes.Music, ["musica"] = LiveModes.Music, ["ritmo"] = LiveModes.Music,
        ["screen"] = LiveModes.Screen, ["tela"] = LiveModes.Screen, ["cor-da-tela"] = LiveModes.Screen,
        ["cpu"] = LiveModes.Cpu,
        ["timer"] = LiveModes.Timer, ["cronometro"] = LiveModes.Timer,
    };

    public const string Usage =
        """
        AkiraCore: linha de comando

          AkiraCore.exe [opções]        (ou: dotnet akira [opções])

        Com o app aberto, o pedido vai para ele. Fechado, ele abre na bandeja e executa.

        Luz
          --ligar | --on                 Liga a luz
          --desligar | --off             Desliga a luz
          --alternar | --toggle          Liga se estiver desligada, desliga se estiver ligada
          --efeito | --effect <nome>     Ex.: rolando, estatico, respiracao, "acender ao tocar"
          --cor | --color <cor>          #RRGGBB ou: vermelho, laranja, amarelo, verde, ciano,
                                         azul, roxo, rosa, branco
          --brilho | --brightness <0-5>
          --velocidade | --speed <0-5>
          --arco-iris | --rainbow        Liga o arco-íris (--sem-arco-iris desliga)
          --invertida | --reverse        Direção invertida (--normal volta)
          --esquema | --scheme <nome>    Usa um esquema de Cores por tecla

        Perfis e ao vivo
          --perfil | --profile <nome>    Troca de perfil
          --ao-vivo | --live <modo>      desligado, teclas, musica, tela, cpu, cronometro
          --cronometro | --timer <min>   Inicia o cronômetro na fileira F (1 a 180; "parar" cancela)

        Avisos
          --aviso | --alert [cor]        Pisca o teclado (padrão: vermelho)
          --vezes | --times <n>          Quantas piscadas (1 a 20, padrão 3)

        Outros
          --status                       Mostra o estado atual
          --escondido | --hidden         Abre direto na bandeja
          --ajuda | --help | -h          Esta ajuda

        Exemplos
          dotnet akira --efeito rolando --cor azul --brilho 3
          AkiraCore.exe --perfil Jogo
          AkiraCore.exe --aviso verde --vezes 5     (ex.: no fim de um pipeline)
        """;

    /// <exception cref="ArgumentException">Opção desconhecida ou valor inválido (mensagem para o usuário).</exception>
    public static CliRequest Parse(IReadOnlyList<string> args)
    {
        var request = new CliRequest();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string Value() => i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : throw new ArgumentException($"Falta o valor de {arg}.");
            string? OptionalValue() => i + 1 < args.Count && !args[i + 1].StartsWith('-') ? args[++i] : null;

            request = arg.Trim() switch
            {
                "--hidden" or "--escondido" => request with { Hidden = true },
                "--help" or "--ajuda" or "-h" or "-?" or "/?" => request with { Help = true },
                "--status" => request with { Status = true },
                "--on" or "--ligar" => request with { Light = true, ToggleLight = false },
                "--off" or "--desligar" => request with { Light = false, ToggleLight = false },
                "--toggle" or "--alternar" => request with { ToggleLight = true, Light = null },
                "--effect" or "--efeito" => request with { Effect = ParseEffect(Value()) },
                "--color" or "--cor" => request with { Color = ParseColor(Value()) },
                "--brightness" or "--brilho" => request with { Brightness = ParseInt(Value(), 0, ProtocolConstants.BrightnessMax, "O brilho") },
                "--speed" or "--velocidade" => request with { Speed = ParseInt(Value(), 0, ProtocolConstants.SpeedMax, "A velocidade") },
                "--rainbow" or "--arco-iris" => request with { Rainbow = true },
                "--no-rainbow" or "--sem-arco-iris" => request with { Rainbow = false },
                "--reverse" or "--invertida" => request with { Reverse = true },
                "--normal" => request with { Reverse = false },
                "--profile" or "--perfil" => request with { Profile = ParseName(Value(), "perfil") },
                "--scheme" or "--esquema" => request with { Scheme = ParseName(Value(), "esquema") },
                "--live" or "--ao-vivo" => request with { Live = ParseLive(Value()) },
                "--timer" or "--cronometro" => ParseTimer(request, Value()),
                "--alert" or "--aviso" => request with { AlertColor = OptionalValue() is { } c ? ParseColor(c) : "#FF0000" },
                "--times" or "--vezes" => request with { AlertTimes = ParseInt(Value(), 1, AlertFlash.MaxTimes, "O número de piscadas") },
                _ => throw new ArgumentException($"Opção desconhecida: {arg}. Use --ajuda para ver as opções."),
            };
        }
        return request;
    }

    /// <summary>Chave do efeito pelo nome em inglês (rolling) ou em português, com ou sem acento.</summary>
    /// <exception cref="ArgumentException">Nenhum efeito com esse nome.</exception>
    public static string ParseEffect(string name)
    {
        var wanted = Simplify(name);
        var effect = EffectCatalog.All.FirstOrDefault(e => e.Mode != 0 && (Simplify(e.Key) == wanted || Simplify(e.Label) == wanted));
        return effect?.Key ?? throw new ArgumentException(
            $"Efeito desconhecido: {name}. Opções: {string.Join(", ", EffectCatalog.All.Where(e => e.Mode != 0).Select(e => Simplify(e.Label)))}.");
    }

    /// <exception cref="ArgumentException">Nem #RRGGBB nem um nome conhecido.</exception>
    public static string ParseColor(string text)
    {
        if (ColorNames.TryGetValue(Simplify(text), out var named))
        {
            return named;
        }
        try
        {
            var rgb = Rgb.FromHex(text.Trim());
            return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ArgumentException($"Cor inválida: {text}. Use #RRGGBB ou um nome, ex.: azul.");
        }
    }

    /// <summary>"Acender ao tocar" → "acender-ao-tocar": minúsculas, sem acento, hífen no lugar de espaço.</summary>
    public static string Simplify(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(c is ' ' or '_' ? '-' : c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int ParseInt(string text, int min, int max, string what) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? value
            : throw new ArgumentException($"{what} vai de {min} a {max} (veio {text}).");

    private static string ParseName(string text, string what) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException($"Falta o nome do {what}.") : text.Trim();

    private static string ParseLive(string text) =>
        LiveNames.TryGetValue(Simplify(text), out var mode)
            ? mode
            : throw new ArgumentException($"Modo ao vivo desconhecido: {text}. Opções: desligado, teclas, musica, tela, cpu, cronometro.");

    private static CliRequest ParseTimer(CliRequest request, string text) =>
        Simplify(text) is "parar" or "stop" or "cancelar"
            ? request with { TimerStop = true, TimerMinutes = null }
            : request with { TimerMinutes = ParseInt(text, 1, LiveSettingsState.MaxTimerMinutes, "O cronômetro"), TimerStop = false };
}
