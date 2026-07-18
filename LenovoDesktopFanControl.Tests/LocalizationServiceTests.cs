using System.Globalization;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public class LocalizationServiceTests : IDisposable
{
    public LocalizationServiceTests()
    {
        LocalizationService.SetLanguage("en");
    }

    [Fact]
    public void Get_ReturnsEnglishResourceAndFallsBackToUnknownKey()
    {
        Assert.NotEqual("MsgInitializing", LocalizationService.Get("MsgInitializing"));
        Assert.Equal("MissingResourceKey", LocalizationService.Get("MissingResourceKey"));
    }

    [Fact]
    public void Get_FormatsArgumentsAndReturnsTemplateWhenArgumentsAreInvalid()
    {
        var formatted = LocalizationService.Get("MsgConnected", 3);
        var invalid = LocalizationService.Get("MsgConnected");

        Assert.Contains("3", formatted);
        Assert.Equal(LocalizationService.Get("MsgConnected"), invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("en")]
    public void SetLanguage_UsesInvariantCultureForEnglish(string language)
    {
        LocalizationService.SetLanguage(language);

        Assert.Equal(CultureInfo.InvariantCulture, LocalizationService.CurrentCulture);
    }

    [Fact]
    public void SetLanguage_SelectsRequestedSatelliteCulture()
    {
        LocalizationService.SetLanguage("fi-FI");

        Assert.Equal("fi-FI", LocalizationService.CurrentCulture.Name);
        Assert.NotEqual(
            LocalizationService.Get("MsgInitializing"),
            new System.Resources.ResourceManager(
                    "LenovoDesktopFanControl.Resources.Strings",
                    typeof(LocalizationService).Assembly)
                .GetString("MsgInitializing", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TemperatureMonitoringResources_AreLocalizedInFinnish()
    {
        LocalizationService.SetLanguage("fi-FI");

        Assert.Equal("Järjestelmän lämpötilat", LocalizationService.Get("SystemTemperatures"));
        Assert.Equal("GPU · Lämpötilahistoria", LocalizationService.Get("TemperatureHistoryTitle", "GPU"));
        Assert.Equal("NVIDIA-telemetria ei ole käytettävissä", LocalizationService.Get("DetailNvidiaTelemetryUnavailable"));
    }

    [Fact]
    public void LocSetCulture_RaisesIndexerNotification()
    {
        string? propertyName = null;
        void Handler(object? _, System.ComponentModel.PropertyChangedEventArgs args) =>
            propertyName = args.PropertyName;

        Loc.Instance.PropertyChanged += Handler;
        try
        {
            Loc.SetCulture(CultureInfo.GetCultureInfo("fi-FI"));
        }
        finally
        {
            Loc.Instance.PropertyChanged -= Handler;
        }

        Assert.Equal("Item[]", propertyName);
    }

    [Fact]
    public void GetAvailableCultures_ContainsEnglishAndFinnishWithoutDuplicates()
    {
        var cultures = LocalizationService.GetAvailableCultures();

        Assert.Contains("en", cultures);
        Assert.Contains("fi-FI", cultures);
        Assert.Equal(cultures.Count, cultures.Distinct().Count());
    }

    public void Dispose()
    {
        LocalizationService.SetLanguage("en");
        Loc.SetCulture(CultureInfo.InvariantCulture);
    }
}
