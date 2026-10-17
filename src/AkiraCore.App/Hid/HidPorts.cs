using System.ComponentModel;
using AkiraCore.Core.Links;
using Microsoft.Win32.SafeHandles;
using static AkiraCore.App.Hid.NativeMethods;

namespace AkiraCore.App.Hid;

internal static class HidHandle
{
    public static SafeFileHandle Open(string path, uint flags = 0)
    {
        var handle = CreateFile(path, GenericRead | GenericWrite, FileShareReadWrite,
            IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            // Plano B: feature reports costumam funcionar mesmo sem acesso de leitura/escrita
            handle.Dispose();
            handle = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        }
        return handle.IsInvalid ? throw new Win32Exception() : handle;
    }
}

/// <summary>Feature reports via hid.dll (canal com fio).</summary>
public sealed class HidFeaturePort(string path) : IFeaturePort
{
    private readonly SafeFileHandle _handle = HidHandle.Open(path);

    public bool SetFeature(byte[] report) => HidD_SetFeature(_handle, report, report.Length);

    public byte[]? GetFeature(int length)
    {
        var buffer = new byte[length]; // buffer[0] = Report ID 0
        return HidD_GetFeature(_handle, buffer, buffer.Length) ? buffer : null;
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Output e input reports via WriteFile/ReadFile assíncronos (canal do dongle).
/// O handle é aberto em modo overlapped para a leitura aceitar timeout.
/// </summary>
public sealed class HidReportPort : IReportPort
{
    private readonly FileStream _stream;

    public HidReportPort(string path)
    {
        var handle = HidHandle.Open(path, FileFlagOverlapped);
        _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
    }

    public bool Write(byte[] report)
    {
        try
        {
            _stream.Write(report, 0, report.Length);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public byte[]? Read(int length, TimeSpan timeout)
    {
        var buffer = new byte[length];
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var read = _stream.ReadAsync(buffer, cts.Token).AsTask().GetAwaiter().GetResult();
            return read > 0 ? buffer : null;
        }
        catch (OperationCanceledException)
        {
            return null; // nada chegou dentro do prazo
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
