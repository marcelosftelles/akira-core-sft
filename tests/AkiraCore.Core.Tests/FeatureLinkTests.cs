using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;

namespace AkiraCore.Core.Tests;

public class FeatureLinkTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private static byte[] StaticRedBlock() =>
        new LightingConfig(EffectCatalog.ByKey("static"), Rgb.FromHex("ff0000")).ToLightBlock();

    [Fact]
    public void SendsOfficialTransactionSequence()
    {
        var port = new RecordingFeaturePort();

        new FeatureLink(port, new NoDelay()).SendLightBlock(StaticRedBlock());

        var headers = port.Written.Select(r => Convert.ToHexString(r, 0, 3)).ToArray();
        Assert.Equal(["000418", "000413", "0001FF", "000402", "0004F0"], headers);
        Assert.All(port.Written, r => Assert.Equal(65, r.Length));
    }

    [Fact]
    public void AnnouncesExactlyOneDataPacket()
    {
        var port = new RecordingFeaturePort();

        new FeatureLink(port, new NoDelay()).SendLightBlock(StaticRedBlock());

        Assert.Equal(1, port.Written[1][9]);
    }

    [Fact]
    public void ThrowsWhenKeyboardDoesNotAck()
    {
        var link = new FeatureLink(new NoAckPort(), new NoDelay());

        Assert.Throws<KeyboardException>(() => link.SendLightBlock(StaticRedBlock()));
    }

    private sealed class NoAckPort : IFeaturePort
    {
        public bool SetFeature(byte[] report) => true;
        public byte[]? GetFeature(int length) => new byte[length];
        public void Dispose() { }
    }
}
