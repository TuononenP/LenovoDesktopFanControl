using LenovoDesktopFanControl.Views.Controls;

namespace LenovoDesktopFanControl.Tests;

public sealed class TemperatureChartScaleTests
{
    [Theory]
    [InlineData(100, 12)]
    [InlineData(50, 112)]
    [InlineData(0, 212)]
    [InlineData(-10, 212)]
    [InlineData(110, 12)]
    public void GetY_UsesFixedZeroToOneHundredScale(int celsius, double expected)
    {
        var actual = TemperatureChartScale.GetY(celsius, top: 12, height: 200);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FormatTimeLabel_OmitsSeconds()
    {
        var timestamp = new DateTime(2026, 7, 18, 13, 47, 36, DateTimeKind.Local);

        var label = TemperatureChartScale.FormatTimeLabel(timestamp);

        Assert.Equal("13:47", label);
        Assert.DoesNotContain("36", label);
    }
}
