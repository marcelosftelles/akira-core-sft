namespace AkiraCore.Core.Links;

/// <summary>E/S crua de feature reports. Implementada no App com hid.dll.</summary>
public interface IFeaturePort : IDisposable
{
    bool SetFeature(byte[] report);

    /// <summary>Retorna o report lido (com Report ID no índice 0) ou null em falha.</summary>
    byte[]? GetFeature(int length);
}

/// <summary>E/S crua de output e input reports (canal do dongle).</summary>
public interface IReportPort : IDisposable
{
    bool Write(byte[] report);

    /// <summary>Retorna o report lido (com Report ID no índice 0) ou null se o tempo esgotar.</summary>
    byte[]? Read(int length, TimeSpan timeout);
}

/// <summary>Pausas entre comandos. Injetável para os testes rodarem sem esperar.</summary>
public interface IDelay
{
    void Wait(TimeSpan duration);
}

public sealed class ThreadDelay : IDelay
{
    public static ThreadDelay Instance { get; } = new();
    public void Wait(TimeSpan duration) => Thread.Sleep(duration);
}
