using AkiraCore.Core.Lighting;
using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Tests;

// Esquema de cores por tecla → buffer de fcn.0044b8a0 (cabo) / fcn.0044bd20 (dongle)
public class CustomLightTests
{
    private static CustomLightState Scheme(Dictionary<int, string> colors, int brightness = 5) =>
        new("c1", "Teste", brightness, colors);

    [Fact]
    public void Frame_PutsEachKeyColorAtItsLedIndex()
    {
        // 39 = W, 96 = Fn (a tecla tem LED mesmo sem ser remapeável)
        var buffer = Scheme(new() { [39] = "#112233", [96] = "#ABCDEF" }).ToFrame().ToBuffer();

        Assert.Equal([39, 0x11, 0x22, 0x33], buffer[(39 * 4)..(39 * 4 + 4)]);
        Assert.Equal([96, 0xAB, 0xCD, 0xEF], buffer[(96 * 4)..(96 * 4 + 4)]);
        Assert.Equal([0, 0, 0, 0], buffer[(40 * 4)..(40 * 4 + 4)]); // tecla sem cor fica apagada
    }

    [Fact]
    public void Frame_AppliesTheColorCorrection()
    {
        var frame = Scheme(new() { [1] = "#FF0010" }).ToFrame(swapRedBlue: true);

        Assert.Equal(new Rgb(0x10, 0x00, 0xFF), frame.Colors[1]);
    }

    [Theory]
    [InlineData(14)]  // índice sem tecla (lacuna da matriz)
    [InlineData(0)]
    [InlineData(200)]
    public void RejectsIndicesWithoutKey(int index)
    {
        Assert.Throws<ArgumentException>(() => Scheme(new() { [index] = "#FFFFFF" }).Validate());
    }

    [Fact]
    public void RejectsBadColorsAndBrightness()
    {
        Assert.Throws<ArgumentException>(() => Scheme(new() { [1] = "vermelho" }).Validate());
        Assert.Throws<ArgumentException>(() => Scheme(new(), brightness: 6).Validate());
        Assert.Throws<ArgumentException>(() => (Scheme(new()) with { Name = " " }).Validate());
    }

    [Fact]
    public void ValidateAll_RejectsDuplicateIds()
    {
        var scheme = Scheme(new());

        Assert.Throws<ArgumentException>(() => CustomLightState.ValidateAll([scheme, scheme]));
    }
}
