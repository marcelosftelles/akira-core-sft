using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Leitura da bateria em fcn.004357d0 (DeviceDriver.exe), transação de fcn.0044f480 e leitura de
// fcn.00451190, que tira o Report ID 0: lá o nível é [3]; aqui, com o Report ID, é [4].
public class BatteryTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    /// <summary>Responde a qualquer escrita com o eco 20 01 00 e o nível pedido.</summary>
    private sealed class BatteryPort(byte level) : IReportPort
    {
        public byte[]? Sent { get; private set; }

        public bool Write(byte[] report)
        {
            Sent = report;
            return true;
        }

        public byte[]? Read(int length, TimeSpan timeout) => Response(level);

        public void Dispose() { }
    }

    private static byte[] Response(byte level)
    {
        var response = new byte[DongleProtocol.ReportLength];
        response[1] = 0x20;
        response[2] = 0x01;
        response[4] = level;
        return response;
    }

    private sealed class SilentFeaturePort : IFeaturePort
    {
        public int Calls { get; private set; }

        public bool SetFeature(byte[] report)
        {
            Calls++;
            return true;
        }

        public byte[]? GetFeature(int length)
        {
            Calls++;
            return null;
        }

        public void Dispose() { }
    }

    [Fact]
    public void Query_IsCommand2001WithChecksum()
    {
        var report = PacketBuilder.DongleBatteryQuery();

        Assert.Equal(33, report.Length);
        Assert.Equal([0x00, 0x20, 0x01, 0x00], report[..4]);
        Assert.All(report[4..32], b => Assert.Equal(0, b));
        Assert.Equal(0x21, report[32]);
    }

    [Fact]
    public void ReadsLevelFromByteAfterEcho()
    {
        var port = new BatteryPort(level: 75);

        var level = new KeyboardClient(new DongleLink(port, new NoDelay())).ReadBatteryLevel();

        Assert.Equal(75, level);
        Assert.Equal(PacketBuilder.DongleBatteryQuery(), port.Sent);
    }

    [Theory]
    [InlineData(0, null)]     // o oficial ignora nível 0
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]    // acima de 100 vira 100
    [InlineData(255, 100)]
    public void LevelFollowsOfficialRules(byte raw, int? expected)
    {
        Assert.Equal(expected, PacketBuilder.DongleBatteryLevel(Response(raw)));
    }

    [Fact]
    public void ResponseOfAnotherCommandHasNoLevel()
    {
        var other = Response(80);
        other[1] = 0x05;

        Assert.Null(PacketBuilder.DongleBatteryLevel(other));
        Assert.Null(PacketBuilder.DongleBatteryLevel([0x00, 0x20, 0x01]));
    }

    [Fact]
    public void CableDoesNotAskForBattery()
    {
        var port = new SilentFeaturePort();

        Assert.Null(new KeyboardClient(new FeatureLink(port, new NoDelay())).ReadBatteryLevel());
        Assert.Equal(0, port.Calls);
    }
}
