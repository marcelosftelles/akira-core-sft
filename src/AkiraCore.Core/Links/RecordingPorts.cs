namespace AkiraCore.Core.Links;

/// <summary>
/// Dublês que registram cada report e respondem como o teclado responderia.
/// Usados no modo Simular e nos testes.
/// </summary>
public sealed class RecordingFeaturePort : IFeaturePort
{
    public List<byte[]> Written { get; } = [];
    public event Action<string>? Logged;

    public bool SetFeature(byte[] report)
    {
        Written.Add(report);
        Logged?.Invoke($"SET {Convert.ToHexString(report, 0, 17)}...");
        return true;
    }

    public byte[]? GetFeature(int length)
    {
        var ack = new byte[length];
        ack[4] = 1;
        return ack;
    }

    public void Dispose() { }
}

public sealed class RecordingReportPort : IReportPort
{
    public List<byte[]> Written { get; } = [];
    public event Action<string>? Logged;

    public bool Write(byte[] report)
    {
        Written.Add(report);
        Logged?.Invoke($"OUT {Convert.ToHexString(report)}");
        return true;
    }

    public byte[]? Read(int length, TimeSpan timeout) => Written.Count > 0 ? Written[^1] : null;

    public void Dispose() { }
}
