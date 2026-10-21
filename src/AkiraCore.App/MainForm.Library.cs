using System.Text.Json;
using AkiraCore.Core;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Macros;
using AkiraCore.Core.Presentation;

namespace AkiraCore.App;

/// <summary>
/// Mensagens das bibliotecas: esquemas de cores por tecla, texto que vira macro, perfis e os
/// arquivos de backup, macros e cores. Contrato em CLAUDE.md.
/// </summary>
public sealed partial class MainForm
{
    private const string FileFilter = "Arquivos do AkiraCore (*.json)|*.json|Todos os arquivos (*.*)|*.*";

    private void HandleLibraryMessage(JsonElement message)
    {
        switch (message.GetProperty("type").GetString())
        {
            case "saveCustomLights":
                AcceptCustomLights(message);
                break;
            case "useCustomLight":
                UseCustomLight(message.GetProperty("id").GetString());
                break;
            case "textMacro":
                BuildTextMacro(message);
                break;
            case "exportBackup":
                ExportBackup();
                break;
            case "importBackup":
                _ = ImportBackupAsync();
                break;
            case "exportMacros":
                ExportMacros(message);
                break;
            case "importMacros":
                ImportMacros();
                break;
            case "exportColors":
                ExportColors(message.GetProperty("id").GetString());
                break;
            case "importColors":
                ImportColors();
                break;
            case "switchProfile":
                _ = SwitchProfileAsync(message.GetProperty("id").GetString());
                break;
            case "createProfile":
                CreateProfile(message.GetProperty("name").GetString());
                break;
            case "renameProfile":
                RenameProfile(message.GetProperty("id").GetString(), message.GetProperty("name").GetString());
                break;
            case "deleteProfile":
                DeleteProfile(message.GetProperty("id").GetString());
                break;
        }
    }

    // --- Cores por tecla ------------------------------------------------------------

    private object CustomLightsView() => new { schemes = _settings.CustomLights, activeId = _settings.CustomLightId };

    private void PostCustomLights() => Post(new { type = "customLights", customLights = CustomLightsView() });

    private CustomLightState? ActiveCustomLight() =>
        _settings.CustomLightId is { } id ? _settings.CustomLights.FirstOrDefault(s => s.Id == id) : null;

    /// <summary>
    /// A página manda a biblioteca inteira a cada mudança (pintar, renomear, apagar). Só reenvia
    /// ao teclado se o esquema em uso mudou: pintar outro esquema não mexe na luz.
    /// </summary>
    private void AcceptCustomLights(JsonElement message)
    {
        var schemes = message.GetProperty("schemes").Deserialize<List<CustomLightState>>(Json) ?? [];
        try
        {
            CustomLightState.ValidateAll(schemes);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", ex is ArgumentException ? ex.Message : "Esquema de cores incompleto.");
            PostCustomLights(); // a tela volta ao que está salvo
            return;
        }

        var before = ActiveCustomLight();
        _settings.CustomLights = schemes;
        DropMissingSchemesFromProfiles();
        var after = ActiveCustomLight();
        if (before is not null && after is null)
        {
            // Esquema em uso apagado: volta ao efeito
            _settings.CustomLightId = null;
            SaveSettings();
            PostCustomLights();
            QueueApply();
            return;
        }
        SaveSettings();
        if (after is not null && JsonSerializer.Serialize(before, Json) != JsonSerializer.Serialize(after, Json))
        {
            QueueApply();
        }
    }

    private void UseCustomLight(string? id)
    {
        if (id is null || _settings.CustomLights.All(s => s.Id != id))
        {
            return;
        }
        _settings.CustomLightId = id;
        _settings.LightOn = true;
        SaveSettings();
        QueueApply();
        PostCustomLights();
    }

    private void DropMissingSchemesFromProfiles()
    {
        var ids = _settings.CustomLights.Select(s => s.Id).ToHashSet();
        _settings.Profiles = _settings.Profiles.Select(p =>
            p.Config.CustomLightId is { } id && !ids.Contains(id)
                ? p with { Config = p.Config with { CustomLightId = null } }
                : p).ToList();
    }

    // --- Texto em macro ---------------------------------------------------------------

    /// <summary>Converte no layout atual do Windows e devolve as ações para a macro em edição.</summary>
    private void BuildTextMacro(JsonElement message)
    {
        var text = message.GetProperty("text").GetString() ?? "";
        var delay = Math.Clamp(message.GetProperty("delay").GetInt32(), 0, Core.Protocol.MacroProtocol.MaxDelayMs);
        var layout = WindowsTextLayout.Current();
        try
        {
            var result = TextMacro.Build(text, layout, delay);
            Post(new
            {
                type = "macroText",
                events = result.Events.Select(MacroEventState.From),
                skipped = result.Skipped,
                layout = layout.Name,
            });
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
        }
    }

    // --- Arquivos -----------------------------------------------------------------------

    private void ExportBackup()
    {
        SaveSettings(); // o perfil ativo leva o que está em uso
        var backup = BackupState.Create(CurrentProfileConfig(), _settings.Macros, _settings.CustomLights,
            _settings.Profiles, _settings.ActiveProfileId);
        WriteFile("Exportar backup", $"AkiraCore backup {DateTime.Now:yyyy-MM-dd}.json",
            ConfigFiles.BackupToJson(backup), "Backup exportado");
    }

