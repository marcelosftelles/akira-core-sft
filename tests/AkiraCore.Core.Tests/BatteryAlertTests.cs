using AkiraCore.Core.Device;

namespace AkiraCore.Core.Tests;

public class BatteryAlertTests
{
    [Fact]
    public void WarnsOnceWhenReachingTheLimit()
    {
        var alert = new BatteryAlert();

        Assert.False(alert.Update(21, charging: false));
        Assert.True(alert.Update(20, charging: false));
        Assert.False(alert.Update(19, charging: false));
        Assert.False(alert.Update(21, charging: false)); // oscilação perto do limite não rearma
        Assert.False(alert.Update(20, charging: false));
    }

    [Fact]
    public void RearmsAfterChargingAboveTheMargin()
    {
        var alert = new BatteryAlert();
        alert.Update(15, charging: false);

        Assert.False(alert.Update(30, charging: false));
        Assert.True(alert.Update(18, charging: false));
    }

    [Fact]
    public void CableRearmsAndNeverWarnsWhileCharging()
    {
        var alert = new BatteryAlert();
        alert.Update(10, charging: false);

        Assert.False(alert.Update(10, charging: true));
        Assert.True(alert.Update(10, charging: false));
    }

    [Fact]
    public void IgnoresMissingReadings()
    {
        Assert.False(new BatteryAlert().Update(null, charging: false));
    }
}
