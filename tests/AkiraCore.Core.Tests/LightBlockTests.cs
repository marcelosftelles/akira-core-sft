using AkiraCore.Core.Lighting;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Offsets vindos da análise estática do DeviceDriver.exe oficial.
public class LightBlockTests
{
    [Fact]
    public void LightBlock_MapsFieldsToOfficialOffsets()
    {
        var config = new LightingConfig(EffectCatalog.ByKey("breath"), new Rgb(0x11, 0x22, 0x33),
            brightness: 4, speed: 2, direction: 1, rainbow: true);

        var block = config.ToLightBlock();

        Assert.Equal(16, block.Length);
        Assert.Equal(7, block[0]);
        Assert.Equal([0x11, 0x22, 0x33], block[1..4]);
        Assert.Equal(1, block[8]);
        Assert.Equal(4, block[9]);
        Assert.Equal(2, block[10]);
        Assert.Equal(1, block[11]);
        Assert.Equal([0xAA, 0x55], block[14..16]);
    }

    [Fact]
    public void SwapRedBlue_InvertsChannels()
    {
        var config = new LightingConfig(EffectCatalog.ByKey("static"), new Rgb(0xFF, 0x00, 0x10))
        {
            SwapRedBlue = true,
        };

        Assert.Equal([0x10, 0x00, 0xFF], config.ToLightBlock()[1..4]);
    }

    [Fact]
    public void OffEffect_SendsOnlyModeByteAndSignature()
    {
        var block = new LightingConfig(EffectCatalog.Off, new Rgb(255, 255, 255)).ToLightBlock();

        Assert.All(block[..14], b => Assert.Equal(0, b));
        Assert.Equal([0xAA, 0x55], block[14..16]);
    }

    [Fact]
    public void CustomModeBlock_UsesFlagAndBrightnessIndex()
    {
        var block = PacketBuilder.CustomModeBlock(2);

        Assert.Equal(0x80, block[0]);
        Assert.Equal(2, block[9]);
    }

    [Theory]
    [InlineData(6, 3)]
    [InlineData(-1, 3)]
    [InlineData(3, 6)]
    public void LightingConfig_RejectsOutOfRangeValues(int brightness, int speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingConfig(EffectCatalog.ByKey("static"), new Rgb(), brightness, speed));
    }

    [Fact]
    public void Catalog_HasUniqueModesAndKeys()
    {
        Assert.Equal(EffectCatalog.All.Count, EffectCatalog.All.Select(e => e.Mode).Distinct().Count());
        Assert.Equal(EffectCatalog.All.Count, EffectCatalog.All.Select(e => e.Key).Distinct().Count());
    }
}
