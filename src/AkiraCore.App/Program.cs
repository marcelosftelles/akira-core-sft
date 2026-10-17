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
        // Uma instância só: duas mandando comandos ao mesmo tempo bagunçariam a transação
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
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

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var form = new MainForm(startHidden: args.Contains(StartupCommand.HiddenArgument));
        var showRegistration = ThreadPool.RegisterWaitForSingleObject(showRequested,
            (_, _) => form.ActivateFromAnotherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);
        var exitRegistration = ThreadPool.RegisterWaitForSingleObject(exitRequested,
            (_, _) => form.ExitFromInstaller(), null, Timeout.Infinite, executeOnlyOnce: true);

        Application.Run(form);
        showRegistration.Unregister(null);
        exitRegistration.Unregister(null);
    }
}
