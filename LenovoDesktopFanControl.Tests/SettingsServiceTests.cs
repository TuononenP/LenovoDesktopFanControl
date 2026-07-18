using System.IO;
using System.Text.Json;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "LenovoDesktopFanControl.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _service = new SettingsService(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_ReturnsDefaultSettingsWhenFileDoesNotExist()
    {
        var settings = _service.Load();

        Assert.Equal(SmartFanMode.Balanced, settings.Mode);
        Assert.Null(settings.GlobalFanCurve);
        Assert.Empty(settings.FanCurves);
        Assert.Equal(2000, settings.PollingIntervalMs);
    }

    [Fact]
    public void Save_PersistsSettingsAsJsonFile()
    {
        var settings = new FanSettings
        {
            Mode = SmartFanMode.Custom,
            MinimizeToTray = true,
            GlobalFanCurve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7],
            Language = "fi-FI",
            LightingZoneNames =
            {
                [2] = "Desk glow"
            },
            LightingZoneBrightness =
            {
                [2] = 35
            }
        };

        _service.Save(settings);
        var loaded = _service.Load();

        Assert.Equal(SmartFanMode.Custom, loaded.Mode);
        Assert.True(loaded.MinimizeToTray);
        Assert.Equal(settings.GlobalFanCurve, loaded.GlobalFanCurve);
        Assert.Equal("fi-FI", loaded.Language);
        Assert.Equal("Desk glow", loaded.LightingZoneNames[2]);
        Assert.Equal(35, loaded.LightingZoneBrightness[2]);
    }

    [Fact]
    public void Save_PersistsEnumValuesAsStrings()
    {
        var settings = new FanSettings { Mode = SmartFanMode.Performance };

        _service.Save(settings);

        var settingsFile = Path.Combine(_testDirectory, "settings.json");
        var json = File.ReadAllText(settingsFile);

        Assert.Contains("\"Performance\"", json);
    }

    [Fact]
    public void Load_ReturnsDefaultSettingsForCorruptFile()
    {
        Directory.CreateDirectory(_testDirectory);
        var settingsFile = Path.Combine(_testDirectory, "settings.json");
        File.WriteAllText(settingsFile, "{ this is not valid json");

        var settings = _service.Load();

        Assert.Equal(SmartFanMode.Balanced, settings.Mode);
    }
}
