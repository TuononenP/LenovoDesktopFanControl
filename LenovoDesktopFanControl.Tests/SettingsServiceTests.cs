using System.IO;
using System.Text.Json;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _originalLocalAppData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);

    public void Dispose()
    {
        var testDir = Path.Combine(_originalLocalAppData, "LenovoDesktopFanControl");
        if (Directory.Exists(testDir))
        {
            try
            {
                var settingsFile = Path.Combine(testDir, "settings.json");
                if (File.Exists(settingsFile))
                    File.Delete(settingsFile);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_ReturnsDefaultSettingsWhenFileDoesNotExist()
    {
        var service = new SettingsService();
        var settings = service.Load();

        Assert.Equal(SmartFanMode.Balanced, settings.Mode);
        Assert.Null(settings.GlobalFanCurve);
        Assert.Empty(settings.FanCurves);
        Assert.Equal(2000, settings.PollingIntervalMs);
    }

    [Fact]
    public void Save_PersistsSettingsAsJsonFile()
    {
        var service = new SettingsService();
        var settings = new FanSettings
        {
            Mode = SmartFanMode.Custom,
            MinimizeToTray = true,
            GlobalFanCurve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7],
            Language = "fi-FI"
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(SmartFanMode.Custom, loaded.Mode);
        Assert.True(loaded.MinimizeToTray);
        Assert.Equal(settings.GlobalFanCurve, loaded.GlobalFanCurve);
        Assert.Equal("fi-FI", loaded.Language);
    }

    [Fact]
    public void Save_PersistsEnumValuesAsStrings()
    {
        var service = new SettingsService();
        var settings = new FanSettings { Mode = SmartFanMode.Performance };

        service.Save(settings);

        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LenovoDesktopFanControl");
        var settingsFile = Path.Combine(settingsDir, "settings.json");
        var json = File.ReadAllText(settingsFile);

        Assert.Contains("\"Performance\"", json);
    }

    [Fact]
    public void Load_ReturnsDefaultSettingsForCorruptFile()
    {
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LenovoDesktopFanControl");
        Directory.CreateDirectory(settingsDir);
        var settingsFile = Path.Combine(settingsDir, "settings.json");
        File.WriteAllText(settingsFile, "{ this is not valid json");

        var service = new SettingsService();
        var settings = service.Load();

        Assert.Equal(SmartFanMode.Balanced, settings.Mode);
    }
}