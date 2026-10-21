using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Fase 2: 04 20 (fcn.00427f60 / fcn.004328c0 → fcn.0044ef50), 04 F5 (fcn.0042adb0),
// 0B 1C 00 (fcn.00432b00) e 1F (fcn.00414600)
public class StreamProtocolTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private static string Header(byte[] report) => Convert.ToHexString(report, 0, 3);

    [Fact]
    public void StreamFrame_UsesTheFastSequenceWithoutBeginOrFinalize()
    {
        var port = new RecordingFeaturePort();

        new FeatureLink(port, new NoDelay()).SendStreamFrame(new byte[StreamProtocol.FrameLength]);

        // 04 20 com [8] = 8, oito pacotes de dados e 04 02: nada de 04 18 nem 04 F0 (não grava)
        Assert.Equal(10, port.Written.Count);
        Assert.Equal("000420", Header(port.Written[0]));
        Assert.Equal(8, port.Written[0][9]);
        Assert.Equal("000402", Header(port.Written[^1]));
        Assert.DoesNotContain(port.Written, r => r[1] == 0x04 && r[2] is 0x18 or 0xF0);
    }

    [Fact]
    public void StreamFrame_SendsTheFrameInOrder()
    {
        var port = new RecordingFeaturePort();
        var frame = new byte[StreamProtocol.FrameLength];
        frame[0] = 0x01;
        frame[1] = 0xAA;
        frame[64] = 0x13; // começo do segundo pacote
        frame[^1] = 0x77;

        new FeatureLink(port, new NoDelay()).SendStreamFrame(frame);

        Assert.Equal([0x00, 0x01, 0xAA], port.Written[1][..3]);
        Assert.Equal(0x13, port.Written[2][1]);
        Assert.Equal(0x77, port.Written[8][64]);
    }

    [Fact]
    public void StreamFrame_RejectsOtherSizes()
    {
        var link = new FeatureLink(new RecordingFeaturePort(), new NoDelay());

        Assert.Throws<ArgumentException>(() => link.SendStreamFrame(new byte[576]));
    }

    [Fact]
    public void StreamFrame_ThrowsWithoutAck()
    {
        var link = new FeatureLink(new ScriptedPort(_ => new byte[65]), new NoDelay());

        Assert.Throws<KeyboardException>(() => link.SendStreamFrame(new byte[StreamProtocol.FrameLength]));
    }

    [Fact]
    public void ReadLedColors_AnnouncesNinePacketsAndJoinsThem()
    {
        // Resposta: ACK, depois 9 pacotes com o número do pacote no primeiro byte de cada um
        var reads = 0;
        var port = new ScriptedPort(_ =>
        {
            var report = new byte[65];
            if (reads++ == 0)
            {
                report[4] = 1;
                return report;
            }
            report[1] = (byte)(reads - 1);
            return report;
        });

        var buffer = new FeatureLink(port, new NoDelay()).ReadLedColors();

        Assert.Equal(576, buffer.Length);
        Assert.Equal("0004F5", Header(port.Written[0]));
        Assert.Equal(9, port.Written[0][9]);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(9, buffer[8 * 64]);
        Assert.Equal("000402", Header(port.Written[^1]));
        Assert.DoesNotContain(port.Written, r => r[2] == 0x18); // o oficial não abre com 04 18
    }

    [Fact]
    public void ReadLedColors_WaitsUpToTenReadsForTheAck()
    {
        var reads = 0;
        var port = new ScriptedPort(_ =>
        {
            var report = new byte[65];
            report[4] = (byte)(++reads == 10 ? 1 : 0);
            return report;
        });

        new FeatureLink(port, new NoDelay()).ReadLedColors();

        Assert.True(reads >= 10 + 9);
    }

    [Fact]
    public void ReadLedColors_GivesUpAfterTenReads()
    {
        var link = new FeatureLink(new ScriptedPort(_ => new byte[65]), new NoDelay());

        Assert.Throws<KeyboardException>(() => link.ReadLedColors());
    }

    [Fact]
    public void ParseLedColors_ReadsTheSameTableAsTheCustomMode()
    {
        // Mesmo formato do 04 23: o buffer montado pelo PerKeyFrame volta igual
        var frame = new PerKeyFrame().Set(1, new Rgb(1, 2, 3)).Set(39, new Rgb(0xFF, 0x80, 0x00));

        var colors = PacketBuilder.ParseLedColors(frame.ToBuffer());

        Assert.Equal(2, colors.Count);
        Assert.Equal(new Rgb(0xFF, 0x80, 0x00), colors[39]);
    }

    [Fact]
    public void ParseLedColors_FallsBackToPositionWhenNoIndexIsPresent()
    {
        var buffer = new byte[576];
        buffer[39 * 4 + 1] = 0x10;

        var colors = PacketBuilder.ParseLedColors(buffer);

        Assert.Equal(new Rgb(0x10, 0, 0), colors[39]);
        Assert.Equal(PerKeyProtocol.MaxLedIndex, colors.Count);
    }

    [Fact]
    public void DongleMusic_PutsModesBrightnessAndHeightsAfterTheHeader()
    {
        var report = PacketBuilder.DongleMusic(2, 1, 5, [6, 3, 0, 1]);

        Assert.Equal(33, report.Length);
        Assert.Equal([0x00, 0x0B, 0x1C, 0x00], report[..4]);
        Assert.Equal([2, 1, 5], report[4..7]);
        Assert.Equal([6, 3, 0, 1], report[7..11]);
        Assert.All(report[11..32], b => Assert.Equal(0, b));
        Assert.Equal((byte)report[..32].Sum(b => b), report[32]);
    }

    [Fact]
    public void DongleMusic_RejectsMoreColumnsThanTheReportHolds()
    {
        Assert.Throws<ArgumentException>(() => PacketBuilder.DongleMusic(0, 0, 5, new byte[26]));
    }

    [Fact]
    public void DongleLink_SendsMusicAndKeyboardReset()
    {
        var port = new RecordingReportPort();
        var link = new DongleLink(port, new NoDelay());

        link.SendMusicReport(PacketBuilder.DongleMusic(0, 0, 5, [1]));
        link.SendKeyboardReset();

        Assert.Equal(0x0B, port.Written[0][1]);
        Assert.Equal([0x00, 0x1F, 0x00], port.Written[1][..3]);
        Assert.Equal(0x1F, port.Written[1][32]);
    }

    [Fact]
    public void Capabilities_MatchWhatTheOfficialDoesOnEachChannel()
    {
        var cable = new FeatureLink(new RecordingFeaturePort(), new NoDelay());
        var dongle = new DongleLink(new RecordingReportPort(), new NoDelay());

        Assert.True(cable.HasStreaming && cable.HasColorReadback);
        Assert.False(cable.HasMusicReport || cable.HasKeyboardReset);
        Assert.False(dongle.HasStreaming || dongle.HasColorReadback);
        Assert.True(dongle.HasMusicReport && dongle.HasKeyboardReset);
    }

    private sealed class ScriptedPort(Func<int, byte[]?> respond) : IFeaturePort
    {
        public List<byte[]> Written { get; } = [];

        public bool SetFeature(byte[] report)
        {
            Written.Add(report);
            return true;
        }

        public byte[]? GetFeature(int length) => respond(length);

        public void Dispose() { }
    }
}
