using System.Runtime.InteropServices;
using System.Text;
using static AkiraCore.App.Platform.NativeSystem;

namespace AkiraCore.App.Platform;

/// <summary>
/// Avisa quando o programa em primeiro plano muda (perfil automático). Usa o gancho de eventos do
/// Windows, que chega pela fila de mensagens da thread da interface; não consulta nada em laço.
/// Só o nome do executável sai daqui, e nada é guardado.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc _callback; // referência viva: o Windows chama de volta depois
    private IntPtr _hook;
    private string? _last;

    public ForegroundWatcher() => _callback = OnEvent;

    /// <summary>Nome do executável em primeiro plano (ex.: "valorant.exe").</summary>
    public event Action<string>? Changed;

    public void Start()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0,
                WinEventOutOfContext);
        }
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _last = null;
    }

    public static string? CurrentProcessName() => ProcessName(GetForegroundWindow());

    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        var name = ProcessName(window);
        if (name is null || name == _last)
        {
            return;
        }
        _last = name;
        Changed?.Invoke(name);
    }

    private static string? ProcessName(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }
        GetWindowThreadProcessId(window, out var processId);
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return null; // processo de outro usuário ou do sistema
        }
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? Path.GetFileName(buffer.ToString()) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Bloqueio do Windows e tela ligada/desligada, para as regras de luz. As mensagens chegam na
/// janela principal; o WndProc dela chama <see cref="Process"/>.
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    private IntPtr _window;
    private IntPtr _powerNotification;

    public bool Locked { get; private set; }
    public bool DisplayOff { get; private set; }

    /// <summary>Algo mudou (bloqueio, tela ou volta da suspensão).</summary>
    public event Action? Changed;

    /// <summary>O PC voltou da suspensão: o teclado pode ter perdido a luz enviada.</summary>
    public event Action? Resumed;

    public void Attach(IntPtr window)
    {
        _window = window;
        WTSRegisterSessionNotification(window, NotifyForThisSession);
        var setting = ConsoleDisplayState;
        _powerNotification = RegisterPowerSettingNotification(window, ref setting, DeviceNotifyWindowHandle);
    }

    /// <summary>True se a mensagem era deste observador.</summary>
    public bool Process(int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmWtsSessionChange when wParam.ToInt32() is WtsSessionLock or WtsSessionUnlock:
                Locked = wParam.ToInt32() == WtsSessionLock;
                Changed?.Invoke();
                return true;
            case WmPowerBroadcast when wParam.ToInt32() == PbtPowerSettingChange && lParam != IntPtr.Zero:
                var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
                if (setting.PowerSetting == ConsoleDisplayState)
                {
                    DisplayOff = setting.Data == 0;
                    Changed?.Invoke();
                }
                return true;
            case WmPowerBroadcast when wParam.ToInt32() == PbtApmResumeAutomatic:
                Resumed?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero)
        {
            WTSUnRegisterSessionNotification(_window);
        }
        if (_powerNotification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_powerNotification);
            _powerNotification = IntPtr.Zero;
        }
    }
}
