using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Formato vindo de fcn.0044f480 (envio) e fcn.0042b170 (iluminação no 2.4G).
public class DongleLinkTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private static byte[] StaticRedBlock() =>
        new LightingConfig(EffectCatalog.ByKey("static"), Rgb.FromHex("ff0000"), brightness: 5, speed: 3)
            .ToLightBlock();

    [Fact]
    public void Report_HasHeaderBlockAndChecksum()
    {
        var report = PacketBuilder.DongleLighting(StaticRedBlock());

        Assert.Equal(33, report.Length);
        Assert.Equal([0x00, 0x05, 0x10, 0x00], report[..4]);
        Assert.Equal(StaticRedBlock(), report[4..20]);
        // 05+10+01+FF+05+03+AA+55 = 0x21C, fica 0x1C
        Assert.Equal(0x1C, report[32]);
    }

    [Fact]
    public void SucceedsWhenEchoArrivesAfterUnrelatedReports()
    {
        var port = new ScriptedPort(writeResults: [true], readResults: [null, Noise(), EchoOf(StaticRedBlock())]);

        new DongleLink(port, new NoDelay()).SendLightBlock(StaticRedBlock());

        Assert.Equal(3, port.Reads);
    }

    [Fact]
    public void RetriesWriteOnce()
    {
        var port = new ScriptedPort(writeResults: [false, true], readResults: [EchoOf(StaticRedBlock())]);

        new DongleLink(port, new NoDelay()).SendLightBlock(StaticRedBlock());

        Assert.Equal(2, port.Writes);
    }

    [Fact]
    public void GivesUpAfterTwentyReadsWithoutEcho()
    {
        var port = new ScriptedPort(writeResults: [true], readResults: []);

        Assert.Throws<KeyboardException>(() =>
            new DongleLink(port, new NoDelay()).SendLightBlock(StaticRedBlock()));
        Assert.Equal(20, port.Reads);
    }

    private static byte[] Noise() => [0x00, 0x09, 0x01, 0x00];

    private static byte[] EchoOf(byte[] block) => PacketBuilder.DongleLighting(block);

    private sealed class ScriptedPort(bool[] writeResults, byte[]?[] readResults) : IReportPort
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }

        public bool Write(byte[] report) => writeResults[Math.Min(Writes++, writeResults.Length - 1)];

        public byte[]? Read(int length, TimeSpan timeout)
        {
            var index = Reads++;
            return index < readResults.Length ? readResults[index] : null;
        }

        public void Dispose() { }
    }
}
