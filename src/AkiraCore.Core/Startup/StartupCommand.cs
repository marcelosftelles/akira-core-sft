namespace AkiraCore.Core.Startup;

/// <summary>
/// Linha de comando gravada em HKCU\Software\Microsoft\Windows\CurrentVersion\Run para o app
/// abrir com o Windows, escondido na bandeja. O app e o instalador gravam o mesmo valor.
/// </summary>
public static class StartupCommand
{
    public const string ValueName = "AkiraCore";
    public const string HiddenArgument = "--hidden";

    /// <summary>Caminho entre aspas: pastas com espaço não quebram a linha de comando.</summary>
    public static string For(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\" {HiddenArgument}";
    }

    /// <summary>
    /// O usuário desligou a entrada no Gerenciador de Tarefas ou em Configurações › Inicialização?
    /// O Windows não apaga o valor do Run: grava um binário em
    /// HKCU\...\Explorer\StartupApproved\Run com o primeiro byte ímpar (03 = desabilitado,
    /// 02 = habilitado). Ausente conta como habilitado.
    /// </summary>
    public static bool IsDisabledByUser(byte[]? startupApprovedValue) =>
        startupApprovedValue is { Length: > 0 } value && (value[0] & 1) == 1;

    /// <summary>
    /// O valor gravado abre este mesmo executável? Uma entrada de outra cópia (pasta antiga,
    /// versão portátil) conta como desligada, e religar regrava apontando para cá.
    /// </summary>
    public static bool PointsTo(string? registryValue, string executablePath) =>
        registryValue is not null
        && !string.IsNullOrWhiteSpace(executablePath)
        && string.Equals(registryValue.Trim(), For(executablePath), StringComparison.OrdinalIgnoreCase);
}
