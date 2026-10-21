namespace AkiraCore.Core.Presentation;

/// <summary>
/// O que muda de um perfil para outro ("Minha configuração exclusiva" do oficial): iluminação,
/// teclas remapeadas e configurações do teclado. Macros e esquemas de cores ficam numa biblioteca
/// só, como no oficial (t_macro_data não tem coluna de perfil): as teclas de cada perfil apontam
/// para as mesmas macros gravadas no teclado. <see cref="CustomLightId"/> preenchido = o perfil
/// usa o esquema de cores por tecla no lugar do efeito.
/// </summary>
public sealed record ProfileConfigState(
    LightingState Lighting,
    bool LightOn,
    string? CustomLightId,
    IReadOnlyList<KeyAssignmentState> KeyAssignments,
    DeviceSettingsState? Device)
{
    /// <exception cref="ArgumentException">Algum valor fora da faixa ou referência que não existe.</exception>
    public void Validate(IReadOnlyList<MacroState> macros, IReadOnlyList<CustomLightState> customLights)
    {
        if (Lighting is null)
        {
            throw new ArgumentException("Perfil sem iluminação.");
        }
        try
        {
            Lighting.ToConfig();
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message);
        }
        if (CustomLightId is not null && customLights.All(s => s.Id != CustomLightId))
        {
            throw new ArgumentException("O esquema de cores do perfil não existe mais.");
        }
        if (KeyAssignments is null || KeyAssignments.Any(a => a is null))
        {
            throw new ArgumentException("Perfil com tecla vazia.");
        }
        KeyProfile.Build(KeyAssignments, macros);
        Device?.ToSettings();
    }
}

public sealed record ProfileState(string Id, string Name, ProfileConfigState Config)
{
    public const int NameMaxLength = 40;
    public const int MaxProfiles = 20;

    /// <exception cref="ArgumentException">Lista grande demais, nome inválido ou perfil inválido.</exception>
    public static void ValidateAll(IReadOnlyList<ProfileState> profiles, IReadOnlyList<MacroState> macros,
        IReadOnlyList<CustomLightState> customLights)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count > MaxProfiles)
        {
            throw new ArgumentException($"Guarde no máximo {MaxProfiles} perfis.");
        }
        if (profiles.Select(p => p?.Id).Distinct().Count() != profiles.Count)
        {
            throw new ArgumentException("Dois perfis com o mesmo identificador.");
        }
        foreach (var profile in profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
            {
                throw new ArgumentException("Perfil sem identificador.");
            }
            ValidateName(profile.Name);
            (profile.Config ?? throw new ArgumentException("Perfil sem configuração.")).Validate(macros, customLights);
        }
    }

    /// <exception cref="ArgumentException">Nome vazio ou longo demais.</exception>
    public static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException($"O nome do perfil precisa ter de 1 a {NameMaxLength} caracteres.");
        }
        return name.Trim();
    }
}
