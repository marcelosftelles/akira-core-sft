using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// "Redefinição de fábrica" oficial: fcn.00414870 (confirmação, textos 167/168)
// → tarefa 14 → fcn.00434ee0. Padrões de fcn.0042b280 + bloco de 0x575200 + rgb-keyboard.xml.
public class FactoryResetTests
{
    private sealed class RecordingDelay : IDelay
    {
        public List<TimeSpan> Waits { get; } = [];
        public void Wait(TimeSpan duration) => Waits.Add(duration);
    }

    [Fact]
    public void DefaultLightingIsRollingRainbowBrightness5Speed3()
    {
        var block = FactoryDefaults.Lighting.ToLightBlock();

        Assert.Equal(11, block[0]);                  // default_mode
        Assert.Equal([0xFF, 0x00, 0x00], block[1..4]); // color_value 0x0000FF = vermelho
        Assert.Equal(1, block[8]);                   // colorful
        Assert.Equal(5, block[9]);                   // default_brightness
        Assert.Equal(3, block[10]);                  // default_speed
        Assert.Equal(0, block[11]);                  // direção
        Assert.Equal([0xAA, 0x55], block[14..16]);
    }

    [Fact]
    public void DongleSendsFirmwareResetThenWaitsThenDefaultLighting()
    {
        var port = new RecordingReportPort();
        var delay = new RecordingDelay();

        var scope = new KeyboardClient(new DongleLink(port, delay)).RestoreFactoryDefaults();

        Assert.Equal(FactoryResetScope.DeviceAndLighting, scope);
        Assert.Equal(2, port.Written.Count);
        var reset = port.Written[0];
        Assert.Equal(33, reset.Length);
        Assert.Equal(0x0F, reset[1]);
        Assert.All(reset[2..32], b => Assert.Equal(0, b));
        Assert.Equal(0x0F, reset[32]);               // checksum = soma de [0..31]
        Assert.Contains(DongleProtocol.FactoryResetSettleDelay, delay.Waits);
        Assert.Equal([0x05, 0x10, 0x00, 11], port.Written[1][1..5]);
    }

    [Fact]
    public void CableHasNoResetCommandAndOnlyReappliesDefaultLighting()
    {
        var port = new RecordingFeaturePort();

        var scope = new KeyboardClient(new FeatureLink(port, new RecordingDelay())).RestoreFactoryDefaults();

        Assert.Equal(FactoryResetScope.LightingOnly, scope);
        Assert.Equal(
            ["000418", "000413", "000BFF", "000402", "0004F0"],
            port.Written.Select(r => Convert.ToHexString(r, 0, 3)));
    }

    [Fact]
    public void KeepsUserColorCorrection()
    {
        var port = new RecordingFeaturePort();

        new KeyboardClient(new FeatureLink(port, new RecordingDelay())).RestoreFactoryDefaults(swapRedBlue: true);

        Assert.Equal([0x00, 0x00, 0xFF], port.Written[2][2..5]);
    }
}
