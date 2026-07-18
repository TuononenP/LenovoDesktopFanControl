using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;

namespace LenovoDesktopFanControl.Services;

public sealed class Loc : INotifyPropertyChanged
{
    public static readonly Loc Instance = new();

    public string this[string key] => LocalizationService.Get(key);

    public string this[string key, params object[] args] => LocalizationService.Get(key, args);

    public static void SetCulture(CultureInfo culture)
    {
        LocalizationService.CurrentCulture = culture;
        Instance.RaiseAllPropertiesChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaiseAllPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

public static class LocalizationService
{
    // Keep this explicit rather than probing ResourceManager. ResourceManager can
    // resolve Simplified Chinese through its parent culture, which made zh-CN
    // invisible to the former resource-set discovery loop.
    private static readonly string[] SupportedCultureCodes =
    [
        "en",
        "fi-FI",
        "zh-CN",
        "fr-FR",
        "de-DE",
        "es-ES",
        "ja-JP",
        "ko-KR"
    ];

    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    public static CultureInfo CurrentCulture
    {
        get => _culture;
        set
        {
            if (_culture.Equals(value)) return;
            _culture = value;
            Thread.CurrentThread.CurrentUICulture = value;
            Thread.CurrentThread.CurrentCulture = value;
        }
    }

    public static void SetLanguage(string cultureCode)
    {
        var ci = string.IsNullOrEmpty(cultureCode) || cultureCode == "en"
            ? CultureInfo.InvariantCulture
            : new CultureInfo(cultureCode);
        CurrentCulture = ci;
    }

    /// <summary>
    /// Resolves an application language from a saved choice or the Windows UI
    /// language. A saved, supported choice always wins. Unsupported Windows
    /// languages fall back to English.
    /// </summary>
    internal static string ResolveLanguage(string? savedLanguage, CultureInfo? operatingSystemCulture = null)
    {
        var saved = FindSupportedCulture(savedLanguage);
        if (saved is not null)
            return saved;

        // CurrentUICulture reflects the signed-in user's Windows display
        // language. InstalledUICulture instead reports the original OS install
        // language and can differ after a user changes their display language.
        var systemCulture = operatingSystemCulture ?? CultureInfo.CurrentUICulture;
        var exact = FindSupportedCulture(systemCulture.Name);
        if (exact is not null)
            return exact;

        var languageMatch = SupportedCultureCodes.FirstOrDefault(cultureCode =>
            !string.Equals(cultureCode, "en", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                CultureInfo.GetCultureInfo(cultureCode).TwoLetterISOLanguageName,
                systemCulture.TwoLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase));

        return languageMatch ?? "en";
    }

    public static string Get(string key)
    {
        var rm = new System.Resources.ResourceManager(
            "LenovoDesktopFanControl.Resources.Strings",
            typeof(LocalizationService).Assembly);
        return rm.GetString(key, _culture) ?? key;
    }

    public static string Get(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(
                _culture.Equals(CultureInfo.InvariantCulture) ? CultureInfo.InvariantCulture : _culture,
                template, args);
        }
        catch
        {
            return template;
        }
    }

    public static List<string> GetAvailableCultures()
    {
        return SupportedCultureCodes.ToList();
    }

    private static string? FindSupportedCulture(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
            return null;

        return SupportedCultureCodes.FirstOrDefault(candidate =>
            string.Equals(candidate, cultureCode, StringComparison.OrdinalIgnoreCase));
    }
}
