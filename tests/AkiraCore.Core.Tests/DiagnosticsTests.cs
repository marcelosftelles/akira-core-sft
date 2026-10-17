using AkiraCore.Core.Diagnostics;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;

namespace AkiraCore.Core.Tests;

public class StateDiffTests
{
    [Fact]
    public void IdenticalSnapshotsHaveNoDifference()
    {
        var before = new StateSnapshot().Add("USB resposta 04 02 #1", [0x00, 0x04, 0x02, 0x00, 0x01]);
        var after = new StateSnapshot().Add("USB resposta 04 02 #1", [0x00, 0x04, 0x02, 0x00, 0x01]);

        var diff = StateDiff.Compare(before, after);

        Assert.True(diff.IsEmpty);
        Assert.Single(StateDiff.Describe(diff));
    }

    [Fact]
    public void ReportsEachChangedByteWithOffset()
    {
        var before = new StateSnapshot().Add("A", [0x04, 0x02, 0x00, 0x01, 0x07]);
        var after = new StateSnapshot().Add("A", [0x04, 0x02, 0x00, 0x01, 0x08]);

        var diff = StateDiff.Compare(before, after);

        var change = Assert.Single(diff.Changes);
        Assert.Equal(new ByteChange("A", 4, 0x07, 0x08), change);
        Assert.Equal("A: [4] 07 → 08", Assert.Single(StateDiff.Describe(diff)));
    }

    [Fact]
    public void LengthChangeShowsMissingBytesAsNull()
    {
        var before = new StateSnapshot().Add("A", [0x01]);
        var after = new StateSnapshot().Add("A", [0x01, 0x05]);

        var change = Assert.Single(StateDiff.Compare(before, after).Changes);

        Assert.Equal(new ByteChange("A", 1, null, 0x05), change);
    }

    [Fact]
    public void SourcesPresentOnOneSideAreListed()
    {
        var before = new StateSnapshot().Add("só antes", [0x01]).Add("comum", [0x02]);
        var after = new StateSnapshot().Add("comum", [0x02]).Add("só depois", [0x03]);

        var diff = StateDiff.Compare(before, after);

        Assert.Equal(["só antes"], diff.OnlyBefore);
        Assert.Equal(["só depois"], diff.OnlyAfter);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void SnapshotRejectsDuplicateSourceAndCopiesData()
    {
        var data = new byte[] { 0x01 };
        var snapshot = new StateSnapshot().Add("A", data);
        data[0] = 0xFF;

        Assert.Equal(0x01, snapshot.Find("A")![0]);
        Assert.Throws<ArgumentException>(() => snapshot.Add("A", [0x02]));
    }
}

public class ExchangeLogTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private static byte[] StaticRedBlock() =>
        new LightingConfig(EffectCatalog.ByKey("static"), Rgb.FromHex("ff0000")).ToLightBlock();

    [Fact]
    public void RecordsOneResponsePerAckedFeatureCommand()
    {
        // Transação oficial (fcn.0044ed60): só 18, 13 e 02 pedem ACK; dados e F0 não
        var log = new ExchangeLog();
        var port = new RecordingFeaturePort();

        new FeatureLink(log.Wrap(port), new NoDelay()).SendLightBlock(StaticRedBlock());

        Assert.Equal(
            ["USB resposta 04 18 #1", "USB resposta 04 13 #1", "USB resposta 04 02 #1"],
            log.ToSnapshot().Sources.Select(s => s.Key));
        Assert.Equal(5, port.Written.Count); // o decorador não muda o tráfego
    }

    [Fact]
    public void KeepsFullResponseNotOnlyAckByte()
    {
        var log = new ExchangeLog();

        new FeatureLink(log.Wrap(new RecordingFeaturePort()), new NoDelay()).SendLightBlock(StaticRedBlock());

        var response = log.ToSnapshot().Find("USB resposta 04 18 #1")!;
        Assert.Equal(65, response.Length);
        Assert.Equal(1, response[4]);
    }

    [Fact]
    public void RepeatedCommandsGetNumberedNames()
    {
        var log = new ExchangeLog();
        var link = new FeatureLink(log.Wrap(new RecordingFeaturePort()), new NoDelay());

        link.SendLightBlock(StaticRedBlock());
        link.SendLightBlock(StaticRedBlock());

        Assert.NotNull(log.ToSnapshot().Find("USB resposta 04 18 #2"));
    }

    [Fact]
    public void RecordsDongleInputReports()
    {
        var log = new ExchangeLog();

        new DongleLink(log.Wrap(new RecordingReportPort()), new NoDelay()).SendLightBlock(StaticRedBlock());

        var (source, data) = Assert.Single(log.ToSnapshot().Sources);
        Assert.Equal("2.4G entrada após 05 10 00 #1", source);
        Assert.Equal(33, data.Length);
    }
}

public class HidPathInfoTests
{
    [Theory]
    [InlineData(@"\\?\hid#vid_0c45&pid_8006&mi_02#8&2b3c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", "MI_02")]
    [InlineData(@"\\?\HID#VID_0C45&PID_8006&MI_01&Col03#8&1a&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", "MI_01 COL03")]
    [InlineData(@"\\?\hid#vid_05ac&pid_024f#7&aa&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", "MI_?")]
    public void ExtractsInterfaceAndCollection(string path, string expected) =>
        Assert.Equal(expected, HidPathInfo.InterfaceLabel(path));
}

public class HexTextTests
{
    [Fact]
    public void CompactsLongRunOfTrailingZeros() =>
        Assert.Equal("05 A6 01 … (+5 × 00)", HexText.Spaced([0x05, 0xA6, 0x01, 0, 0, 0, 0, 0]));

    [Fact]
    public void KeepsShortRunOfTrailingZeros() =>
        Assert.Equal("04 18 00 01 00", HexText.Spaced([0x04, 0x18, 0x00, 0x01, 0x00]));

    [Fact]
    public void AllZeroReportIsOnlyTheSummary() =>
        Assert.Equal("(+33 × 00)", HexText.Spaced(new byte[33]));

    [Fact]
    public void CanShowEveryByte() =>
        Assert.Equal("01 00 00 00 00", HexText.Spaced([0x01, 0, 0, 0, 0], compactTrailingZeros: false));
}
