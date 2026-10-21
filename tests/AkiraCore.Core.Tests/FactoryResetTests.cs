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
    public void CableHasNoResetCommandAndResendsDefaultsInOfficialOrder()
    {
        var port = new RecordingFeaturePort();

        var scope = new KeyboardClient(new FeatureLink(port, new RecordingDelay())).RestoreFactoryDefaults();

        Assert.Equal(FactoryResetScope.Defaults, scope);
        // fcn.00434ee0: macros (0x43505e) → teclas normal e Fn (0x435070/0x435082)
        // → iluminação (0x4350ba) → configurações (0x4350d4)
        var commands = port.Written
            .Select(r => Convert.ToHexString(r, 0, 3))
            .Where(h => h.StartsWith("0004"))
            .ToList();
        Assert.Equal(
            ["000419", "000415", "000402",
             "000418", "000411", "000402", "0004F0",
             "000418", "000427", "000402", "0004F0",
             "000418", "000413", "000402", "0004F0",
             "000418", "000417", "000402"],
            commands);
        // Macros vazias: só os cabeçalhos zerados (400 bytes → 8 pacotes)
        Assert.Equal(8, port.Written[1][9]);
        // Configurações zeradas: só o 01 fixo e a assinatura
        var settings = port.Written[^2];
        Assert.Equal([0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], settings[1..10]);
    }

    [Fact]
    public void KeepsUserColorCorrection()
    {
        var port = new RecordingFeaturePort();

        new KeyboardClient(new FeatureLink(port, new RecordingDelay())).RestoreFactoryDefaults(swapRedBlue: true);

        var lightingData = port.Written.FindIndex(r => r[1] == 0x04 && r[2] == 0x13) + 1;
        Assert.Equal([0x00, 0x00, 0xFF], port.Written[lightingData][2..5]);
    }
}
