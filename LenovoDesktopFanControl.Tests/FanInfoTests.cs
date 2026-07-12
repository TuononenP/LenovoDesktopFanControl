using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Tests;

public class FanInfoTests
{
    [Fact]
    public void Defaults_AreSetToSafeValues()
    {
        var info = new FanInfo();

        Assert.Equal(0, info.FanId);
        Assert.Equal(-1, info.TelemetryId);
        Assert.Equal(0, info.SensorId);
        Assert.Equal("", info.Name);
        Assert.Null(info.NameResourceKey);
        Assert.Null(info.NameResourceArgument);
        Assert.Null(info.CurrentRpm);
        Assert.Null(info.Temperature);
        Assert.False(info.IsAvailable);
        Assert.Equal(2500, info.MaxRpm);
        Assert.Equal(0, info.MinRpm);
    }

    [Fact]
    public void InitOnlyProperties_CannotBeMutatedAfterConstruction()
    {
        var info = new FanInfo
        {
            FanId = 7,
            TelemetryId = 16,
            SensorId = 3,
            Name = "CPU fan",
            NameResourceKey = "FanNameCpu",
            MaxRpm = 3000
        };

        Assert.Equal(7, info.FanId);
        Assert.Equal(16, info.TelemetryId);
        Assert.Equal(3, info.SensorId);
        Assert.Equal("CPU fan", info.Name);
        Assert.Equal("FanNameCpu", info.NameResourceKey);
        Assert.Equal(3000, info.MaxRpm);
    }

    [Fact]
    public void MutableProperties_CanBeUpdatedAfterConstruction()
    {
        var info = new FanInfo();

        info.CurrentRpm = 1500;
        info.Temperature = 42;
        info.IsAvailable = true;
        info.MaxRpm = 2800;
        info.MinRpm = 200;

        Assert.Equal(1500, info.CurrentRpm);
        Assert.Equal(42, info.Temperature);
        Assert.True(info.IsAvailable);
        Assert.Equal(2800, info.MaxRpm);
        Assert.Equal(200, info.MinRpm);
    }
}

public class LanguageInfoTests
{
    [Fact]
    public void Defaults_AreEmptyStrings()
    {
        var info = new LanguageInfo();

        Assert.Equal("", info.Code);
        Assert.Equal("", info.DisplayName);
    }

    [Fact]
    public void InitOnlyProperties_StoreValues()
    {
        var info = new LanguageInfo { Code = "fi-FI", DisplayName = "Suomi" };

        Assert.Equal("fi-FI", info.Code);
        Assert.Equal("Suomi", info.DisplayName);
    }

    [Fact]
    public void ToString_ReturnsDisplayName()
    {
        var info = new LanguageInfo { Code = "en", DisplayName = "English" };

        Assert.Equal("English", info.ToString());
    }
}