    private async Task ImportBackupAsync()
    {
        if (ReadFile("Importar backup") is not { } json)
        {
            return;
        }
        BackupState backup;
        try
        {
            backup = ConfigFiles.ParseBackup(json);
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
            return;
        }

        _settings.Macros = backup.Macros.ToList();
        _settings.CustomLights = backup.CustomLights.ToList();
        _settings.Profiles = (backup.Profiles ?? []).ToList();
        _settings.ActiveProfileId = backup.ActiveProfileId;
        LoadProfileConfig(backup.Current);
        EnsureProfiles();
        SaveSettings();
        PostState();
        if (await SendWholeConfigAsync(includeMacros: true))
        {
            PostStatus("ok", "Backup importado e gravado no teclado");
        }
    }

    /// <summary>Exporta a lista que está na tela, inclusive mudanças ainda não gravadas.</summary>
    private void ExportMacros(JsonElement message)
    {
        var macros = message.GetProperty("macros").Deserialize<List<MacroState>>(Json) ?? [];
        try
        {
            if (macros.Count == 0)
            {
                throw new ArgumentException("Nenhuma macro para exportar.");
            }
            MacroState.ToMacros(macros);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", ex is ArgumentException ? ex.Message : "Macro incompleta.");
            return;
        }
        WriteFile("Exportar macros", "Macros do AKIRA.json", ConfigFiles.MacrosToJson(macros), "Macros exportadas");
    }

    /// <summary>As macros importadas entram na lista da tela; vão ao teclado em "Gravar no teclado".</summary>
    private void ImportMacros()
    {
        if (ReadFile("Importar macros") is not { } json)
        {
            return;
        }
        try
        {
            Post(new { type = "macrosImported", macros = ConfigFiles.ParseMacros(json) });
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
        }
    }

    private void ExportColors(string? id)
    {
        if (_settings.CustomLights.FirstOrDefault(s => s.Id == id) is not { } scheme)
        {
            return;
        }
        var name = string.Concat(scheme.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        WriteFile("Exportar esquema de cores", $"{(name.Length > 0 ? name : "Cores")}.json",
            ConfigFiles.ColorsToJson([scheme]), "Esquema exportado");
    }

    private void ImportColors()
    {
        if (ReadFile("Importar esquema de cores") is not { } json)
        {
            return;
        }
        try
        {
            var imported = ConfigFiles.ParseColors(json);
            if (_settings.CustomLights.Count + imported.Count > CustomLightState.MaxSchemes)
            {
                throw new ArgumentException($"Cabem no máximo {CustomLightState.MaxSchemes} esquemas de cores.");
            }
            var names = _settings.CustomLights.Select(s => s.Name).ToHashSet();
            foreach (var scheme in imported)
            {
                var name = scheme.Name;
                for (var n = 2; names.Contains(name); n++)
                {
                    name = $"{scheme.Name[..Math.Min(scheme.Name.Length, CustomLightState.NameMaxLength - 5)]} ({n})";
                }
                names.Add(name);
                _settings.CustomLights.Add(scheme with { Name = name });
            }
            SaveSettings();
            PostCustomLights();
            PostStatus("ok", imported.Count == 1 ? "Esquema importado" : $"{imported.Count} esquemas importados");
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
        }
    }

    private void WriteFile(string title, string fileName, string content, string done)
    {
        using var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = fileName,
            Filter = FileFilter,
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, content);
            PostStatus("ok", done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PostStatus("error", "Não foi possível salvar: " + ex.Message);
        }
    }

    private string? ReadFile(string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = FileFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }
        try
        {
            if (new FileInfo(dialog.FileName).Length > ConfigFiles.MaxFileBytes)
            {
                PostStatus("error", "O arquivo é grande demais para ser do AkiraCore.");
                return null;
            }
            return File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PostStatus("error", "Não foi possível ler: " + ex.Message);
            return null;
        }
    }

    /// <summary>Regrava tudo que o app controla: depois de importar um backup ou trocar de perfil.</summary>
    private async Task<bool> SendWholeConfigAsync(bool includeMacros)
    {
        IReadOnlyList<Macro> macros;
        (KeyRemapTable Normal, KeyRemapTable Fn) tables;
        Core.Device.DeviceSettings device;
        Action<KeyboardClient> lighting;
        try
        {
            macros = MacroState.ToMacros(_settings.Macros);
            tables = KeyProfile.Build(_settings.KeyAssignments, _settings.Macros);
            device = CurrentDeviceSettings();
            lighting = CurrentLightingAction();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or NullReferenceException)
        {
            PostStatus("error", ex is NullReferenceException ? "Configuração incompleta." : ex.Message);
            return false;
        }
        return await SendAsync(client =>
        {
            if (includeMacros)
            {
                client.ApplyMacros(macros);
            }
            client.ApplyKeyRemap(tables.Normal, tables.Fn);
            client.ApplyDeviceSettings(device);
            lighting(client);
        });
    }
}
