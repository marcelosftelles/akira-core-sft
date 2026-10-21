using System.Text.RegularExpressions;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Live;
using AkiraCore.Core.Presentation;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

public class LiveTests
{
    private static readonly Rgb Red = new(0xFF, 0, 0);
    private static readonly Rgb Blue = new(0, 0, 0xFF);

    private static KeyShape Key(string name) => KeyGeometry.All.Single(k => k.Name == name);

    // --- Geometria -------------------------------------------------------------------

    [Fact]
    public void Geometry_HasTheSameKeysAsTheOfficialLayout()
    {
        Assert.Equal(KeyboardLayout.All.Select(k => k.Index).Order(), KeyGeometry.All.Select(k => k.Led).Order());
    }

    [Fact]
    public void Geometry_MatchesTheInterfaceLayoutFile()
    {
        // As duas cópias (Ui/layout.js e KeyGeometry) vêm do mesmo rgb-keyboard.xml: não podem divergir
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AkiraCore.App", "Ui", "layout.js"));
        var pattern = new Regex(@"\{ name: ""(?<name>(?:[^""\\]|\\.)*)"", led: (?<led>\d+), x: (?<x>\d+), y: (?<y>\d+), w: (?<w>\d+), h: (?<h>\d+) \}");
        var keys = pattern.Matches(layout)
            .Select(m => (Led: int.Parse(m.Groups["led"].Value), X: int.Parse(m.Groups["x"].Value),
                Y: int.Parse(m.Groups["y"].Value), W: int.Parse(m.Groups["w"].Value), H: int.Parse(m.Groups["h"].Value)))
            .ToList();

        Assert.Equal(KeyGeometry.All.Count, keys.Count);
        foreach (var (shape, js) in KeyGeometry.All.Zip(keys))
        {
            Assert.Equal((js.Led, js.X, js.Y, js.W, js.H),
                (shape.Led, (int)shape.X, (int)shape.Y, (int)shape.Width, (int)shape.Height));
        }
    }

    [Theory]
    [InlineData("Esc", 0)]
    [InlineData("F12", 0)]
    [InlineData("~`", 1)]
    [InlineData("Tab", 2)]
    [InlineData("CapsLock", 3)]
    [InlineData("Shift_L", 4)]
    [InlineData("↑", 4)]
    [InlineData("Space", 5)]
    [InlineData("←", 5)]
    public void Geometry_KnowsTheRowOfEachKey(string name, int row)
    {
        Assert.Equal(row, KeyGeometry.RowOf(Key(name)));
    }

    [Theory]
    [InlineData(0x11, 39)]     // W
    [InlineData(0xE048, 101)]  // seta para cima (prefixo E0)
    [InlineData(0x01, 1)]      // Esc
    public void ScanCodes_FindTheLedOfTheTouchedKey(int scan, int led)
    {
        Assert.Equal(led, KeyboardLayout.LedOfScanCode(scan));
    }

    [Fact]
    public void ScanCodes_IgnoreKeysTheAkiraDoesNotHave()
    {
        Assert.Null(KeyboardLayout.LedOfScanCode(0x45)); // Num Lock
    }

    // --- Quadro ----------------------------------------------------------------------

