using Microsoft.Win32;

namespace AkiraCore.App;

/// <summary>
/// Iniciar com o Windows: entrada em HKCU\...\Run abrindo o app escondido na bandeja.
/// O registro é a fonte da verdade (não o settings.json), para a chave refletir também
/// o que o usuário mudar no Gerenciador de Tarefas.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Onde o Gerenciador de Tarefas guarda "Desabilitado" sem apagar a entrada do Run
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "AkiraCore";

    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string)
            {
                return false;
            }
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            // Primeiro byte par (02, 06) = habilitado; ímpar (03) = desabilitado
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
    }

    public static void Set(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        // Sem a marca antiga do Gerenciador de Tarefas, a entrada nova vale como habilitada
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? Application.ExecutablePath;
            run.SetValue(ValueName, $"\"{exe}\" --hidden");
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
