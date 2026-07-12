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
        var result = new List<string> { "en" };
        try
        {
            var asm = typeof(LocalizationService).Assembly;
            var rm = new System.Resources.ResourceManager(
                "LenovoDesktopFanControl.Resources.Strings", asm);
            var cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            foreach (var ci in cultures)
            {
                if (ci.Equals(CultureInfo.InvariantCulture)) continue;
                try
                {
                    var rs = rm.GetResourceSet(ci, true, false);
                    if (rs != null)
                        result.Add(ci.Name);
                }
                catch { }
            }
        }
        catch { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}