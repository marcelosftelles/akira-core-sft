using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Tests;

public class ConfigFilesTests
{
    private static readonly LightingState Lighting = new("rolling", "#FF0000", 5, 3, 0, true);

    private static readonly MacroState Macro = new("m1", "Copiar", [
        new MacroEventState("down", 0xE0, 10), new MacroEventState("down", 0x06, 10),
        new MacroEventState("up", 0x06, 10), new MacroEventState("up", 0xE0, 10),
    ]);

    private static readonly CustomLightState Scheme = new("c1", "WASD", 4,
        new Dictionary<int, string> { [39] = "#FF0000", [56] = "#00FF00" });

    private static ProfileConfigState Config(string? customLightId = null) =>
        new(Lighting, true, customLightId,
            [new KeyAssignmentState(2, "normal", "macro", MacroId: "m1", Playback: "once")],
            new DeviceSettingsState(true, false, false, false, "5m", 0));

    private static BackupState Backup() => BackupState.Create(Config("c1"), [Macro], [Scheme],
        [new ProfileState("p1", "Jogos", Config()), new ProfileState("p2", "Trabalho", Config("c1"))], "p2");

    [Fact]
    public void Backup_RoundTrips()
    {
        var parsed = ConfigFiles.ParseBackup(ConfigFiles.BackupToJson(Backup()));

        Assert.Equal("c1", parsed.Current.CustomLightId);
        Assert.Equal("Copiar", parsed.Macros[0].Name);
        Assert.Equal("#00FF00", parsed.CustomLights[0].Colors[56]);
        Assert.Equal(["Jogos", "Trabalho"], parsed.Profiles.Select(p => p.Name));
        Assert.Equal("p2", parsed.ActiveProfileId);
    }

    [Fact]
    public void Backup_UsesCamelCaseAndSaysWhatItIs()
    {
        var json = ConfigFiles.BackupToJson(Backup());

        Assert.Contains("\"format\": \"akiracore-backup\"", json);
        Assert.Contains("\"keyAssignments\"", json);
    }

    [Fact]
    public void Backup_RejectsOtherFiles()
    {
        var macros = ConfigFiles.MacrosToJson([Macro]);

        var ex = Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup(macros));
        Assert.Contains("não é um backup", ex.Message);
        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup("isto não é json"));
        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup("{\"format\":\"akiracore-backup\",\"version\":1}"));
    }

    [Fact]
    public void Backup_RejectsNewerVersions()
    {
        var json = ConfigFiles.BackupToJson(Backup() with { Version = 99 });

        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup(json));
    }

    [Fact]
    public void Backup_RejectsBrokenReferences()
    {
        // Tecla apontando para macro que não está no arquivo
        var noMacro = Backup() with { Macros = [] };
        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup(ConfigFiles.BackupToJson(noMacro)));

        // Perfil usando esquema de cores apagado
        var noScheme = Backup() with { CustomLights = [] };
        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup(ConfigFiles.BackupToJson(noScheme)));

        var missingActive = Backup() with { ActiveProfileId = "outro" };
        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup(ConfigFiles.BackupToJson(missingActive)));
    }

    [Fact]
    public void Backup_KeepsTheSettingsOfEachEffect()
    {
        var memory = new Dictionary<string, EffectSettingsState> { ["scrolling"] = new("#00FF00", 2, 4, 3, false) };
        var backup = Backup() with { Current = Config() with { EffectSettings = memory } };

        var parsed = ConfigFiles.ParseBackup(ConfigFiles.BackupToJson(backup));

        Assert.Equal(memory["scrolling"], parsed.Current.EffectSettings!["scrolling"]);
    }

    [Fact]
    public void Backup_FromOlderVersionLoadsWithoutEffectSettings()
    {
        // Versão anterior: sem "effectSettings" e com "reverse" no lugar de "direction"
        var json = ConfigFiles.BackupToJson(Backup())
            .Replace("\"direction\": 0", "\"reverse\": true")
            .Replace("\"effectSettings\": null,", "");

        var parsed = ConfigFiles.ParseBackup(json);

        Assert.Null(parsed.Current.EffectSettings);
        Assert.Equal(0, parsed.Current.Lighting.Direction);
    }

    [Fact]
    public void Backup_RejectsEffectSettingsThatCannotBeSent()
    {
        var memory = new Dictionary<string, EffectSettingsState> { ["rolling"] = new("#00FF00", 5, 3, 2, true) };
        var backup = Backup() with { Current = Config() with { EffectSettings = memory } };

        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseBackup(ConfigFiles.BackupToJson(backup)));
    }

    [Fact]
    public void Macros_GetFreshIdsOnImport()
    {
        var imported = ConfigFiles.ParseMacros(ConfigFiles.MacrosToJson([Macro]));

        Assert.Single(imported);
        Assert.NotEqual("m1", imported[0].Id);
        Assert.Equal(4, imported[0].Events.Count);
    }

    [Fact]
    public void Macros_RejectInvalidEvents()
    {
        var bad = Macro with { Events = [new MacroEventState("down", 0x00, 10)] };

        Assert.Throws<ArgumentException>(() => ConfigFiles.ParseMacros(ConfigFiles.MacrosToJson([bad])));
    }

    [Fact]
    public void Colors_RoundTripWithFreshIds()
    {
        var imported = ConfigFiles.ParseColors(ConfigFiles.ColorsToJson([Scheme]));

        Assert.NotEqual("c1", imported[0].Id);
        Assert.Equal("#FF0000", imported[0].Colors[39]);
    }
}
