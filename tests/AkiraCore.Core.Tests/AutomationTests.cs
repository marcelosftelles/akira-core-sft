using AkiraCore.Core.Automation;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Tests;

public class AutomationTests
{
    private static ProfileConfigState Config() =>
        new(LightingState.From(FactoryDefaults.Lighting), true, null, [], null);

    private static ProfileState Profile(string id, params string[] apps) => new(id, $"Perfil {id}", Config(), apps);

    // --- Perfil por programa ------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Jogos\Valorant.exe", "valorant.exe")]
    [InlineData("VALORANT.EXE", "valorant.exe")]
    [InlineData("valorant", "valorant.exe")]
    [InlineData("  \"code.exe\" ", "code.exe")]
    public void Apps_AreNormalizedToTheFileName(string input, string expected)
    {
        Assert.Equal(expected, ProfileApps.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("jo?go")]
    [InlineData(".exe")]
    public void Apps_RejectInvalidNames(string input)
    {
        Assert.Throws<ArgumentException>(() => ProfileApps.Normalize(input));
    }

    [Fact]
    public void Apps_CannotBelongToTwoProfiles()
    {
        Assert.Throws<ArgumentException>(() =>
            ProfileApps.ValidateAll([Profile("a", "jogo.exe"), Profile("b", "Jogo")]));
    }

    [Fact]
    public void Target_SwitchesToTheProfileOfTheProgramOrTheFallback()
    {
        IReadOnlyList<ProfileState> profiles = [Profile("padrao"), Profile("jogo", "valorant.exe")];
        var auto = new AutoProfileState(true, "padrao");

        Assert.Equal("jogo", ProfileApps.Target(auto, profiles, "VALORANT"));
        Assert.Equal("padrao", ProfileApps.Target(auto, profiles, "explorer"));
        Assert.Null(ProfileApps.Target(auto with { FallbackId = null }, profiles, "explorer"));
        Assert.Null(ProfileApps.Target(auto with { Enabled = false }, profiles, "valorant"));
    }

    [Fact]
    public void Profiles_ValidateTheirAppsTogether()
    {
        Assert.Throws<ArgumentException>(() =>
            ProfileState.ValidateAll([Profile("a", "x.exe"), Profile("b", "x.exe")], [], []));
    }

    // --- Regras de luz --------------------------------------------------------------------

    [Fact]
    public void Rules_TurnOffWhenLockedOrDisplayOff()
    {
        var rules = LightRulesState.Default with { OffWhenLocked = true, OffWhenDisplayOff = true };
        var noon = new TimeOnly(12, 0);

        Assert.Equal(LightOverride.Off, LightRules.Evaluate(rules, locked: true, displayOff: false, noon));
        Assert.Equal(LightOverride.Off, LightRules.Evaluate(rules, locked: false, displayOff: true, noon));
        Assert.Equal(LightOverride.None, LightRules.Evaluate(rules, locked: false, displayOff: false, noon));
        Assert.Equal(LightOverride.None, LightRules.Evaluate(LightRulesState.Default, true, true, noon));
    }

    [Theory]
    [InlineData(23, 30, true)]
    [InlineData(2, 0, true)]
    [InlineData(7, 0, false)]  // fim excluído
    [InlineData(12, 0, false)]
    public void Rules_NightWindowCrossesMidnight(int hour, int minute, bool inside)
    {
        var rules = LightRulesState.Default with { Night = true, NightAction = "dim" };

        var result = LightRules.Evaluate(rules, false, false, new TimeOnly(hour, minute));

        Assert.Equal(inside ? LightOverride.Dim : LightOverride.None, result);
    }

    [Fact]
    public void Rules_RejectBadTimes()
    {
        Assert.Throws<ArgumentException>(() => (LightRulesState.Default with { NightStart = "25:00" }).Validate());
        Assert.Throws<ArgumentException>(() => (LightRulesState.Default with { NightAction = "blink" }).Validate());
    }

    // --- Atalhos globais ------------------------------------------------------------------------

    [Fact]
    public void Hotkeys_NeedCtrlAltOrWinAndAKnownKey()
    {
        Hotkeys.ValidateAll([new("toggleLight", (int)(HotkeyModifiers.Control | HotkeyModifiers.Alt), "KeyL")]);

        Assert.Throws<ArgumentException>(() => Hotkeys.ValidateAll([new("toggleLight", (int)HotkeyModifiers.Shift, "KeyL")]));
        Assert.Throws<ArgumentException>(() => Hotkeys.ValidateAll([new("toggleLight", 2, "Fn")]));
        Assert.Throws<ArgumentException>(() => Hotkeys.ValidateAll([new("dance", 2, "KeyL")]));
        Assert.Throws<ArgumentException>(() => Hotkeys.ValidateAll([new("toggleLight", 2, "F13")])); // reservada
    }

    [Fact]
    public void Hotkeys_RejectTheSameComboTwice()
    {
        Assert.Throws<ArgumentException>(() => Hotkeys.ValidateAll(
        [
            new("toggleLight", 2, "KeyL"),
            new("nextEffect", 2, "KeyL"),
        ]));
    }

    [Theory]
    [InlineData("KeyL", 0x4C)]
    [InlineData("Digit0", 0x30)]
    [InlineData("F12", 0x7B)]
    [InlineData("F24", 0x87)]
    [InlineData("ArrowUp", 0x26)]
    public void Hotkeys_MapCodesToVirtualKeys(string code, int vk)
    {
        Assert.Equal(vk, Hotkeys.VirtualKey(code));
    }

    [Fact]
    public void Hotkeys_DescribeInPortuguese()
    {
        Assert.Equal("Ctrl + Alt + L", Hotkeys.Describe(new("toggleLight", 3, "KeyL")));
    }

    // --- Teclas com ação do app ---------------------------------------------------------------

    private static KeyAssignmentState App(int key, string kind, string? target = null, string layer = "normal") =>
        new(key, layer, KeyAssignmentState.AppAction, App: kind, Target: target);

    [Fact]
    public void AppKeys_BecomeAReservedShortcutInTheFirmwareTable()
    {
        var (normal, _) = KeyProfile.Build([App(56, AppKeyActions.Lock)], []);

        // Slot 0: Ctrl + Alt + Shift esquerdos e F13 (0x68), pelo formato 02 do oficial
        Assert.Equal([0x02, 0x07, 0x68, 0x00], normal.ToBuffer()[(56 * 4)..(56 * 4 + 4)]);
    }

    [Fact]
    public void AppKeys_AreAllocatedInAStableOrder()
    {
        var bindings = AppKeyActions.Allocate(
        [
            App(39, AppKeyActions.Sleep, layer: "fn"),
            App(40, AppKeyActions.Lock),
            App(38, AppKeyActions.Site, "https://exemplo.com"),
        ]);

        Assert.Equal([38, 40, 39], bindings.Select(b => b.Assignment.Key));
        Assert.Equal([0x68, 0x69, 0x6A], bindings.Select(b => b.Usage));
        Assert.Equal(0x7C, bindings[0].VirtualKey);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, bindings[0].HotkeyModifiers);
    }

