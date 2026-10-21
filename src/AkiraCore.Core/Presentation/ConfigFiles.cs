using System.Text.Json;

namespace AkiraCore.Core.Presentation;

/// <summary>
/// Backup completo: a configuração em uso, as bibliotecas de macros e de cores e os perfis.
/// Importar substitui tudo e regrava o teclado.
/// </summary>
public sealed record BackupState(
    string Format,
    int Version,
    ProfileConfigState Current,
    IReadOnlyList<MacroState> Macros,
    IReadOnlyList<CustomLightState> CustomLights,
    IReadOnlyList<ProfileState> Profiles,
    string? ActiveProfileId)
{
    public const string FormatName = "akiracore-backup";

    public static BackupState Create(ProfileConfigState current, IReadOnlyList<MacroState> macros,
        IReadOnlyList<CustomLightState> customLights, IReadOnlyList<ProfileState> profiles, string? activeProfileId) =>
        new(FormatName, ConfigFiles.Version, current, macros, customLights, profiles, activeProfileId);

    /// <exception cref="ArgumentException">Alguma parte inválida.</exception>
    public void Validate()
    {
        var macros = Macros ?? throw new ArgumentException("Backup sem a lista de macros.");
        var customLights = CustomLights ?? throw new ArgumentException("Backup sem a lista de cores.");
        if (macros.Any(m => m is null))
        {
            throw new ArgumentException("Backup com macro vazia.");
        }
        MacroState.ToMacros(macros);
        CustomLightState.ValidateAll(customLights);
        (Current ?? throw new ArgumentException("Backup sem a configuração atual.")).Validate(macros, customLights);
        var profiles = Profiles ?? [];
        ProfileState.ValidateAll(profiles, macros, customLights);
        if (ActiveProfileId is not null && profiles.All(p => p.Id != ActiveProfileId))
        {
            throw new ArgumentException("O perfil ativo do backup não está na lista.");
        }
    }
}

/// <summary>
/// Arquivos que o usuário exporta e importa (JSON). Cada um diz o que é em "format", para o app
/// não confundir um backup com uma lista de macros, e tudo é validado antes de virar envio.
/// </summary>
public static class ConfigFiles
{
    public const int Version = 1;
    public const string MacrosFormat = "akiracore-macros";
    public const string ColorsFormat = "akiracore-colors";
    public const long MaxFileBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record MacrosFile(string Format, int Version, IReadOnlyList<MacroState> Macros);

    private sealed record ColorsFile(string Format, int Version, IReadOnlyList<CustomLightState> Schemes);

    public static string BackupToJson(BackupState backup) => JsonSerializer.Serialize(backup, Json);

    /// <exception cref="ArgumentException">Não é um backup do AkiraCore ou tem algo inválido.</exception>
    public static BackupState ParseBackup(string json)
    {
        var backup = Read<BackupState>(json, BackupState.FormatName, b => b.Format, b => b.Version);
        Guard(backup.Validate);
        return backup;
    }

    public static string MacrosToJson(IReadOnlyList<MacroState> macros) =>
        JsonSerializer.Serialize(new MacrosFile(MacrosFormat, Version, macros), Json);

    /// <summary>Macros de um arquivo, com identificadores novos para não colidir com as atuais.</summary>
    /// <exception cref="ArgumentException">Não é uma lista de macros do AkiraCore ou tem macro inválida.</exception>
    public static IReadOnlyList<MacroState> ParseMacros(string json)
    {
        var file = Read<MacrosFile>(json, MacrosFormat, f => f.Format, f => f.Version);
        var macros = (file.Macros ?? []).Select(m => m with { Id = NewId() }).ToList();
        Guard(() => MacroState.ToMacros(macros));
        return macros;
    }

    public static string ColorsToJson(IReadOnlyList<CustomLightState> schemes) =>
        JsonSerializer.Serialize(new ColorsFile(ColorsFormat, Version, schemes), Json);

    /// <summary>Esquemas de um arquivo, com identificadores novos.</summary>
    /// <exception cref="ArgumentException">Não é um arquivo de cores do AkiraCore ou tem esquema inválido.</exception>
    public static IReadOnlyList<CustomLightState> ParseColors(string json)
    {
        var file = Read<ColorsFile>(json, ColorsFormat, f => f.Format, f => f.Version);
        var schemes = (file.Schemes ?? []).Select(s => s with { Id = NewId() }).ToList();
        Guard(() => CustomLightState.ValidateAll(schemes));
        return schemes;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    private static T Read<T>(string json, string format, Func<T, string?> formatOf, Func<T, int> versionOf)
    {
        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            throw new ArgumentException("O arquivo não é um JSON válido.");
        }
        if (value is null || formatOf(value) != format)
        {
            throw new ArgumentException(format switch
            {
                BackupState.FormatName => "Este arquivo não é um backup do AkiraCore.",
                MacrosFormat => "Este arquivo não é uma lista de macros do AkiraCore.",
                _ => "Este arquivo não é um esquema de cores do AkiraCore.",
            });
        }
        if (versionOf(value) > Version)
        {
            throw new ArgumentException("O arquivo é de uma versão mais nova do AkiraCore.");
        }
        return value;
    }

    // Campo faltando no JSON chega como null em algum ponto da validação
    private static void Guard(Action validate)
    {
        try
        {
            validate();
        }
        catch (NullReferenceException)
        {
            throw new ArgumentException("O arquivo está incompleto.");
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message);
        }
    }
}
