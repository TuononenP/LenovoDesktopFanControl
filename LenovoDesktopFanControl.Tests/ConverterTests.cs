using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Views.Converters;

namespace LenovoDesktopFanControl.Tests;

public class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void TelemetryConverters_FormatValuesAndUnavailableReadings()
    {
        var rpm = new RpmToStringConverter();
        var temperature = new TempToStringConverter();

        Assert.Equal("1,234 RPM", rpm.Convert(1234, typeof(string), null, Culture));
        Assert.Equal("— RPM", rpm.Convert(null, typeof(string), null, Culture));
        Assert.Equal("— RPM", rpm.Convert("1234", typeof(string), null, Culture));
        Assert.Equal("42 °C", temperature.Convert(42, typeof(string), null, Culture));
        Assert.Equal("— °C", temperature.Convert(null, typeof(string), null, Culture));
    }

    [Fact]
    public void VisibilityConverters_HandleTrueFalseAndNonBooleanValues()
    {
        var normal = new BoolToVisibilityConverter();
        var inverse = new BoolToInverseVisibilityConverter();

        Assert.Equal(Visibility.Visible, normal.Convert(true, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, normal.Convert(false, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, normal.Convert(null, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, inverse.Convert(true, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Visible, inverse.Convert(false, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, inverse.Convert("false", typeof(Visibility), null, Culture));
    }

    [Fact]
    public void ModeConverter_HandlesIntegerAndEnumBindingsInBothDirections()
    {
        var converter = new ModeToBoolConverter();

        Assert.True((bool)converter.Convert(2, typeof(bool), 2, Culture));
        Assert.False((bool)converter.Convert(2, typeof(bool), 1, Culture));
        Assert.True((bool)converter.Convert(
            SmartFanMode.Custom, typeof(bool), SmartFanMode.Custom, Culture));
        Assert.False((bool)converter.Convert("Custom", typeof(bool), SmartFanMode.Custom, Culture));
        Assert.Equal(
            SmartFanMode.Quiet,
            converter.ConvertBack(true, typeof(SmartFanMode), SmartFanMode.Quiet, Culture));
        Assert.Equal(
            (SmartFanMode)2,
            converter.ConvertBack(true, typeof(SmartFanMode), 2, Culture));
        Assert.Same(
            Binding.DoNothing,
            converter.ConvertBack(false, typeof(SmartFanMode), SmartFanMode.Custom, Culture));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(319, 1)]
    [InlineData(640, 2)]
    [InlineData(1279, 3)]
    [InlineData(1280, 4)]
    [InlineData(5000, 4)]
    public void WidthConverter_ClampsResponsiveColumnCount(double width, int expected)
    {
        var converter = new WidthToColumnCountConverter();

        Assert.Equal(expected, converter.Convert(width, typeof(int), null, Culture));
    }

    [Fact]
    public void WidthConverter_ReturnsOneForNonDoubleValues()
    {
        Assert.Equal(
            1,
            new WidthToColumnCountConverter().Convert("640", typeof(int), null, Culture));
    }

    [Fact]
    public void LocFormatConverter_FormatsResourceOrReturnsOriginalValue()
    {
        var converter = new LocFormatConverter();

        Assert.Contains(
            "2",
            (string)converter.Convert(2, typeof(string), "MsgConnected", Culture));
        Assert.Equal("plain", converter.Convert("plain", typeof(string), null, Culture));
        Assert.Equal("", converter.Convert(null, typeof(string), null, Culture));
    }

    [Fact]
    public void UnsupportedConvertBackMethods_Throw()
    {
        Assert.Throws<NotSupportedException>(() =>
            new RpmToStringConverter().ConvertBack(null, typeof(int), null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new TempToStringConverter().ConvertBack(null, typeof(int), null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new BoolToVisibilityConverter().ConvertBack(null, typeof(bool), null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new BoolToInverseVisibilityConverter().ConvertBack(null, typeof(bool), null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new WidthToColumnCountConverter().ConvertBack(null, typeof(double), null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new LocFormatConverter().ConvertBack(null, typeof(string), null, Culture));
    }
}