using System.Security;
using AkiraCore.Core.Startup;
using Microsoft.Win32;

namespace AkiraCore.App;

/// <summary>
/// Liga e desliga a entrada "iniciar com o Windows" deste usuário (sem precisar de admin).
/// Respeita o que o usuário fez no Gerenciador de Tarefas: lá o Windows não apaga o Run,
/// só marca a entrada como desabilitada em StartupApproved.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>Nunca lança: é chamado a cada estado enviado para a interface.</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (ExecutablePath is not { } exe)
            {
                return false;
            }
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (!StartupCommand.PointsTo(run?.GetValue(StartupCommand.ValueName) as string, exe))
            {
                return false;
            }
            using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
            return !StartupCommand.IsDisabledByUser(approved?.GetValue(StartupCommand.ValueName) as byte[]);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            if (enabled)
            {
                var exe = ExecutablePath ?? throw new InvalidOperationException("Caminho do executável desconhecido.");
                run.SetValue(StartupCommand.ValueName, StartupCommand.For(exe), RegistryValueKind.String);
            }
            else
            {
                run.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
            }
        }
        // Ligar de novo pelo app desfaz um "Desabilitado" antigo do Gerenciador de Tarefas
        using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
        approved?.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
    }
}
