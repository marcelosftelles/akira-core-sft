using AkiraCore.Core.Links;

namespace AkiraCore.Core.Diagnostics;

/// <summary>Uma resposta do teclado e o nome do comando que a provocou.</summary>
public sealed record Exchange(string Label, byte[] Response);

/// <summary>
/// Guarda as respostas completas do teclado durante um envio. O link normal só
/// confere o byte do ACK (report[4]); aqui o Laboratório vê o report inteiro,
/// que pode carregar estado do firmware. No AULA F108 Pro, mesma plataforma e
/// mesmo dongle 05AC:024F, a resposta do 04 02 traz 2 bytes além do ACK.
/// Funciona como um interceptador (cy.intercept): não muda o tráfego, só registra.
/// </summary>
public sealed class ExchangeLog
{
    private readonly List<Exchange> _exchanges = [];

    public IReadOnlyList<Exchange> Exchanges => _exchanges;

    public IFeaturePort Wrap(IFeaturePort inner) => new TracingFeaturePort(inner, this);

    public IReportPort Wrap(IReportPort inner) => new TracingReportPort(inner, this);

    /// <summary>
    /// Respostas com nomes estáveis ("USB resposta 04 18 #1"), na ordem de chegada.
    /// O mesmo envio gera os mesmos nomes, então duas fotos são comparáveis.
    /// </summary>
    public StateSnapshot ToSnapshot()
    {
        var snapshot = new StateSnapshot();
        var counts = new Dictionary<string, int>();
        foreach (var exchange in _exchanges)
        {
            var n = counts[exchange.Label] = counts.GetValueOrDefault(exchange.Label) + 1;
            snapshot.Add($"{exchange.Label} #{n}", exchange.Response);
        }
        return snapshot;
    }

    internal void Add(string label, byte[] response) =>
        _exchanges.Add(new Exchange(label, (byte[])response.Clone()));
}

/// <summary>Decorador do canal com fio: repassa tudo e guarda cada GET_REPORT.</summary>
internal sealed class TracingFeaturePort(IFeaturePort inner, ExchangeLog log) : IFeaturePort
{
    private byte[]? _lastSent;

    public bool SetFeature(byte[] report)
    {
        _lastSent = report;
        return inner.SetFeature(report);
    }

    public byte[]? GetFeature(int length)
    {
        var response = inner.GetFeature(length);
        if (response is not null)
        {
            // report[0] é o Report ID; o comando fica em [1] e [2] (ex.: 04 18)
            var label = _lastSent is { Length: > 2 } sent
                ? $"USB resposta {sent[1]:X2} {sent[2]:X2}"
                : "USB GET_REPORT";
            log.Add(label, response);
        }
        return response;
    }

    public void Dispose() => inner.Dispose();
}

/// <summary>Decorador do canal 2.4G: guarda todo input report lido, eco ou não.</summary>
internal sealed class TracingReportPort(IReportPort inner, ExchangeLog log) : IReportPort
{
    private byte[]? _lastSent;

    public bool Write(byte[] report)
    {
        _lastSent = report;
        return inner.Write(report);
    }

    public byte[]? Read(int length, TimeSpan timeout)
    {
        var response = inner.Read(length, timeout);
        if (response is not null)
        {
            var label = _lastSent is { Length: > 3 } sent
                ? $"2.4G entrada após {sent[1]:X2} {sent[2]:X2} {sent[3]:X2}"
                : "2.4G entrada";
            log.Add(label, response);
        }
        return response;
    }

    public void Dispose() => inner.Dispose();
}
