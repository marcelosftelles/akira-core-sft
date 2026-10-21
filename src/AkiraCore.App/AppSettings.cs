using System.Text.Json;
using AkiraCore.Core.Automation;
using AkiraCore.Core.Presentation;

namespace AkiraCore.App;

/// <summary>Última configuração aplicada, salva em %AppData%\AkiraCore.</summary>
public sealed class AppSettings
{
    public string EffectKey { get; set; } = "static";
    public string ColorHex { get; set; } = "FF0000";
    public int Brightness { get; set; } = 5;
    public int Speed { get; set; } = 3;
    public bool ReverseDirection { get; set; }
    public bool Rainbow { get; set; }
    public bool SwapRedBlue { get; set; }
    public bool LightOn { get; set; } = true;
    public string SideLightKey { get; set; } = "flowing";
    // Remapeamento, macros e configurações gerais: o teclado não devolve o que tem gravado
    // (o oficial também só escreve), então o arquivo é a única referência do que foi configurado
    public List<KeyAssignmentState> KeyAssignments { get; set; } = [];
    public List<MacroState> Macros { get; set; } = [];
    public DeviceSettingsState? Device { get; set; }
    // Cores por tecla: biblioteca de esquemas e o que está no teclado (null = efeito)
    public List<CustomLightState> CustomLights { get; set; } = [];
    public string? CustomLightId { get; set; }
    // Perfis: os campos acima são o perfil ativo em uso; a lista guarda a cópia de cada um
    public List<ProfileState> Profiles { get; set; } = [];
    public string? ActiveProfileId { get; set; }
    public bool BatteryAlert { get; set; } = true;
    // Ao vivo: efeitos calculados no PC (04 20 no cabo, ritmo musical no dongle). Global, fora dos perfis
    public LiveSettingsState? Live { get; set; }
    public string LastLiveMode { get; set; } = LiveModes.Music;
    // Calibração do ritmo musical no dongle, ajustada no Laboratório até o formato ser confirmado
    public Live.DongleMusicCalibration DongleMusic { get; set; } = new();
    // Automação do lado do PC
    public LightRulesState? LightRules { get; set; }
    public AutoProfileState? AutoProfile { get; set; }
    public List<HotkeyState> Hotkeys { get; set; } = [];

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkiraCore", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new(); // arquivo corrompido não deve impedir o app de abrir
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
