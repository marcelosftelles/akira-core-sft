using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Automation;

/// <summary>
/// Troca automática de perfil pelo programa em primeiro plano. O oficial guarda a coluna "app" em
/// t_profile_data; aqui cada perfil tem uma lista de executáveis (ex.: valorant.exe).
/// </summary>
public sealed record AutoProfileState(bool Enabled, string? FallbackId)
{
    public static AutoProfileState Default { get; } = new(false, null);
}

public static class ProfileApps
{
    public const int MaxPerProfile = 20;
    public const int MaxLength = 100;

    /// <summary>
    /// "C:\Jogos\Valorant.exe", "Valorant.EXE" ou "valorant" viram "valorant.exe": o Windows compara
    /// nomes de arquivo sem diferenciar maiúsculas.
    /// </summary>
    /// <exception cref="ArgumentException">Nome vazio, longo demais ou com caractere inválido.</exception>
    public static string Normalize(string? app)
    {
        var name = (app ?? "").Trim().Trim('"');
        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }
        name = name.ToLowerInvariant();
        if (name.Length == 0)
        {
            throw new ArgumentException("Programa sem nome.");
        }
        if (!name.EndsWith(".exe", StringComparison.Ordinal))
        {
            name += ".exe";
        }
        if (name.Length > MaxLength || name.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || name == ".exe")
        {
            throw new ArgumentException($"Nome de programa inválido: {app}.");
        }
        return name;
    }

    /// <summary>Lista limpa, sem repetição.</summary>
    /// <exception cref="ArgumentException">Algum nome inválido ou programas demais.</exception>
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string?>? apps)
    {
        var list = (apps ?? []).Select(Normalize).Distinct().ToList();
        if (list.Count > MaxPerProfile)
        {
            throw new ArgumentException($"Cada perfil aceita no máximo {MaxPerProfile} programas.");
        }
        return list;
    }

    /// <exception cref="ArgumentException">Algum programa em mais de um perfil, ou nome inválido.</exception>
    public static void ValidateAll(IReadOnlyList<ProfileState> profiles)
    {
        var owners = new Dictionary<string, string>();
        foreach (var profile in profiles)
        {
            foreach (var app in NormalizeAll(profile.Apps))
            {
                if (!owners.TryAdd(app, profile.Name))
                {
                    throw new ArgumentException($"{app} já está no perfil {owners[app]}.");
                }
            }
        }
    }

    /// <summary>Perfil ligado ao programa (nome do processo, com ou sem ".exe"), ou null.</summary>
    public static ProfileState? Match(IReadOnlyList<ProfileState> profiles, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }
        string app;
        try
        {
            app = Normalize(processName);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return profiles.FirstOrDefault(p => (p.Apps ?? []).Any(a => string.Equals(a, app, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Perfil que deve ficar ativo com o programa em primeiro plano: o do programa, senão o perfil
    /// de "nenhum programa" escolhido. Null = não mexer (automático desligado ou sem regra).
    /// </summary>
    public static string? Target(AutoProfileState settings, IReadOnlyList<ProfileState> profiles, string? processName)
    {
        if (!settings.Enabled)
        {
            return null;
        }
        if (Match(profiles, processName) is { } match)
        {
            return match.Id;
        }
        return settings.FallbackId is { } id && profiles.Any(p => p.Id == id) ? id : null;
    }
}
