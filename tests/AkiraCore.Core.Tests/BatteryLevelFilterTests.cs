using AkiraCore.Core.Device;

namespace AkiraCore.Core.Tests;

public class BatteryLevelFilterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int poll) => Start + TimeSpan.FromSeconds(15 * poll);

    // Uma leitura a cada 15 s a partir de "from"; devolve o nível mostrado depois de cada uma
    private static int?[] Feed(BatteryLevelFilter filter, bool charging, DateTimeOffset from, params int[] readings) =>
        readings.Select((r, i) => filter.Update(r, charging, from + TimeSpan.FromSeconds(15 * i))).ToArray();

    // Nível estável já na tela: três leituras iguais
    private static BatteryLevelFilter Settled(int level, bool charging = false)
    {
        var filter = new BatteryLevelFilter();
        Feed(filter, charging, Start, level, level, level);
        return filter;
    }

    [Fact]
    public void FirstReadingShowsAsIs()
    {
        var filter = new BatteryLevelFilter();

        Assert.Null(filter.Level);
        Assert.Equal(91, filter.Update(91, charging: false, At(0)));
    }

    [Fact]
    public void StartFollowsTheMedianUntilThreeReadings()
    {
        // Caso relatado: 91 e, 15 s depois, 87
        var filter = new BatteryLevelFilter();

        Assert.Equal([91, 89, 91, 91], Feed(filter, false, Start, 91, 87, 91, 90));
    }

    [Fact]
    public void NoisyReadingsNeverGoUpWithoutCable()
    {
        var filter = new BatteryLevelFilter();

        var shown = Feed(filter, false, Start, 91, 87, 90, 88, 91, 86, 90, 89, 91, 87)
            .Select(l => l!.Value).Skip(BatteryLevelFilter.MinSamples - 1).ToArray();

        Assert.Equal(shown.OrderByDescending(l => l), shown);
        Assert.Equal(88, shown[^1]);
    }

    [Fact]
    public void SustainedDropShowsUp()
    {
        var filter = new BatteryLevelFilter();

        Assert.Equal([80, 80, 80, 78, 75], Feed(filter, false, Start, 80, 80, 75, 75, 75));
    }

    [Fact]
    public void OneLowReadingAfterSleepIsIgnored()
    {
        // Teclado dormiu 3 minutos: as leituras antigas saíram da janela e ela recomeça
        var filter = Settled(91);

        Assert.Equal([91, 91, 90], Feed(filter, false, At(2) + TimeSpan.FromMinutes(3), 87, 91, 90));
    }

    [Fact]
    public void RestingRiseAfterSleepIsIgnored()
    {
        // Em repouso a tensão sobe: a primeira leitura depois de acordar vem alta
        var filter = Settled(60);

        Assert.All(Feed(filter, false, At(2) + TimeSpan.FromMinutes(30), 66, 61, 60, 60, 60),
            level => Assert.Equal(60, level));
    }

    [Fact]
    public void ChargingOutsideThePcRisesWithFullWindow()
    {
        var filter = Settled(50);

        // Janela [50 54 56 58 60]: mediana 56, cheia e 5 acima de 50
        Assert.Equal([50, 50, 50, 56], Feed(filter, false, At(3), 54, 56, 58, 60));
    }

    [Fact]
    public void WhileChargingOnlyRises()
    {
        var filter = Settled(60, charging: true);

        Assert.Equal([60, 60, 64, 64], Feed(filter, true, At(3), 64, 64, 64, 58));
    }

    [Fact]
    public void UnplugWaitsForThreeReadings()
    {
        var filter = Settled(98, charging: true);

        // Sem o cabo a tensão cai de patamar: a janela recomeça e decide com três leituras
        Assert.Equal([98, 98, 95], Feed(filter, false, At(3), 95, 95, 95));
    }

    [Fact]
    public void MissingReadingKeepsTheLevel()
    {
        var filter = Settled(70);

        Assert.Equal(70, filter.Update(null, false, At(3)));
        Assert.Equal(70, filter.Update(null, false, At(3) + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var filter = Settled(40);
        filter.Reset();

        Assert.Null(filter.Level);
        Assert.Equal(90, filter.Update(90, false, At(5)));
    }
}
