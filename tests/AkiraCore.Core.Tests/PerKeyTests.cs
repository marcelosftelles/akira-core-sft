using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Layout de fcn.0044b8a0 (cabo) e fcn.0044bd20 (dongle).
public class PerKeyTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    [Fact]
    public void Buffer_PutsIndexAndRgbAtIndexTimesFour()
    {
        var buffer = new PerKeyFrame().Set(5, new Rgb(0x11, 0x22, 0x33)).ToBuffer();

        Assert.Equal(576, buffer.Length);
        Assert.Equal([0x05, 0x11, 0x22, 0x33], buffer[20..24]);
        Assert.Equal([0xAA, 0x55], buffer[574..576]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(143)]
    public void Frame_RejectsIndicesOutsideSafeRange(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerKeyFrame().Set(index, new Rgb()));
    }

    [Fact]
    public void Cable_SendsCustomModeThenNineChunks()
    {
        var port = new RecordingFeaturePort();
        var frame = new PerKeyFrame().Set(14, new Rgb(255, 0, 0));

        new KeyboardClient(new FeatureLink(port, new NoDelay())).ApplyPerKeyColors(frame, brightness: 3);

        // 5 reports do modo personalizado + 2 de abertura + 9 de dados + 2 de fechamento
        Assert.Equal(18, port.Written.Count);
        Assert.Equal(0x80, port.Written[2][1]);           // bloco do modo: byte 0 = 0x80
        Assert.Equal(3, port.Written[2][10]);             // brilho no byte 9 do bloco
        Assert.Equal("000423", Convert.ToHexString(port.Written[6], 0, 3));
        Assert.Equal(9, port.Written[6][9]);              // anuncia 9 pacotes
        // LED 14 fica no offset 56: primeiro chunk, byte 56 do payload
        Assert.Equal([14, 255, 0, 0], port.Written[7][57..61]);
        Assert.Equal("000402", Convert.ToHexString(port.Written[16], 0, 3));
    }

    [Fact]
    public void Dongle_SendsCustomModeThenTwentyOneChunks()
    {
        var port = new RecordingReportPort();
        var frame = new PerKeyFrame().Set(14, new Rgb(255, 0, 0));

        new KeyboardClient(new DongleLink(port, new NoDelay())).ApplyPerKeyColors(frame, brightness: 4);

        Assert.Equal(22, port.Written.Count);
        Assert.Equal([0x05, 0x10, 0x00, 0x80], port.Written[0][1..5]);
        Assert.Equal(4, port.Written[0][13]);

        var chunks = port.Written.Skip(1).ToList();
        Assert.All(chunks.Select((r, i) => (r, i)), c =>
        {
            Assert.Equal(0x14, c.r[1]);
            Assert.Equal(c.i == 20 ? 0x10 : 0x1C, c.r[2]);
            Assert.Equal(c.i, c.r[3]);
        });
        // offset 56 do buffer = chunk 2 (56 / 28), início dos dados
        Assert.Equal([14, 255, 0, 0], chunks[2][4..8]);
        // assinatura AA 55 no offset 574 = chunk 20, posição 14 dos dados
        Assert.Equal([0xAA, 0x55], chunks[20][18..20]);
    }

    [Fact]
    public void UnmappedIndices_AreInsideSafeRangeAndUnique()
    {
        Assert.All(LedMap.UnmappedIndices, i => Assert.InRange(i, 1, PerKeyProtocol.MaxLedIndex));
        Assert.Equal(LedMap.UnmappedIndices.Count, LedMap.UnmappedIndices.Distinct().Count());
    }
}
