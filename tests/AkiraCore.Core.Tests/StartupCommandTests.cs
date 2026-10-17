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
}
