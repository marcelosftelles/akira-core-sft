using AkiraCore.Core.Automation;
using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Keys;

/// <summary>
/// Tecla com ação do app: o firmware não abre programa nem digita texto sozinho. O oficial grava um
/// slot próprio (tipos 8 a 10, 12 e 13) que só funciona com o app dele aberto. Aqui a tecla é
/// remapeada, pelo formato já confirmado (02, modificadores, código), para um atalho que ninguém
/// usa (F13 a F24 com Ctrl, Alt e Shift); o app registra esse atalho no Windows e executa a ação.
/// Também só funciona com o app aberto, mas sem mandar ao teclado nada fora do que já se conhece.
/// </summary>
public sealed record AppKeyBinding(int Slot, KeyAssignmentState Assignment)
{
    // Combinações na ordem de uso: as com mais modificadores primeiro, que nenhum programa usa
    private static readonly KeyModifiers[] Combos =
    [
        KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt | KeyModifiers.LeftShift,
        KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt,
        KeyModifiers.LeftCtrl | KeyModifiers.LeftShift,
        KeyModifiers.LeftAlt | KeyModifiers.LeftShift,
    ];

    public const int FunctionKeys = 12;
    public static int Capacity => Combos.Length * FunctionKeys;

    public KeyModifiers Modifiers => Combos[Slot / FunctionKeys];

    /// <summary>Código HID de F13 a F24 (0x68 a 0x73).</summary>
    public byte Usage => (byte)(0x68 + Slot % FunctionKeys);

    /// <summary>VK_F13 a VK_F24, para o RegisterHotKey.</summary>
    public int VirtualKey => 0x7C + Slot % FunctionKeys;

    public HotkeyModifiers HotkeyModifiers =>
        (Modifiers.HasFlag(KeyModifiers.LeftCtrl) ? HotkeyModifiers.Control : 0)
        | (Modifiers.HasFlag(KeyModifiers.LeftAlt) ? HotkeyModifiers.Alt : 0)
        | (Modifiers.HasFlag(KeyModifiers.LeftShift) ? HotkeyModifiers.Shift : 0);

    public KeyAction ToKeyAction() => new KeyComboAction(Modifiers, Usage);
}

public static class AppKeyActions
{
    public const string Open = "open";
    public const string Site = "site";
    public const string Text = "text";
    public const string Media = "media";
    public const string Lock = "lock";
    public const string Sleep = "sleep";
    public const string Shutdown = "shutdown";
    public const int TargetMaxLength = 500;

    public static IReadOnlyList<Option<bool>> Kinds { get; } =
    [
        new(Open, "Abrir programa ou arquivo", true),
        new(Site, "Abrir site", true),
        new(Text, "Digitar texto", true),
        new(Media, "Multimídia estendida", true),
        new(Lock, "Bloquear o computador", false),
        new(Sleep, "Suspender o computador", false),
        new(Shutdown, "Desligar o computador", false),
    ];

    /// <summary>Teclas multimídia que o firmware não tem (o "tipo 12" do oficial) e o VK_* de cada uma.</summary>
    public static IReadOnlyList<Option<int>> MediaKeys { get; } =
    [
        new("calculator", "Calculadora", 0xB7),
        new("mail", "E-mail", 0xB4),
        new("browser", "Navegador (página inicial)", 0xAC),
        new("computer", "Este computador", 0xB6),
        new("search", "Pesquisar", 0xAA),
        new("player", "Player de mídia", 0xB5),
        new("favorites", "Favoritos do navegador", 0xAB),
        new("back", "Voltar (navegador)", 0xA6),
        new("forward", "Avançar (navegador)", 0xA7),
        new("refresh", "Atualizar (navegador)", 0xA8),
    ];

    /// <exception cref="ArgumentException">Tipo desconhecido ou destino que não serve para ele.</exception>
    public static void Validate(string? kind, string? target)
    {
        var option = Kinds.FirstOrDefault(k => k.Key == kind)
                     ?? throw new ArgumentException($"Ação do app desconhecida: {kind}.");
        if (!option.Value)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(target) || target.Length > TargetMaxLength)
        {
            throw new ArgumentException(kind switch
            {
                Open => "Escolha o programa ou arquivo.",
                Site => "Escreva o endereço do site.",
                Text => $"Escreva o texto (até {TargetMaxLength} caracteres).",
                _ => "Escolha a tecla multimídia.",
            });
        }
        switch (kind)
        {
            case Site when !IsWebAddress(target):
                throw new ArgumentException("O site precisa começar com http:// ou https://.");
            case Media when MediaKeys.All(m => m.Key != target):
                throw new ArgumentException("Tecla multimídia desconhecida.");
        }
    }

    public static bool IsWebAddress(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Reserva um atalho para cada tecla com ação do app, sempre na mesma ordem (camada normal antes
    /// da Fn, depois pelo índice), para o mesmo conjunto de teclas dar os mesmos atalhos.
    /// </summary>
    /// <exception cref="ArgumentException">Ação inválida ou mais teclas do que atalhos livres.</exception>
    public static IReadOnlyList<AppKeyBinding> Allocate(IEnumerable<KeyAssignmentState> assignments)
    {
        var apps = assignments.Where(a => a?.Action == KeyAssignmentState.AppAction)
            .OrderBy(a => a.Layer == "fn" ? 1 : 0).ThenBy(a => a.Key).ToList();
        if (apps.Count > AppKeyBinding.Capacity)
        {
            throw new ArgumentException($"Cabem no máximo {AppKeyBinding.Capacity} teclas com ação do app.");
        }
        foreach (var app in apps)
        {
            Validate(app.App, app.Target);
        }
        return apps.Select((a, i) => new AppKeyBinding(i, a)).ToList();
    }
}
