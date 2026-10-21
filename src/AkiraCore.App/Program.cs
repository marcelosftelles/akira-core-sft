using System.Diagnostics;
using AkiraCore.App.Platform;
using AkiraCore.Core.Automation;

namespace AkiraCore.App;

internal static class Program
{
    private const string MutexName = "AkiraCore.SingleInstance";
    private const string ShowEventName = "AkiraCore.ShowWindow";

    [STAThread]
    private static int Main(string[] args)
    {
        CliRequest request;
        try
        {
            request = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            return Print(ex.Message, error: true);
        }
        if (request.Help)
        {
            return Print(CommandLine.Usage);
        }

        // Uma instância só: duas mandando comandos ao mesmo tempo bagunçariam a transação
        var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            mutex.Dispose();
            if (request.HasActions)
            {
                return Forward(args, TimeSpan.FromSeconds(5));
            }
            // Em vez de sair calada, pede para a instância aberta aparecer
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
            {
                showEvent.Set();
                showEvent.Dispose();
            }
            return 0;
        }

        if (request.HasActions)
        {
            // Nenhuma instância aberta: abre uma na bandeja e entrega o pedido a ela. Assim quem
            // chamou (script, atalho, pipeline) recebe a resposta e segue, sem ficar preso no app.
            mutex.ReleaseMutex();
            mutex.Dispose();
            if (!StartHiddenInstance())
            {
                return Print("Não foi possível abrir o AkiraCore.", error: true);
            }
            return Forward(args, TimeSpan.FromSeconds(30));
        }

        try
        {
            Run(request.Hidden);
        }
        finally
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
        return 0;
    }

    private static void Run(bool hidden)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var form = new MainForm(startHidden: hidden);
        var registration = ThreadPool.RegisterWaitForSingleObject(showRequested,
            (_, _) => form.ActivateFromAnotherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(form);
        registration.Unregister(null);
    }

    private static int Forward(string[] args, TimeSpan timeout)
    {
        var reply = CommandChannel.Send(args, timeout);
        return reply is null
            ? Print("O AkiraCore aberto não respondeu. Tente de novo em alguns segundos.", error: true)
            : Print(reply.Text, error: !reply.Ok);
    }

    private static bool StartHiddenInstance()
    {
        var path = Environment.ProcessPath;
        if (path is null)
        {
            return false;
        }
        // Rodando como "dotnet AkiraCore.dll": o executável é o dotnet e a dll vai como argumento
        var viaDotnet = Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var arguments = viaDotnet ? $"\"{typeof(Program).Assembly.Location}\" --hidden" : "--hidden";
        try
        {
            // ShellExecute não herda a saída deste processo: um script que lê a resposta não fica
            // esperando o app (que continua aberto) fechar o pipe
            Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Escreve no terminal que chamou. O app é de janela (sem console próprio): pega o console do pai
    /// ou a saída redirecionada; sem nenhum dos dois (atalho com argumento errado), mostra uma caixa.
    /// </summary>
    private static int Print(string text, bool error = false)
    {
        var attached = NativeSystem.AttachConsole(NativeSystem.AttachParentProcess);
        if (!attached && !Console.IsOutputRedirected)
        {
            MessageBox.Show(text, "AkiraCore", MessageBoxButtons.OK, error ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return error ? 1 : 0;
        }
        // Console.Out usa a página de código do console: acentos saem certos no cmd e no PowerShell
        var writer = error ? Console.Error : Console.Out;
        writer.WriteLine();
        writer.WriteLine(text);
        writer.Flush();
        return error ? 1 : 0;
    }
}
