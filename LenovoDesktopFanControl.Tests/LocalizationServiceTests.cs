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

    [Theory]
    [InlineData("fi-FI", "en-US", "fi-FI")]
    [InlineData("FI-fi", "en-US", "fi-FI")]
    [InlineData(null, "de-DE", "de-DE")]
    [InlineData(null, "fr-CA", "fr-FR")]
    [InlineData(null, "ja-JP", "ja-JP")]
    [InlineData(null, "it-IT", "en")]
    [InlineData("unsupported", "ko-KR", "ko-KR")]
    public void ResolveLanguage_UsesSavedLanguageOrSupportedWindowsLanguage(
        string? savedLanguage,
        string windowsLanguage,
        string expectedLanguage)
    {
        var result = LocalizationService.ResolveLanguage(
            savedLanguage,
            CultureInfo.GetCultureInfo(windowsLanguage));

        Assert.Equal(expectedLanguage, result);
    }

    [Fact]
    public void ResolveLanguage_UsesCurrentWindowsDisplayLanguageWhenNoLanguageIsSaved()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CA");

            Assert.Equal("fr-FR", LocalizationService.ResolveLanguage(null));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("fi-FI", "Alustetaan...")]
    [InlineData("zh-CN", "正在初始化...")]
    [InlineData("fr-FR", "Initialisation...")]
    [InlineData("de-DE", "Initialisierung...")]
    [InlineData("es-ES", "Inicializando...")]
    [InlineData("ja-JP", "初期化中...")]
    [InlineData("ko-KR", "초기화 중...")]
    public void SetLanguage_SelectsRequestedSatelliteCulture(string language, string expectedInitializingMessage)
    {
        LocalizationService.SetLanguage(language);

        Assert.Equal(language, LocalizationService.CurrentCulture.Name);
        Assert.Equal(expectedInitializingMessage, LocalizationService.Get("MsgInitializing"));
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
    public void GetAvailableCultures_ContainsEverySupportedCultureWithoutDuplicates()
    {
        var cultures = LocalizationService.GetAvailableCultures();

        Assert.Contains("en", cultures);
        Assert.Contains("fi-FI", cultures);
        Assert.Contains("zh-CN", cultures);
        Assert.Contains("fr-FR", cultures);
        Assert.Contains("de-DE", cultures);
        Assert.Contains("es-ES", cultures);
        Assert.Contains("ja-JP", cultures);
        Assert.Contains("ko-KR", cultures);
        Assert.Equal(cultures.Count, cultures.Distinct().Count());
    }

    [Theory]
    [InlineData("fi-FI")]
    [InlineData("zh-CN")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("ja-JP")]
    [InlineData("ko-KR")]
    public void SatelliteResources_ContainEveryNeutralResourceKey(string culture)
    {
        var applicationAssembly = typeof(LocalizationService).Assembly;
        var neutralKeys = ReadResourceKeys(applicationAssembly).Order(StringComparer.Ordinal).ToArray();
        var satellitePath = Path.Combine(
            AppContext.BaseDirectory,
            culture,
            "LenovoDesktopFanControl.resources.dll");
        Assert.True(File.Exists(satellitePath), $"Missing satellite assembly for {culture}");

        var satelliteAssembly = System.Reflection.Assembly.LoadFrom(satellitePath);
        var satelliteKeys = ReadResourceKeys(satelliteAssembly).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(neutralKeys, satelliteKeys);
    }

    private static IEnumerable<string> ReadResourceKeys(System.Reflection.Assembly assembly)
    {
        var resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.StartsWith("LenovoDesktopFanControl.Resources.Strings", StringComparison.Ordinal) &&
            name.EndsWith(".resources", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var reader = new System.Resources.ResourceReader(stream);
        var enumerator = reader.GetEnumerator();
        while (enumerator.MoveNext())
            yield return (string)enumerator.Key;
    }

    public void Dispose()
    {
        LocalizationService.SetLanguage("en");
        Loc.SetCulture(CultureInfo.InvariantCulture);
    }
}
