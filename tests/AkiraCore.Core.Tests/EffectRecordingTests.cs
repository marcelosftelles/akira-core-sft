using System.Text.Json;
using AkiraCore.Core.Diagnostics;
using AkiraCore.Core.Lighting;

namespace AkiraCore.Core.Tests;

/// <summary>Gravação dos efeitos pelo cabo (leituras 04 F5, fcn.0042adb0), base da prévia do dongle.</summary>
public class EffectRecordingTests
{
    private static readonly IReadOnlyList<RecordingVariant> Plan = EffectRecordingPlan.Build();

    private static IEnumerable<RecordingVariant> Of(string key) => Plan.Where(v => v.Config.Effect.Key == key);

    [Fact]
    public void Plan_CoversEveryEffectExceptOff()
    {
        var recorded = Plan.Select(v => v.Config.Effect.Key).Distinct().ToList();
        Assert.Equal(EffectCatalog.All.Where(e => e.Mode != 0).Select(e => e.Key), recorded);
    }

    [Fact]
    public void Plan_UsesOfficialDefaultsForTheBaseVariants()
    {
        // fcn.0042b280: brilho 5, velocidade 3, direção 0
        foreach (var variant in Plan.Where(v => v.Config.Effect.Key != "rolling"))
        {
            Assert.Equal(5, variant.Config.Brightness);
            Assert.Equal(3, variant.Config.Speed);
        }
    }

    [Fact]
    public void Plan_RecordsRainbowAndSingleColorWhenTheEffectHasBoth()
    {
        var breath = Of("breath").ToList();
        Assert.Equal(2, breath.Count);
        Assert.Contains(breath, v => v.Config.Rainbow);
        Assert.Contains(breath, v => !v.Config.Rainbow && v.Config.Color == EffectRecordingPlan.SingleColor);
    }

    [Theory]
    [InlineData("colourful")]
    [InlineData("spectrum")]
    public void Plan_EffectsWithoutColorAreRecordedOnce(string key) =>
        Assert.Single(Of(key));

    [Fact]
    public void Plan_RecordsEveryArrowOfDirectionalEffects()
    {
        // fcn.0042c500: setas 0/1 nos efeitos com 0x04, 2/3 no Rolagem (0x08)
        Assert.Equal(new[] { 0, 0, 1 }, Of("flowing").Select(v => v.Config.Direction).Order());
        Assert.Equal(new[] { 0, 0, 2, 3 }, Of("scrolling").Select(v => v.Config.Direction).Order());
    }

    [Fact]
    public void Plan_ProbesSpeedAndBrightnessScaleOnRolling()
    {
        var rolling = Of("rolling").ToList();
        Assert.Contains(rolling, v => v.Config.Speed == 1);
        Assert.Contains(rolling, v => v.Config.Speed == 5);
        Assert.Contains(rolling, v => v.Config.Brightness == 1);
    }

    [Fact]
    public void Plan_StaticIsShorterThanAnimatedEffects()
    {
        Assert.All(Of("static"), v => Assert.Equal(EffectRecordingPlan.StaticDuration, v.Duration));
        Assert.All(Of("rolling"), v => Assert.Equal(EffectRecordingPlan.AnimatedDuration, v.Duration));
    }

    [Fact]
    public void Plan_ForOneEffectRecordsOnlyItWithMoreTime()
    {
        var explode = EffectRecordingPlan.Build("explode");
        Assert.All(explode, v => Assert.Equal("explode", v.Config.Effect.Key));
        Assert.All(explode, v => Assert.Equal(EffectRecordingPlan.SingleEffectDuration, v.Duration));
        Assert.Equal(2, explode.Count);
    }

    [Fact]
    public void Recorded_CountsDistinctFrames()
    {
        var recorded = new RecordedVariant(Plan[0]);
        recorded.Frames.Add((0, [1, 2]));
        recorded.Frames.Add((40, [1, 2]));
        recorded.Frames.Add((80, [1, 3]));
        Assert.Equal(2, recorded.DistinctFrames);
    }

    [Fact]
    public void File_KeepsRawBuffersKeysAndTheBlockSent()
    {
        var variant = Of("rolling").First();
        var recorded = new RecordedVariant(variant);
        recorded.Frames.Add((35, [0xAB, 0xCD]));
        recorded.Keys.Add((120, 0x1E, true));

        using var json = JsonDocument.Parse(EffectRecordingFile.Serialize([recorded], DateTimeOffset.UnixEpoch));
        var root = json.RootElement;
        Assert.Equal(EffectRecordingFile.Format, root.GetProperty("format").GetString());
        var saved = root.GetProperty("variants")[0];
        Assert.Equal("rolling", saved.GetProperty("effect").GetString());
        Assert.Equal(Convert.ToHexString(variant.Config.ToLightBlock()), saved.GetProperty("block").GetString());
        Assert.Equal("ABCD", saved.GetProperty("frames")[0].GetProperty("leds").GetString());
        Assert.Equal(35, saved.GetProperty("frames")[0].GetProperty("t").GetInt32());
        Assert.Equal(0x1E, saved.GetProperty("keys")[0].GetProperty("scan").GetInt32());
    }
}
