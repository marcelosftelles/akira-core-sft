using AkiraCore.Core.Lighting;
using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Tests;

public class PresentationTests
{
    [Fact]
    public void StateBecomesTheSameBlockAsTheOfficialFields()
    {
        var state = new LightingState("breath", "#112233", Brightness: 4, Speed: 2, Reverse: true, Rainbow: true);

        var block = state.ToConfig().ToLightBlock();

        Assert.Equal(7, block[0]);
        Assert.Equal([0x11, 0x22, 0x33], block[1..4]);
        Assert.Equal([1, 4, 2, 1], block[8..12]);
    }

    [Fact]
    public void RoundTripsThroughConfig()
    {
        var state = new LightingState("rolling", "#FF8800", 3, 5, Reverse: false, Rainbow: false);

        Assert.Equal(state, LightingState.From(state.ToConfig()));
    }

    [Fact]
    public void FactoryDefaultsMatchWhatTheScreenShows() =>
        Assert.Equal(new LightingState("rolling", "#FF0000", 5, 3, false, true),
            LightingState.From(FactoryDefaults.Lighting));

    [Theory]
    [InlineData("nao-existe", "#FF0000", 5, 3)]
    [InlineData("static", "#FF0000", 6, 3)]
    [InlineData("static", "#FF0000", 5, -1)]
    public void RejectsInvalidValuesBeforeAnythingIsSent(string effect, string color, int brightness, int speed) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new LightingState(effect, color, brightness, speed, false, false).ToConfig());

    [Fact]
    public void MissingFieldsAreRejectedAsArgumentErrors()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LightingState(null!, "#FF0000", 5, 3, false, false).ToConfig());
        Assert.ThrowsAny<ArgumentException>(() => new LightingState("static", null!, 5, 3, false, false).ToConfig());
    }

    [Fact]
    public void RejectsMalformedColor() =>
        Assert.Throws<FormatException>(() => new LightingState("static", "red", 5, 3, false, false).ToConfig());

    [Fact]
    public void CatalogHidesOffAndExposesControlsPerEffect()
    {
        Assert.DoesNotContain(EffectView.Catalog, e => e.Mode == 0);
        Assert.Equal(19, EffectView.Catalog.Count);

        var spectrum = EffectView.Catalog.Single(e => e.Key == "spectrum");
        Assert.False(spectrum.Color);
        Assert.True(spectrum.Speed);

        Assert.True(EffectView.Catalog.Single(e => e.Key == "scrolling").Direction);
        Assert.True(EffectView.Catalog.Single(e => e.Key == "rolling").Direction);
        Assert.False(EffectView.Catalog.Single(e => e.Key == "static").Speed);
    }
}
