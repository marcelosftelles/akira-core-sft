using AkiraCore.Core.Startup;
using Microsoft.Win32;

namespace AkiraCore.App;

/// <summary>Liga e desliga a entrada "iniciar com o Windows" deste usuário (sem precisar de admin).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string? ExecutablePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return ExecutablePath is { } exe && StartupCommand.PointsTo(key?.GetValue(StartupCommand.ValueName) as string, exe);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = ExecutablePath ?? throw new InvalidOperationException("Caminho do executável desconhecido.");
            key.SetValue(StartupCommand.ValueName, StartupCommand.For(exe), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
        }
    }
}
