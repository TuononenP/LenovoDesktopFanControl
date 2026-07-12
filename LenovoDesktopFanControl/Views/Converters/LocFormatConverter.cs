using System.Globalization;
using System.Windows.Data;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Views.Converters;

public class LocFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string key)
            return LocalizationService.Get(key, value ?? "");
        return value ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}