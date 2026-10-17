using AkiraCore.Core.Links;
using static AkiraCore.App.Hid.NativeMethods;

namespace AkiraCore.App.Hid;

/// <summary>
/// Escuta input reports das coleções de fabricante (usage page 0xFF00 em diante).
/// O app oficial faz algo parecido na coleção 0xFFFF para receber avisos do
/// firmware ("05 A6 ..."), então atalhos do teclado podem aparecer aqui.
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
            if (collection.UsagePage < 0xFF00 || collection.InputReportLength == 0)
            {
                continue;
            }
            try
            {
                var handle = HidHandle.Open(collection.Path, FileFlagOverlapped);
                var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
                _streams.Add(stream);
                var label = $"page 0x{collection.UsagePage:X4}";
                opened.Add($"{label} (in={collection.InputReportLength})");
                _ = ListenAsync(stream, collection.InputReportLength, label, _cts.Token);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                opened.Add($"page 0x{collection.UsagePage:X4}: sem acesso");
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
                        $"{DateTime.Now:HH:mm:ss.fff} {label}  {Convert.ToHexString(buffer, 0, read)}");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                ReportReceived?.Invoke($"{label}: dispositivo desconectado");
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
