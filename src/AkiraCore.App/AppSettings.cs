using System.Text.Json;

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
    public string SideLightKey { get; set; } = "flowing";

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