    [Fact]
    public void StreamFrame_ListsEveryKeyInLayoutOrderAndPadsWithZeros()
    {
        var frame = new LiveFrame();
        frame[1] = new Rgb(0x11, 0x22, 0x33);
        frame[102] = new Rgb(0xAA, 0xBB, 0xCC);

        var bytes = frame.ToStreamFrame();

        Assert.Equal(StreamProtocol.FrameLength, bytes.Length);
        Assert.Equal([1, 0x11, 0x22, 0x33], bytes[..4]);
        Assert.Equal([2, 0, 0, 0], bytes[4..8]); // F1 apagada, mas listada
        var last = (KeyGeometry.All.Count - 1) * 4;
        Assert.Equal([102, 0xAA, 0xBB, 0xCC], bytes[last..(last + 4)]);
        Assert.All(bytes[(last + 4)..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void StreamFrame_AppliesTheColorCorrection()
    {
        var frame = new LiveFrame();
        frame[1] = new Rgb(0xFF, 0x10, 0x00);

        Assert.Equal([1, 0x00, 0x10, 0xFF], frame.ToStreamFrame(swapRedBlue: true)[..4]);
    }

    // --- Cor -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0xFF, 0, 0)]
    [InlineData(120, 0, 0xFF, 0)]
    [InlineData(240, 0, 0, 0xFF)]
    [InlineData(-120, 0, 0, 0xFF)]
    public void Hsv_GivesThePrimaryColors(double hue, int r, int g, int b)
    {
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), ColorMath.Hsv(hue));
    }

    [Fact]
    public void BrightnessFactor_GoesFromDimToFull()
    {
        Assert.True(ColorMath.BrightnessFactor(0) > 0);
        Assert.Equal(1, ColorMath.BrightnessFactor(5));
        Assert.Equal(1, ColorMath.BrightnessFactor(99));
    }

    // --- Teclas animadas --------------------------------------------------------------

    [Fact]
    public void Scene_StaticKeysKeepTheirColorAndOthersStayOff()
    {
        var scene = new AnimatedScene(new Dictionary<int, AnimatedKey> { [39] = new(KeyAnimation.Static, Red) }, 3);
        var frame = new LiveFrame();

        scene.Render(frame, new LiveTime(10, 0.03));

        Assert.Equal(Red, frame[39]);
        Assert.Equal(default, frame[40]);
    }

    [Fact]
    public void Scene_ReactiveKeyLightsWhenTouchedAndFades()
    {
        var scene = new AnimatedScene(new Dictionary<int, AnimatedKey> { [39] = new(KeyAnimation.Reactive, Red) }, 3);
        var frame = new LiveFrame();

        scene.Render(frame, new LiveTime(1, 0.03));
        Assert.Equal(default, frame[39]);

        scene.Press(39);
        scene.Render(frame, new LiveTime(2, 0.03));
        Assert.Equal(Red, frame[39]);

        scene.Render(frame, new LiveTime(5, 0.03));
        Assert.Equal(default, frame[39]);
    }

    [Fact]
    public void Scene_BlinkAlternates()
    {
        var scene = new AnimatedScene(new Dictionary<int, AnimatedKey> { [39] = new(KeyAnimation.Blink, Red) }, 3);
        var on = new LiveFrame();
        var off = new LiveFrame();

        scene.Render(on, new LiveTime(0.1, 0.03));   // velocidade 3: 1 piscada por segundo
        scene.Render(off, new LiveTime(0.6, 0.03));

        Assert.Equal(Red, on[39]);
        Assert.Equal(default, off[39]);
    }

    // --- Ritmo musical ----------------------------------------------------------------

    [Fact]
    public void Music_FullLevelLightsWholeColumnsAndSilenceTurnsOff()
    {
        var music = new MusicVisualizer(MusicStyle.Solid, Blue, background: false);
        var frame = new LiveFrame();

        music.SetLevels([1f]);
        music.Render(frame, new LiveTime(1, 0.03));
        Assert.All(KeyGeometry.All, k => Assert.Equal(Blue, frame[k.Led]));

        music.SetLevels([0f]);
        music.Render(frame, new LiveTime(1.1, 0.03));
        Assert.All(KeyGeometry.All, k => Assert.Equal(default, frame[k.Led]));
    }

    [Fact]
    public void Music_HalfLevelLightsOnlyTheLowerRows()
    {
        var music = new MusicVisualizer(MusicStyle.Solid, Blue, background: false);
        var frame = new LiveFrame();

        music.SetLevels([0.5f]);
        music.Render(frame, new LiveTime(1, 0.03));

        Assert.Equal(Blue, frame[Key("Space").Led]);     // fileira 5: base da barra
        Assert.Equal(default, frame[Key("Esc").Led]);    // fileira 0: topo
    }

    [Fact]
    public void Music_ColumnHeightsUseTheBandOfEachColumn()
    {
        var heights = MusicVisualizer.ColumnHeights([0f, 0.5f, 1f], columns: 6, maxHeight: 6);

        Assert.Equal([0, 0, 3, 3, 6, 6], heights);
    }

    // --- CPU e cronômetro --------------------------------------------------------------

    [Fact]
    public void Cpu_WholeKeyboardMovesTowardsTheHighColor()
    {
        var cpu = new CpuMeter(CpuStyle.Whole, Blue, Red);
        var frame = new LiveFrame();

        cpu.SetUsage(1);
        for (var t = 0.0; t < 5; t += 0.1)
        {
            cpu.Render(frame, new LiveTime(t, 0.1));
        }

        Assert.Equal(Red, frame[Key("A").Led]);
    }

    [Fact]
    public void Timer_CountsDownAndPauses()
    {
        var start = new DateTime(2026, 10, 21, 10, 0, 0);
        var timer = new CountdownTimer(TimeSpan.FromMinutes(10));

        timer.Start(start);
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Remaining(start.AddMinutes(4)));

        timer.Pause(start.AddMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Remaining(start.AddMinutes(9)));
        Assert.False(timer.Running);

        timer.Start(start.AddMinutes(9));
        Assert.True(timer.IsFinished(start.AddMinutes(15)));
    }

