using AkiraCore.Core.Presentation;

namespace AkiraCore.App;

/// <summary>
/// Perfis ("Minha configuração exclusiva" do oficial). Os campos de AppSettings são o perfil
/// ativo em uso; cada gravação copia o estado para a entrada dele na lista, e trocar de perfil
/// carrega a cópia do outro e regrava teclas, configurações e iluminação no teclado.
/// </summary>
public sealed partial class MainForm
{
    private ProfileConfigState CurrentProfileConfig() =>
        new(_lighting, _settings.LightOn, _settings.CustomLightId, _settings.KeyAssignments.ToList(), _settings.Device);

    private void LoadProfileConfig(ProfileConfigState config)
    {
        _lighting = config.Lighting;
        _settings.LightOn = config.LightOn;
        _settings.CustomLightId = config.CustomLightId;
        _settings.KeyAssignments = config.KeyAssignments.ToList();
        _settings.Device = config.Device;
    }

    /// <summary>Sempre há um perfil ativo; na primeira vez ele nasce com o que já estava configurado.</summary>
    private void EnsureProfiles()
    {
        if (_settings.Profiles.Any(p => p.Id == _settings.ActiveProfileId))
        {
            return;
        }
        if (_settings.Profiles.Count < ProfileState.MaxProfiles)
        {
            var name = _settings.Profiles.Count == 0 ? "Padrão" : UniqueProfileName("Perfil em uso");
            var profile = new ProfileState(ConfigFiles.NewId(), name, CurrentProfileConfig());
            _settings.Profiles.Add(profile);
            _settings.ActiveProfileId = profile.Id;
        }
        else
        {
            _settings.ActiveProfileId = _settings.Profiles[0].Id;
        }
    }

    private void SyncActiveProfile()
    {
        var index = _settings.Profiles.FindIndex(p => p.Id == _settings.ActiveProfileId);
        if (index >= 0)
        {
            _settings.Profiles[index] = _settings.Profiles[index] with { Config = CurrentProfileConfig() };
        }
    }

    private object ProfilesView() => new
    {
        list = _settings.Profiles.Select(p => new { id = p.Id, name = p.Name }),
        activeId = _settings.ActiveProfileId,
    };

    private void PostProfiles() => Post(new { type = "profiles", profiles = ProfilesView() });

    private async Task SwitchProfileAsync(string? id)
    {
        if (id is null || id == _settings.ActiveProfileId
            || _settings.Profiles.FirstOrDefault(p => p.Id == id) is not { } target)
        {
            return;
        }
        SaveSettings(); // guarda o estado atual no perfil que está saindo
        try
        {
            target.Config.Validate(_settings.Macros, _settings.CustomLights);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", "Perfil com configuração inválida: " + ex.Message);
            return;
        }
        LoadProfileConfig(target.Config);
        _settings.ActiveProfileId = target.Id;
        SaveSettings();
        PostState();
        if (await SendWholeConfigAsync(includeMacros: false))
        {
            PostStatus("ok", $"Perfil {target.Name} aplicado");
        }
    }

    /// <summary>O perfil novo começa como cópia do atual e já fica ativo (nada muda no teclado).</summary>
    private void CreateProfile(string? name)
    {
        try
        {
            if (_settings.Profiles.Count >= ProfileState.MaxProfiles)
            {
                throw new ArgumentException($"Cabem no máximo {ProfileState.MaxProfiles} perfis.");
            }
            var profile = new ProfileState(ConfigFiles.NewId(), UniqueProfileName(ProfileState.ValidateName(name)),
                CurrentProfileConfig());
            SaveSettings();
            _settings.Profiles.Add(profile);
            _settings.ActiveProfileId = profile.Id;
            SaveSettings();
            PostProfiles();
            PostStatus("ok", $"Perfil {profile.Name} criado");
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
            PostProfiles();
        }
    }

    private void RenameProfile(string? id, string? name)
    {
        var index = _settings.Profiles.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return;
        }
        try
        {
            var valid = ProfileState.ValidateName(name);
            if (valid != _settings.Profiles[index].Name)
            {
                _settings.Profiles[index] = _settings.Profiles[index] with { Name = UniqueProfileName(valid) };
                SaveSettings();
            }
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
        }
        PostProfiles();
    }

    /// <summary>O perfil ativo não pode ser apagado: troque antes. Sempre fica pelo menos um.</summary>
    private void DeleteProfile(string? id)
    {
        if (id is null || id == _settings.ActiveProfileId || _settings.Profiles.Count <= 1)
        {
            return;
        }
        _settings.Profiles.RemoveAll(p => p.Id == id);
        SaveSettings();
        PostProfiles();
    }

    private string UniqueProfileName(string name)
    {
        var names = _settings.Profiles.Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var unique = name;
        for (var n = 2; names.Contains(unique); n++)
        {
            unique = $"{name[..Math.Min(name.Length, ProfileState.NameMaxLength - 5)]} ({n})";
        }
        return unique;
    }

    /// <summary>Teclas guardadas em outros perfis que apontam para macros apagadas voltam ao original.</summary>
    private void DropMissingMacrosFromProfiles()
    {
        var ids = _settings.Macros.Select(m => m.Id).ToHashSet();
        _settings.Profiles = _settings.Profiles.Select(p => p with
        {
            Config = p.Config with
            {
                KeyAssignments = p.Config.KeyAssignments
                    .Where(a => a.Action != "macro" || (a.MacroId is not null && ids.Contains(a.MacroId)))
                    .ToList(),
            },
        }).ToList();
    }

    private void FillTrayProfiles(ToolStripMenuItem item)
    {
        item.DropDownItems.Clear();
        foreach (var profile in _settings.Profiles)
        {
            var id = profile.Id;
            item.DropDownItems.Add(new ToolStripMenuItem(profile.Name, null, (_, _) => _ = SwitchProfileAsync(id))
            {
                Checked = id == _settings.ActiveProfileId,
            });
        }
    }
}
