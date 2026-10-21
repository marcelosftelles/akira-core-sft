using AkiraCore.Core.Keys;
using AkiraCore.Core.Macros;

namespace AkiraCore.Core.Tests;

public class TextMacroTests
{
    // Pedaço de um layout parecido com o ABNT2: ´ e ~ são teclas de acento, @ usa Shift,
    // / usa AltGr (como no ABNT2 sem a tecla /?), ç tem tecla própria
    private sealed class FakeLayout : ITextLayout
    {
        public KeyStroke? Find(char c) => c switch
        {
            >= 'a' and <= 'z' => new KeyStroke((byte)(0x04 + (c - 'a'))),
            >= 'A' and <= 'Z' => new KeyStroke((byte)(0x04 + (c - 'A')), Shift: true),
            ' ' => new KeyStroke(0x2C),
            '@' => new KeyStroke(0x1F, Shift: true),
            '/' => new KeyStroke(0x14, AltGr: true),
            'ç' => new KeyStroke(0x33),
            '´' => new KeyStroke(0x2F, DeadKey: true),
            '~' => new KeyStroke(0x34, DeadKey: true),
            _ => null,
        };
    }

    private static string Describe(TextMacroResult result) =>
        string.Join(" ", result.Events.Select(e => $"{(e.Kind == MacroEventKind.KeyDown ? "+" : "-")}{e.Code:X2}"));

    [Fact]
    public void PlainLetter_PressesAndReleases()
    {
        var result = TextMacro.Build("a", new FakeLayout(), 10);

        Assert.Equal("+04 -04", Describe(result));
        Assert.All(result.Events, e => Assert.Equal(10, e.DelayMs));
    }

    [Fact]
    public void Uppercase_HoldsShiftAroundTheKey()
    {
        Assert.Equal("+E1 +04 -04 -E1", Describe(TextMacro.Build("A", new FakeLayout(), 10)));
    }

    [Fact]
    public void AltGr_UsesRightAlt()
    {
        Assert.Equal("+E6 +14 -14 -E6", Describe(TextMacro.Build("/", new FakeLayout(), 10)));
    }

    [Fact]
    public void AccentedLetter_UsesTheDeadKeyThenTheLetter()
    {
        // á = ´ + a ; ã = ~ + a
        Assert.Equal("+2F -2F +04 -04 +34 -34 +04 -04", Describe(TextMacro.Build("áã", new FakeLayout(), 10)));
    }

    [Fact]
    public void LoneDeadKey_IsFollowedBySpace()
    {
        Assert.Equal("+34 -34 +2C -2C", Describe(TextMacro.Build("~", new FakeLayout(), 10)));
    }

    [Fact]
    public void NewLineAndTab_BecomeEnterAndTab()
    {
        Assert.Equal("+28 -28 +2B -2B", Describe(TextMacro.Build("\r\n\t", new FakeLayout(), 10)));
    }

    [Fact]
    public void UnknownCharacters_AreSkippedOnce()
    {
        var result = TextMacro.Build("a€b€", new FakeLayout(), 10);

        Assert.Equal("+04 -04 +05 -05", Describe(result));
        Assert.Equal("€", result.Skipped);
    }

    [Fact]
    public void AccentWithoutDeadKey_IsSkipped()
    {
        // ü pediria ¨, que este layout não tem
        Assert.Equal("ü", TextMacro.Build("ü", new FakeLayout(), 10).Skipped);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyText_IsRejected(string? text)
    {
        if (text is null)
        {
            Assert.Throws<ArgumentNullException>(() => TextMacro.Build(text!, new FakeLayout(), 10));
            return;
        }
        Assert.Throws<ArgumentException>(() => TextMacro.Build(text, new FakeLayout(), 10));
    }

    [Fact]
    public void TooLongText_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            TextMacro.Build(new string('a', TextMacro.MaxLength + 1), new FakeLayout(), 10));
    }

    [Theory]
    [InlineData(0x1E, 0x04)]   // A
    [InlineData(0x10, 0x14)]   // Q
    [InlineData(0x2C, 0x1D)]   // Z
    [InlineData(0x02, 0x1E)]   // 1
    [InlineData(0x0B, 0x27)]   // 0
    [InlineData(0x56, 0x64)]   // \| do ABNT2
    [InlineData(0x73, 0x87)]   // /? do ABNT2
    [InlineData(0xE038, 0xE6)] // Alt direito (AltGr)
    [InlineData(0x57, 0x44)]   // F11
    public void ScanCodes_MapToHidUsage(int scan, int usage)
    {
        Assert.Equal((byte)usage, ScanCodes.ToUsage(scan));
    }

    [Fact]
    public void ScanCodes_OnlyProduceKeysTheMacroAccepts()
    {
        Assert.All(ScanCodes.All.Values, usage => Assert.NotNull(HidKeys.ByUsage(usage)));
    }
}
