using AkiraCore.Core.Startup;

namespace AkiraCore.Core.Tests;

public class StartupCommandTests
{
    private const string Exe = @"C:\Users\Marcelo\AppData\Local\Programs\AkiraCore\AkiraCore.exe";

    [Fact]
    public void QuotesPathAndStartsHidden() =>
        Assert.Equal($"\"{Exe}\" --hidden", StartupCommand.For(Exe));

    [Fact]
    public void RecognizesItsOwnEntryIgnoringCaseAndSpaces() =>
        Assert.True(StartupCommand.PointsTo($"  \"{Exe.ToUpperInvariant()}\" --hidden ", Exe));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"""C:\Antigo\AkiraCore.exe"" --hidden")]
    [InlineData(@"""C:\Users\Marcelo\AppData\Local\Programs\AkiraCore\AkiraCore.exe""")]
    public void OtherEntriesCountAsOff(string? value) =>
        Assert.False(StartupCommand.PointsTo(value, Exe));

    [Fact]
    public void RejectsEmptyPath() =>
        Assert.ThrowsAny<ArgumentException>(() => StartupCommand.For(" "));

    [Theory]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x01 }, true)]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x06 }, false)]
    [InlineData(new byte[0], false)]
    public void StartupApprovedOddFirstByteMeansDisabled(byte[] value, bool disabled) =>
        Assert.Equal(disabled, StartupCommand.IsDisabledByUser(value));

    [Fact]
    public void MissingStartupApprovedMeansEnabled() =>
        Assert.False(StartupCommand.IsDisabledByUser(null));
}
