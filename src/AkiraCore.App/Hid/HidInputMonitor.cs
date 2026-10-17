using System.ComponentModel;
using AkiraCore.Core.Diagnostics;
using static AkiraCore.App.Hid.NativeMethods;

namespace AkiraCore.App.Hid;

/// <summary>
/// Escuta input reports de todas as coleções do teclado que o Windows deixa abrir:
/// fabricante (o app oficial lê avisos "05 A6 ..." na 0xFFFF), multimídia (0x0C),
/// controle de sistema e o canal do dongle. Teclado e mouse ficam presos pelo
/// Windows; as teclas são vistas pelo RawKeyboardMonitor.
/// Cada handle recebe sua própria cópia dos reports: ler aqui não rouba o eco do dongle.
/// </summary>
public sealed class HidInputMonitor : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly List<FileStream> _streams = [];

    public event Action<string>? ReportReceived;

    public IReadOnlyList<string> Start()
    {
        var opened = new List<string>();
        foreach (var collection in HidDeviceFinder.ListCollections())
        {
            if (collection.InputReportLength == 0)
            {
                continue; // coleção só de feature (ex.: comandos do cabo): nada a escutar
            }
            if (collection.IsLockedBySystem)
            {
                opened.Add(collection.Usage == 0x06
                    ? $"{collection.Label}: teclado, travado pelo Windows (aparece nas linhas TECLA)"
                    : $"{collection.Label}: mouse, travado pelo Windows (ignorado)");
                continue;
            }
            try
            {
                // Só leitura: o monitor nunca escreve no teclado
                var handle = CreateFile(collection.Path, GenericRead, FileShareReadWrite,
                    IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = new Win32Exception();
                    handle.Dispose();
                    throw error;
                }
                var stream = new FileStream(handle, FileAccess.Read, bufferSize: 0, isAsync: true);
                _streams.Add(stream);
                opened.Add($"{collection.Label} (in={collection.InputReportLength})");
                _ = ListenAsync(stream, collection.InputReportLength, collection.Label, _cts.Token);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            {
                opened.Add($"{collection.Label}: sem acesso ({ex.Message})");
            }
        }
        return opened;
    }

    private async Task ListenAsync(FileStream stream, int length, string label, CancellationToken token)
    {
        var buffer = new byte[length];
        while (!token.IsCancellationRequested)
        {
            try
            {
                var read = await stream.ReadAsync(buffer, token);
                if (read > 0)
                {
                    ReportReceived?.Invoke(
                        $"{DateTime.Now:HH:mm:ss.fff} HID   {label}  {HexText.Spaced(buffer.AsSpan(0, read))}");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                if (!token.IsCancellationRequested)
                {
                    ReportReceived?.Invoke($"{label}: leitura encerrada ({ex.Message})");
                }
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var stream in _streams)
        {
            stream.Dispose();
        }
        _cts.Dispose();
    }
}
