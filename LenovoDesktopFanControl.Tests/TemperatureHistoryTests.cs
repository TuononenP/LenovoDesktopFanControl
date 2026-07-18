using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Tests;

public sealed class TemperatureHistoryTests
{
    [Fact]
    public void Compact_KeepsDetailedHour_CompactsOlderReadings_AndDropsExpiredData()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var history = new TemperatureHistory
        {
            Samples =
            [
                new(now.AddHours(-13), 20),
                new(now.AddHours(-2).AddMinutes(1), 30),
                new(now.AddHours(-2).AddMinutes(3), 34),
                new(now.AddMinutes(-30), 40),
                new(now.AddMinutes(-29), 42)
            ]
        };

        history.Compact(now);

        Assert.Equal(3, history.Samples.Count);
        Assert.DoesNotContain(history.Samples, sample => sample.Celsius == 20);
        Assert.Contains(history.Samples, sample => sample.Celsius == 32);
        Assert.Contains(history.Samples, sample => sample.Celsius == 40);
        Assert.Contains(history.Samples, sample => sample.Celsius == 42);
    }
}
