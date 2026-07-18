using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public sealed class FanChannelViewModelTests
{
    [Fact]
    public void Constructor_UsesFanIdWhenTelemetryIdIsNotReported()
    {
        var channel = new FanChannelViewModel(new FanInfo
        {
            FanId = 7,
            TelemetryId = -1,
            SensorId = 3,
            Name = "Rear fan",
            CurrentRpm = 1_250,
            Temperature = 43,
            MinRpm = 400,
            MaxRpm = 2_500
        });

        Assert.Equal(7, channel.TelemetryId);
        Assert.Equal(3, channel.SensorId);
        Assert.Equal("Rear fan", channel.Name);
        Assert.Equal(1_250, channel.CurrentRpm);
        Assert.Equal(43, channel.Temperature);
        Assert.Equal(50, channel.SpeedPercentage);
    }

    [Theory]
    [InlineData(null, 2_500, 0)]
    [InlineData(1_251, 2_500, 50)]
    [InlineData(2_600, 2_500, 100)]
    [InlineData(-10, 2_500, 0)]
    [InlineData(500, 0, 0)]
    public void SpeedPercentage_RoundsAndClampsTelemetry(int? rpm, int maximum, int expected)
    {
        var channel = new FanChannelViewModel(new FanInfo { CurrentRpm = rpm, MaxRpm = maximum });

        Assert.Equal(expected, channel.SpeedPercentage);
    }

    [Fact]
    public void UpdatingTelemetry_RaisesDependentPropertyNotifications()
    {
        var channel = new FanChannelViewModel(new FanInfo { CurrentRpm = 500, MaxRpm = 2_000 });
        var changed = new List<string?>();
        channel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        channel.CurrentRpm = 1_000;
        channel.Temperature = 48;

        Assert.Equal(50, channel.SpeedPercentage);
        Assert.Contains(nameof(FanChannelViewModel.CurrentRpm), changed);
        Assert.Contains(nameof(FanChannelViewModel.SpeedPercentage), changed);
        Assert.Contains(nameof(FanChannelViewModel.Temperature), changed);
    }
}
