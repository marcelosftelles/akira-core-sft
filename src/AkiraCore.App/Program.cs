namespace AkiraCore.App;

internal static class Program
{
    private const string MutexName = "AkiraCore.SingleInstance";
    private const string ShowEventName = "AkiraCore.ShowWindow";

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
        var form = new MainForm(startHidden: args.Contains("--hidden"));
        var registration = ThreadPool.RegisterWaitForSingleObject(showRequested,
            (_, _) => form.ActivateFromAnotherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(form);
        registration.Unregister(null);
    }
}
