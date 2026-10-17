using AkiraCore.Core.Startup;

namespace AkiraCore.App;

internal static class Program
{
    private const string MutexName = "AkiraCore.SingleInstance";
    private const string ShowEventName = "AkiraCore.ShowWindow";
    // O instalador sinaliza este evento para o app sair antes de trocar ou apagar o .exe
    private const string ExitEventName = "AkiraCore.Exit";

    [STAThread]
    private static void Main(string[] args)
    {
        // Uma instância só: duas mandando comandos ao mesmo tempo bagunçariam a transação.
        // Sem using: o mutex fica com o processo até o fim (GC.KeepAlive lá embaixo), para o
        // instalador não ver o mutex sumir enquanto o .exe ainda está em uso.
        var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Em vez de sair calada, pede para a instância aberta aparecer
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
            {
                showEvent.Set();
                showEvent.Dispose();
            }
            return;
        }

        // Logo depois do mutex: uma instância viva sempre tem o evento que o instalador procura
        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm(startHidden: args.Contains(StartupCommand.HiddenArgument));
        var showRegistration = ThreadPool.RegisterWaitForSingleObject(showRequested,
            (_, _) => form.ActivateFromAnotherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);
        var exitRegistration = ThreadPool.RegisterWaitForSingleObject(exitRequested,
            (_, _) => form.ExitFromInstaller(), null, Timeout.Infinite, executeOnlyOnce: true);

        Application.Run(form);
        showRegistration.Unregister(null);
        exitRegistration.Unregister(null);
        GC.KeepAlive(mutex);
    }
}