    [Fact]
    public void AppKeys_ThirteenthBindingUsesTheNextModifierCombo()
    {
        var bindings = AppKeyActions.Allocate(Enumerable.Range(0, 13).Select(i => App(KeyboardLayout.All[i].Index, AppKeyActions.Lock)));

        Assert.Equal(KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt, bindings[12].Modifiers);
        Assert.Equal(0x68, bindings[12].Usage);
    }

    [Theory]
    [InlineData("site", "exemplo.com")]
    [InlineData("site", "file:///c:/x")]
    [InlineData("open", "")]
    [InlineData("text", null)]
    [InlineData("media", "disco")]
    [InlineData("hack", null)]
    public void AppKeys_RejectIncompleteActions(string kind, string? target)
    {
        Assert.Throws<ArgumentException>(() => KeyProfile.Build([App(56, kind, target)], []));
    }

    [Fact]
    public void AppKeys_RejectMoreThanTheReservedShortcuts()
    {
        var keys = KeyboardLayout.All.Where(k => k.Remappable).Take(AppKeyBinding.Capacity + 1)
            .Select(k => App(k.Index, AppKeyActions.Lock)).ToList();
        keys.AddRange(KeyboardLayout.All.Where(k => k.Remappable).Take(10).Select(k => App(k.Index, AppKeyActions.Lock, layer: "fn")));

        Assert.Throws<ArgumentException>(() => KeyProfile.Build(keys, []));
    }

    // --- Linha de comando ----------------------------------------------------------------------

    [Fact]
    public void Cli_ParsesEffectsInPortugueseOrEnglish()
    {
        Assert.Equal("rolling", CommandLine.Parse(["--efeito", "rolando"]).Effect);
        Assert.Equal("static", CommandLine.Parse(["--effect", "Estático"]).Effect);
        Assert.Equal("single-on", CommandLine.Parse(["--efeito", "acender ao tocar"]).Effect);
        Assert.Equal("breath", CommandLine.Parse(["--efeito", "respiracao"]).Effect);
    }

    [Fact]
    public void Cli_ParsesColorsAndRanges()
    {
        var request = CommandLine.Parse(["--cor", "azul", "--brilho", "3", "--velocidade", "0", "--arco-iris"]);

        Assert.Equal("#0040FF", request.Color);
        Assert.Equal(3, request.Brightness);
        Assert.Equal(0, request.Speed);
        Assert.True(request.Rainbow);
        Assert.True(request.ChangesLighting);
        Assert.Equal("#00FF7F", CommandLine.Parse(["--color", "00ff7f"]).Color);
    }

    [Fact]
    public void Cli_ParsesAlertsWithOrWithoutColor()
    {
        Assert.Equal("#FF0000", CommandLine.Parse(["--aviso"]).AlertColor);
        var green = CommandLine.Parse(["--aviso", "verde", "--vezes", "5"]);
        Assert.Equal("#00FF40", green.AlertColor);
        Assert.Equal(5, green.AlertTimes);
        Assert.Equal("#FF0000", CommandLine.Parse(["--aviso", "--hidden"]).AlertColor);
    }

    [Fact]
    public void Cli_ParsesProfilesLiveAndTimer()
    {
        var request = CommandLine.Parse(["--perfil", "Jogo", "--ao-vivo", "música", "--cronometro", "25"]);

        Assert.Equal("Jogo", request.Profile);
        Assert.Equal(LiveModes.Music, request.Live);
        Assert.Equal(25, request.TimerMinutes);
        Assert.True(CommandLine.Parse(["--timer", "parar"]).TimerStop);
    }

    [Fact]
    public void Cli_HiddenAloneIsNotAnAction()
    {
        var request = CommandLine.Parse(["--hidden"]);

        Assert.True(request.Hidden);
        Assert.False(request.HasActions);
        Assert.True(CommandLine.Parse(["--ligar"]).HasActions);
    }

    [Theory]
    [InlineData("--efeito", "disco")]
    [InlineData("--brilho", "9")]
    [InlineData("--cor", "furta-cor")]
    [InlineData("--voar")]
    [InlineData("--perfil")]
    [InlineData("--cronometro", "0")]
    [InlineData("--ao-vivo", "lava")]
    public void Cli_RejectsBadInput(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(args));
    }
}
