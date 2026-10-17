using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Valores de fcn.00433e20 (combo) e fcn.00434900 (envio por canal).
public class SideLightTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    [Fact]
    public void Catalog_CoversOfficialRange0x20To0x29()
    {
        Assert.Equal(Enumerable.Range(0x20, 10).Select(v => (byte)v), SideLightCatalog.All.Select(m => m.Value));
    }

    [Fact]
    public void Dongle_UsesSideLightHeaderAndModeAtBlockStart()
    {
        var port = new RecordingReportPort();

        new KeyboardClient(new DongleLink(port, new NoDelay())).SetSideLight(SideLightCatalog.ByKey("green"));

        var report = port.Written.Single();
        Assert.Equal([0x00, 0x05, 0x01, 0x00, 0x23], report[..5]);
        Assert.Equal([0xAA, 0x55], report[18..20]);
        // 05+01+23+AA+55 = 0x128, fica 0x28
        Assert.Equal(0x28, report[32]);
    }

    [Fact]
    public void Cable_UsesSameTransactionWithModeInDataPacket()
    {
        var port = new RecordingFeaturePort();

        new KeyboardClient(new FeatureLink(port, new NoDelay())).SetSideLight(SideLightCatalog.ByKey("off"));

        var headers = port.Written.Select(r => Convert.ToHexString(r, 0, 3)).ToArray();
        Assert.Equal(["000418", "000413", "002900", "000402", "0004F0"], headers);
    }

    [Fact]
    public void KeysLighting_StillUsesMainHeaderOnDongle()
    {
        var report = PacketBuilder.DongleLighting(SideLightTests.Block());

        Assert.Equal([0x05, 0x10, 0x00], report[1..4]);
    }

    private static byte[] Block() =>
        new LightingConfig(EffectCatalog.ByKey("static"), new Rgb(1, 2, 3)).ToLightBlock();
}

public class ExperimentalBlockTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    [Fact]
    public void ForcesSignatureEvenIfUserOverwritesIt()
    {
        var port = new RecordingReportPort();
        var block = new byte[16];
        block[0] = 0x07;
        block[14] = 0x00;

        new KeyboardClient(new DongleLink(port, new NoDelay())).SendExperimentalBlock(block, LightTarget.SideStrips);

        var report = port.Written.Single();
        Assert.Equal([0x05, 0x01, 0x00, 0x07], report[1..5]);
        Assert.Equal([0xAA, 0x55], report[18..20]);
    }

    [Fact]
    public void RejectsWrongLength()
    {
        var client = new KeyboardClient(new DongleLink(new RecordingReportPort(), new NoDelay()));

        Assert.Throws<ArgumentException>(() => client.SendExperimentalBlock(new byte[20], LightTarget.Keys));
    }
}
