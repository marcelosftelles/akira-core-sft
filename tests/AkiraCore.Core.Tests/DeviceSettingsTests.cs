using AkiraCore.Core.Device;
using AkiraCore.Core.Links;
using AkiraCore.Core.Presentation;

namespace AkiraCore.Core.Tests;

// Configurações gerais: fcn.004141a0 (cabo) e fcn.00414410 (dongle).
public class DeviceSettingsTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private static readonly DeviceSettings Sample =
        new(DisableWinKey: true, DisableAltF4: false, DisableAltTab: true, FnMode.Toggle, SleepTimeout.FiveMinutes, KeyResponse: 3);

    [Fact]
    public void Block_PlacesEachSettingAtOfficialOffset()
    {
        // 0x414238: word 0x0100; [2] Alt+Tab (+678h), [3] Alt+F4 (+67Ch), [4] Win (+680h),
        // [5] fn_switch, [6] sleep_time, [8] key_respondtime
        Assert.Equal([0x00, 0x01, 0x01, 0x00, 0x01, 0x01, 0x02, 0x00, 0x03], Sample.ToBlock()[..9]);
        Assert.Equal([0xAA, 0x55], Sample.ToBlock()[14..16]);
    }

    [Fact]
    public void Cable_SendsBeginSeventeenDataAndEndWithoutFinalize()
    {
        var port = new RecordingFeaturePort();

        new KeyboardClient(new FeatureLink(port, new NoDelay())).ApplyDeviceSettings(Sample);

        Assert.Equal(4, port.Written.Count);
        Assert.Equal("000418", Convert.ToHexString(port.Written[0], 0, 3));
        Assert.Equal("00041701", Convert.ToHexString(port.Written[1], 0, 4)); // [2] = 1
        Assert.Equal(1, port.Written[1][9]);                                     // [8] = 1
        Assert.Equal([0x00, 0x01, 0x01, 0x00, 0x01], port.Written[2][1..6]);
        Assert.Equal(new byte[2], port.Written[2][15..17]);                      // sem AA 55 no 14
        Assert.Equal([0xAA, 0x55], port.Written[2][63..65]);                     // AA 55 no 62
        Assert.Equal("000402", Convert.ToHexString(port.Written[3], 0, 3));
    }

    [Fact]
    public void Dongle_SendsSingleReportWithSevenTenHeader()
    {
        var port = new RecordingReportPort();

        new KeyboardClient(new DongleLink(port, new NoDelay())).ApplyDeviceSettings(Sample);

        var report = Assert.Single(port.Written);
        // 0x41445c: dword 0x00001007 em [1..4], [5] = 1, campos a partir de [6], AA 55 em [18]
        Assert.Equal([0x07, 0x10, 0x00, 0x00, 0x01, 0x01, 0x00, 0x01, 0x01, 0x02, 0x00, 0x03], report[1..13]);
        Assert.Equal([0xAA, 0x55], report[18..20]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Block_RejectsKeyResponseOutOfRange(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Sample with { KeyResponse = level }).ToBlock());
    }

    [Fact]
    public void State_RoundTrips()
    {
        var state = DeviceSettingsState.From(Sample);

        Assert.Equal("5m", state.Sleep);
        Assert.Equal(Sample, state.ToSettings());
        Assert.Throws<ArgumentException>(() => (state with { Sleep = "2h" }).ToSettings());
    }
}
