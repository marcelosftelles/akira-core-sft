using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

/// <summary>Leitura das cores dos LEDs que a prévia do oficial usa no cabo (fcn.0042adb0 e fcn.0044f230).</summary>
public class LedReadTests
{
    private sealed class RecordingDelay : IDelay
    {
        public List<int> Waits { get; } = [];
        public void Wait(TimeSpan duration) => Waits.Add((int)duration.TotalMilliseconds);
    }

    /// <summary>Responde os GetFeature na ordem dada; acabou a lista, devolve ACK.</summary>
    private sealed class ScriptedPort(params byte[]?[] responses) : IFeaturePort
    {
        private readonly Queue<byte[]?> _responses = new(responses);
        public List<byte[]> Written { get; } = [];
        public int Reads { get; private set; }

        public bool SetFeature(byte[] report)
        {
            Written.Add(report);
            return true;
        }

        public byte[]? GetFeature(int length)
        {
            Reads++;
            return _responses.Count > 0 ? _responses.Dequeue() : Ack();
        }

        public void Dispose() { }
    }

    private static byte[] Ack()
    {
        var report = new byte[FeatureProtocol.ReportLength];
        report[FeatureProtocol.AckIndex] = 1;
        return report;
    }

    /// <summary>Pacote k do buffer: Report ID 0 e 64 bytes que valem k + 1.</summary>
    private static byte[] Chunk(int k)
    {
        var report = new byte[FeatureProtocol.ReportLength];
        Array.Fill(report, (byte)(k + 1), 1, FeatureProtocol.PayloadLength);
        return report;
    }

    private static byte[]?[] AckThenChunks(int count) => [Ack(), .. Enumerable.Range(0, count).Select(Chunk)];

    [Fact]
    public void AsksF5WithNinePacketsAndClosesWith02()
    {
        var port = new ScriptedPort(AckThenChunks(9));

        new FeatureLink(port, new RecordingDelay()).ReadLedColors();

        var headers = port.Written.Select(r => Convert.ToHexString(r, 0, 3)).ToArray();
        Assert.Equal(["0004F5", "000402"], headers); // sem 04 18 e sem 04 F0
        Assert.Equal(9, port.Written[0][9]);           // [8] do payload: rgb_keyboard="1"
    }

    [Fact]
    public void CopiesTheNinePacketsInOrder()
    {
        var buffer = new FeatureLink(new ScriptedPort(AckThenChunks(9)), new RecordingDelay()).ReadLedColors();

        Assert.NotNull(buffer);
        Assert.Equal(PerKeyProtocol.BufferLength, buffer.Length);
        for (var k = 0; k < 9; k++)
        {
            Assert.All(buffer.AsSpan(k * 64, 64).ToArray(), b => Assert.Equal(k + 1, b));
        }
    }

    [Fact]
    public void WaitsLikeTheOfficial()
    {
        var delay = new RecordingDelay();

        new FeatureLink(new ScriptedPort(AckThenChunks(9)), delay).ReadLedColors();

        // 3 ms, pedido, 5 ms, ACK + 5 ms, 9 × (3 ms + pacote), 04 02 com 35 ms antes e 35 antes do ACK
        Assert.Equal([3, 5, 5, 3, 3, 3, 3, 3, 3, 3, 3, 3, 35, 35], delay.Waits);
    }

    [Fact]
    public void TriesTenTimesForTheAck()
    {
        var port = new ScriptedPort([.. Enumerable.Repeat<byte[]?>(null, 9), .. AckThenChunks(9)]);

        Assert.NotNull(new FeatureLink(port, new RecordingDelay()).ReadLedColors());
    }

    [Fact]
    public void GivesUpWithoutAckAndSendsNothingElse()
    {
        var port = new ScriptedPort([.. Enumerable.Repeat<byte[]?>(null, 10)]);

        Assert.Null(new FeatureLink(port, new RecordingDelay()).ReadLedColors());
        Assert.Single(port.Written);
        Assert.Equal(10, port.Reads);
    }

    [Fact]
    public void RefusedRequestIsNotAnAck()
    {
        var port = new ScriptedPort(new byte[FeatureProtocol.ReportLength]);

        Assert.Null(new FeatureLink(port, new RecordingDelay()).ReadLedColors());
        Assert.Single(port.Written);
    }

    [Fact]
    public void KeepsWhatArrivedWhenAPacketFails()
    {
        var port = new ScriptedPort([Ack(), Chunk(0), Chunk(1), null]);

        var buffer = new FeatureLink(port, new RecordingDelay()).ReadLedColors();

        Assert.NotNull(buffer);
        Assert.Equal(2, buffer[64]);
        Assert.All(buffer.AsSpan(128).ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void NoPacketMeansNoFrame()
    {
        var port = new ScriptedPort([Ack(), null]);

        Assert.Null(new FeatureLink(port, new RecordingDelay()).ReadLedColors());
        Assert.Single(port.Written); // sem 04 02 quando nada foi lido
    }

    // fcn.0042adb0: cor do LED i = buffer[i*4 + 1], [i*4 + 2], [i*4 + 3]; o byte i*4 é ignorado
    [Fact]
    public void ColorsComeFromTheSameLayoutAsTheWrite()
    {
        var buffer = new PerKeyFrame().Set(1, new Rgb(0x10, 0x20, 0x30)).Set(142, new Rgb(1, 2, 3)).ToBuffer();
        buffer[39 * 4 + 1] = 0xAA; // sem o byte de índice, como o firmware pode devolver

        var colors = PerKeyFrame.ReadColors(buffer);

        Assert.Equal(143, colors.Length);
        Assert.Equal(new Rgb(0x10, 0x20, 0x30), colors[1]);
        Assert.Equal(new Rgb(1, 2, 3), colors[142]);
        Assert.Equal(new Rgb(0xAA, 0, 0), colors[39]);
        Assert.Equal(default, colors[0]);
    }

    [Fact]
    public void ColorFixAppliesToTheReadToo() =>
        Assert.Equal(new Rgb(0x30, 0x20, 0x10),
            PerKeyFrame.ReadColors(new PerKeyFrame().Set(5, new Rgb(0x10, 0x20, 0x30)).ToBuffer(), swapRedBlue: true)[5]);

    [Fact]
    public void ClientReadsThroughTheCableOnly()
    {
        var cable = new KeyboardClient(new FeatureLink(new ScriptedPort(AckThenChunks(9)), new RecordingDelay()));
        var dongle = new KeyboardClient(new DongleLink(new RecordingReportPort()));

        Assert.Equal(143, cable.ReadLedColors()!.Length);
        Assert.Null(dongle.ReadLedColors());
    }
}
