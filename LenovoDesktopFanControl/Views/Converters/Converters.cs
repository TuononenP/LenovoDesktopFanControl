using System.Globalization;
using System.Windows.Data;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Views.Converters;

public class RpmToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int rpm)
            return string.Format(culture, "{0:N0} RPM", rpm);
        return "— RPM";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class TempToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int temp)
            return $"{temp} °C";
        return "— °C";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && !b
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class ModeToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int mode && parameter is int target)
            return mode == target;
        if (value is SmartFanMode modeEnum && parameter is SmartFanMode targetEnum)
            return modeEnum == targetEnum;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            if (parameter is SmartFanMode targetEnum)
                return targetEnum;
            if (parameter is int target)
                return (SmartFanMode)target;
        }
        return System.Windows.Data.Binding.DoNothing;
    }
}

public class WidthToColumnCountConverter : IValueConverter
{
    private const double MinimumCardWidth = 320;
    private const int MaximumColumns = 4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || width <= 0)
            return 1;

        return Math.Clamp((int)(width / MinimumCardWidth), 1, MaximumColumns);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