    [Fact]
    public void TimerBar_HalfTimeLightsHalfOfTheFunctionRow()
    {
        var now = new DateTime(2026, 10, 21, 10, 0, 0);
        var timer = new CountdownTimer(TimeSpan.FromMinutes(12));
        timer.Start(now);
        var bar = new TimerBar(timer, Red, () => now.AddMinutes(6));
        var frame = new LiveFrame();

        bar.Render(frame, new LiveTime(1, 0.03));

        Assert.Equal(Red, frame[Key("F6").Led]);
        Assert.NotEqual(Red, frame[Key("F7").Led]);
    }

    // --- Aviso e motor -----------------------------------------------------------------

    [Fact]
    public void Alert_BlinksTheGivenNumberOfTimes()
    {
        var alert = new AlertFlash(Red, times: 2);
        var frame = new LiveFrame();

        Assert.True(alert.Apply(frame, 10.0));
        Assert.Equal(Red, frame[1]);
        Assert.True(alert.Apply(frame, 10.4));
        Assert.Equal(default, frame[1]);
        Assert.True(alert.Apply(frame, 10.6));
        Assert.False(alert.Apply(frame, 11.0));
    }

    [Fact]
    public void Engine_ScalesByBrightnessAndReturnsToTheSourceAfterTheAlert()
    {
        var engine = new LiveEngine
        {
            Source = new AnimatedScene(new Dictionary<int, AnimatedKey> { [1] = new(KeyAnimation.Static, Blue) }, 3),
            Brightness = 5,
        };

        engine.ShowAlert(Red, 1);
        Assert.Equal(Red, engine.Render(1.0)[1]);
        Assert.Equal(Blue, engine.Render(1.6)[1]);

        engine.Brightness = 0;
        Assert.True(engine.Render(1.7)[1].B < 0x20);
    }

    // --- Espectro ------------------------------------------------------------------------

    [Fact]
    public void Spectrum_PutsAToneInTheRightBand()
    {
        var low = PeakBand(100);
        var high = PeakBand(4000);

        Assert.True(low < high, $"100 Hz na faixa {low}, 4 kHz na faixa {high}");
    }

    [Fact]
    public void Spectrum_SilenceEmptiesTheBars()
    {
        var analyzer = new SpectrumAnalyzer(48000);
        analyzer.AddSamples(Tone(1000, 48000, 4096));
        Assert.Contains(analyzer.Analyze(0.03), level => level > 0.5);

        float[] levels = [];
        for (var i = 0; i < 40; i++)
        {
            levels = analyzer.Analyze(0.05); // nada chega: conta como silêncio
        }

        Assert.All(levels, level => Assert.Equal(0, level));
    }

    private static int PeakBand(double hz)
    {
        var analyzer = new SpectrumAnalyzer(48000);
        analyzer.AddSamples(Tone(hz, 48000, 4096));
        var levels = analyzer.Analyze(0.03);
        return Array.IndexOf(levels, levels.Max());
    }

    private static float[] Tone(double hz, int rate, int count) =>
        Enumerable.Range(0, count).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();

    // --- Estado da página -------------------------------------------------------------------

    [Fact]
    public void Settings_DefaultIsValid()
    {
        LiveSettingsState.Default.Validate();
    }

    [Fact]
    public void Settings_RejectsInvalidValues()
    {
        var ok = LiveSettingsState.Default;

        Assert.Throws<ArgumentException>(() => (ok with { Mode = "disco" }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with { Brightness = 6 }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with
        {
            Scene = new LiveSceneState(3, new Dictionary<int, LiveKeyState> { [14] = new("static", "#FFFFFF") }),
        }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with
        {
            Scene = new LiveSceneState(3, new Dictionary<int, LiveKeyState> { [1] = new("dance", "#FFFFFF") }),
        }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with { Timer = new TimerOptionsState(0, "#FFFFFF") }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with { Music = ok.Music with { Color = "azul" } }).Validate());
    }

    [Theory]
    [InlineData("music", "dongle", true)]
    [InlineData("scene", "dongle", false)]
    [InlineData("screen", "usb", true)]
    [InlineData("cpu", null, false)]
    [InlineData("off", null, true)]
    public void Modes_KnowWhichChannelRunsThem(string mode, string? channel, bool supported)
    {
        Assert.Equal(supported, LiveModes.Supports(mode, channel));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkiraCore.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("AkiraCore.sln não encontrado.");
    }
}
