using AkiraCore.Core.Lighting;
using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Tests;

public class PresentationTests
{
    [Fact]
    public void StateBecomesTheSameBlockAsTheOfficialFields()
    {
        var state = new LightingState("rolling", "#112233", Brightness: 4, Speed: 2, Direction: 1, Rainbow: true);

        var block = state.ToConfig().ToLightBlock();

        // fcn.0042afd0: [0] modo, [1..3] cor, [8] colorful, [9] brilho, [10] velocidade, [11] direção
        Assert.Equal(11, block[0]);
        Assert.Equal([0x11, 0x22, 0x33], block[1..4]);
        Assert.Equal([1, 4, 2, 1], block[8..12]);
    }

    [Fact]
    public void RoundTripsThroughConfig()
    {
        var state = new LightingState("scrolling", "#FF8800", 3, 5, Direction: 2, Rainbow: false);

        Assert.Equal(state, LightingState.From(state.ToConfig()));
    }

    [Fact]
    public void FactoryDefaultsMatchWhatTheScreenShows() =>
        Assert.Equal(new LightingState("rolling", "#FF0000", 5, 3, 0, true),
            LightingState.From(FactoryDefaults.Lighting));

    [Theory]
    [InlineData("nao-existe", "#FF0000", 5, 3)]
    [InlineData("static", "#FF0000", 6, 3)]
    [InlineData("static", "#FF0000", 5, -1)]
    public void RejectsInvalidValuesBeforeAnythingIsSent(string effect, string color, int brightness, int speed) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new LightingState(effect, color, brightness, speed, 0, false).ToConfig());

    [Fact]
    public void MissingFieldsAreRejectedAsArgumentErrors()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LightingState(null!, "#FF0000", 5, 3, 0, false).ToConfig());
        Assert.ThrowsAny<ArgumentException>(() => new LightingState("static", null!, 5, 3, 0, false).ToConfig());
    }

    [Fact]
    public void RejectsMalformedColor() =>
        Assert.Throws<FormatException>(() => new LightingState("static", "red", 5, 3, 0, false).ToConfig());

    // fcn.0042c500: botão 0x5b grava 1, 0x5c grava 0, 0x5d grava 2 e 0x5e grava 3.
    // fcn.00437540 cria 0x5b com img_direction_right, 0x5c left, 0x5d top e 0x5e bottom.
    [Fact]
    public void DirectionValuesAreTheOfficialButtons()
    {
        Assert.Equal(0, (int)EffectDirection.Left);
        Assert.Equal(1, (int)EffectDirection.Right);
        Assert.Equal(2, (int)EffectDirection.Up);
        Assert.Equal(3, (int)EffectDirection.Down);
    }

    // fcn.0042aa10: bit 0x04 mostra direita/esquerda e marca direita só com 1;
    // bit 0x08 (só o Rolagem) mostra cima/baixo e marca cima só com 2
    [Theory]
    [InlineData("rolling", new[] { 0, 1 }, 0)]
    [InlineData("rotating", new[] { 0, 1 }, 0)]
    [InlineData("flowing", new[] { 0, 1 }, 0)]
    [InlineData("tilt", new[] { 0, 1 }, 0)]
    [InlineData("scrolling", new[] { 2, 3 }, 3)]
    public void DirectionalEffectsShowTheOfficialArrows(string key, int[] arrows, int fallback)
    {
        var view = EffectView.Catalog.Single(e => e.Key == key);

        Assert.Equal(arrows, view.Directions);
        Assert.Equal(fallback, view.DirectionFallback);
    }

    [Fact]
    public void OnlyFiveEffectsHaveDirection()
    {
        var withArrows = EffectView.Catalog.Where(e => e.Directions.Count > 0).Select(e => e.Key);

        Assert.Equal(["scrolling", "rolling", "rotating", "flowing", "tilt"], withArrows);
        Assert.All(EffectView.Catalog.Where(e => e.Directions.Count == 0), e => Assert.Null(e.DirectionFallback));
    }

    // O 0 semeado por fcn.0042b280 vale em qualquer efeito; fora dele, só as setas do próprio efeito
    [Theory]
    [InlineData("rolling", 0, true)]
    [InlineData("rolling", 1, true)]
    [InlineData("rolling", 2, false)]
    [InlineData("scrolling", 0, true)]
    [InlineData("scrolling", 1, false)]
    [InlineData("scrolling", 2, true)]
    [InlineData("scrolling", 3, true)]
    [InlineData("breath", 0, true)]
    [InlineData("breath", 1, false)]
    public void AcceptsOnlyDirectionsTheOfficialCanStore(string key, int direction, bool accepted)
    {
        var state = new LightingState(key, "#FF0000", 5, 3, direction, true);

        if (accepted)
        {
            Assert.Equal(direction, state.ToConfig().ToLightBlock()[11]);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => state.ToConfig());
        }
    }

    // fcn.0042b280 semeia os 19 efeitos com default_brightness=5, default_speed=3 do XML e o
    // bloco de 0x575200: direção 0, colorful 1, colorindex 0, color_value 0x000000FF
    [Fact]
    public void EveryEffectStartsWithTheOfficialSeed() =>
        Assert.Equal(new EffectSettingsState("#FF0000", 5, 3, 0, true), EffectSettingsState.Default);

    [Fact]
    public void SlidersStartAtOneLikeTheOfficial() => Assert.Equal(1, LightingState.LevelMin);

    [Fact]
    public void NormalizedKeepsWhatTheEffectShows()
    {
        var state = new LightingState("rolling", "#00FF00", 2, 4, 1, false);

        Assert.Equal(state, state.Normalized());
    }

    [Fact]
    public void NormalizedClampsOldZeroLevelsToTheSliderRange()
    {
        var state = new LightingState("breath", "#00FF00", 0, 0, 0, false).Normalized();

        Assert.Equal(1, state.Brightness);
        Assert.Equal(1, state.Speed);
    }

    // O oficial manda o que está gravado (fcn.0042afd0); controle escondido nunca muda o
    // valor semeado. Ex.: Estático sem velocidade fica com 3; Colorido e Espectro, sem
    // "Colorido" nem cor, ficam com colorful 1 e vermelho.
    [Fact]
    public void NormalizedUsesTheSeedForHiddenControls()
    {
        var staticLight = new LightingState("static", "#123456", 4, 1, 0, false).Normalized();
        Assert.Equal(3, staticLight.Speed);
        Assert.Equal("#123456", staticLight.Color);

        var colourful = new LightingState("colourful", "#00FF00", 2, 2, 0, false).Normalized();
        Assert.True(colourful.Rainbow);
        Assert.Equal("#FF0000", colourful.Color);
        Assert.Equal(2, colourful.Brightness);
    }

    [Theory]
    [InlineData("scrolling", 1, 0)] // o "invertido" antigo não existe no Rolagem
    [InlineData("breath", 1, 0)]
    [InlineData("rolling", 1, 1)]
    public void NormalizedDropsDirectionsTheEffectDoesNotHave(string key, int direction, int expected) =>
        Assert.Equal(expected, new LightingState(key, "#FF0000", 5, 3, direction, true).Normalized().Direction);

    [Fact]
    public void SettingsRoundTripPerEffect()
    {
        var settings = new EffectSettingsState("#0000FF", 2, 4, 3, false);

        var state = LightingState.For("scrolling", settings);

        Assert.Equal("scrolling", state.Effect);
        Assert.Equal(settings, state.Settings);
    }

    [Fact]
    public void SanitizeDropsBrokenEntriesAndNormalizesTheRest()
    {
        var saved = new Dictionary<string, EffectSettingsState>
        {
            ["breath"] = new("#00FF00", 0, 2, 0, false),
            ["nao-existe"] = new("#00FF00", 5, 3, 0, true),
            ["static"] = new("verde", 5, 3, 0, true),
            ["rolling"] = new("#00FF00", 5, 3, 2, true),
        };

        var clean = EffectSettingsState.Sanitize(saved);

        Assert.Equal(["breath", "rolling"], clean.Keys.Order());
        Assert.Equal(1, clean["breath"].Brightness);
        Assert.Equal(0, clean["rolling"].Direction);
    }

    [Fact]
    public void ValidateAllRejectsWhatCannotBeSent()
    {
        EffectSettingsState.ValidateAll(null);
        EffectSettingsState.ValidateAll(new Dictionary<string, EffectSettingsState> { ["rolling"] = new("#00FF00", 5, 3, 1, true) });

        Assert.Throws<ArgumentException>(() => EffectSettingsState.ValidateAll(
            new Dictionary<string, EffectSettingsState> { ["rolling"] = new("#00FF00", 5, 3, 3, true) }));
        Assert.Throws<ArgumentException>(() => EffectSettingsState.ValidateAll(
            new Dictionary<string, EffectSettingsState> { ["static"] = new("verde", 5, 3, 0, true) }));
    }

    [Fact]
    public void CatalogHidesOffAndExposesControlsPerEffect()
    {
        Assert.DoesNotContain(EffectView.Catalog, e => e.Mode == 0);
        Assert.Equal(19, EffectView.Catalog.Count);

        var spectrum = EffectView.Catalog.Single(e => e.Key == "spectrum");
        Assert.False(spectrum.Color);
        Assert.False(spectrum.Rainbow);
        Assert.True(spectrum.Speed);

        Assert.False(EffectView.Catalog.Single(e => e.Key == "static").Speed);
    }

    // config_func de cada efeito em fcn.0042b280, na ordem dos modos 1 a 19
    [Fact]
    public void CapabilitiesAreTheOfficialConfigFunc()
    {
        int[] official = [0x31, 0x33, 0x33, 0x33, 0x33, 0x03, 0x33, 0x03, 0x33, 0x3b,
            0x37, 0x37, 0x33, 0x33, 0x33, 0x37, 0x33, 0x37, 0x33];

        var ours = EffectCatalog.All.Where(e => e.Mode != 0).OrderBy(e => e.Mode).Select(e => (int)e.Capabilities);

        Assert.Equal(official, ours);
        Assert.Equal(EffectCapabilities.None, EffectCatalog.Off.Capabilities);
    }
}
