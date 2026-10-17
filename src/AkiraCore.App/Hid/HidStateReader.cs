using System.ComponentModel;
using AkiraCore.Core.Diagnostics;

namespace AkiraCore.App.Hid;

/// <summary>
/// Lê o feature report atual de cada coleção do teclado que tem feature, sem
/// enviar nada antes (GET_REPORT puro). É só leitura: o firmware devolve o
/// buffer de resposta que já tem pronto.
/// </summary>
public static class HidStateReader
{
    public static StateSnapshot ReadFeatureReports(Action<string> log)
    {
        var snapshot = new StateSnapshot();
        foreach (var collection in HidDeviceFinder.ListCollections().Where(c => c.FeatureReportLength > 0))
        {
            var source = UniqueName(snapshot, $"{collection.Label} GET_REPORT");
            try
            {
                using var port = new HidFeaturePort(collection.Path);
                var data = port.GetFeature(collection.FeatureReportLength);
                if (data is null)
                {
                    log($"{source}: o teclado recusou a leitura");
                    continue;
                }
                snapshot.Add(source, data);
            }
            catch (Win32Exception ex)
            {
                log($"{source}: sem acesso ({ex.Message})");
            }
        }
        return snapshot;
    }

    private static string UniqueName(StateSnapshot snapshot, string name)
    {
        var candidate = name;
        for (var n = 2; snapshot.Find(candidate) is not null; n++)
        {
            candidate = $"{name} ({n})";
        }
        return candidate;
    }
}
