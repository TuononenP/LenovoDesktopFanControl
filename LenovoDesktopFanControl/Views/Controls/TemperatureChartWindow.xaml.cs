using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shapes;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;
using MediaColor = System.Windows.Media.Color;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfPoint = System.Windows.Point;

namespace LenovoDesktopFanControl.Views.Controls;

public partial class TemperatureChartWindow : Window
{
    private readonly IReadOnlyList<TemperatureHistorySample> _samples;

    public string SourceName { get; }
    public string CurrentLabel { get; }
    public string LowLabel { get; }
    public string HighLabel { get; }

    public TemperatureChartWindow(FanViewModel fan)
        : this(fan.FanName, fan.Temperature, fan.TemperatureHistory)
    {
    }

    public TemperatureChartWindow(SystemTemperatureViewModel temperature)
        : this(temperature.Name, temperature.Temperature, temperature.TemperatureHistory)
    {
    }

    private TemperatureChartWindow(string sourceName, int? currentTemperature, TemperatureHistory history)
    {
        SourceName = sourceName;
        _samples = history.Samples.OrderBy(sample => sample.TimestampUtc).ToArray();
        var values = _samples.Select(sample => sample.Celsius).ToArray();
        CurrentLabel = currentTemperature is int current ? $"{current} °C" : "—";
        LowLabel = values.Length > 0 ? $"{values.Min()} °C" : "—";
        HighLabel = values.Length > 0 ? $"{values.Max()} °C" : "—";
        DataContext = this;
        InitializeComponent();
        Title = LocalizationService.Get("TemperatureHistoryTitle", sourceName);
        AutomationProperties.SetName(this, Title);
        SourceInitialized += (_, _) => NativeWindowTheme.Apply(this);
        Loaded += (_, _) => DrawChart();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        if (ChartCanvas == null || ChartCanvas.ActualWidth < 40 || ChartCanvas.ActualHeight < 40)
            return;

        ChartCanvas.Children.Clear();
        EmptyText.Visibility = _samples.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_samples.Count == 0)
            return;

        const double left = 42, right = 12, top = 12, bottom = 30;
        var width = ChartCanvas.ActualWidth - left - right;
        var height = ChartCanvas.ActualHeight - top - bottom;
        var start = _samples[0].TimestampUtc;
        var end = _samples[^1].TimestampUtc;
        var duration = Math.Max(1, (end - start).TotalSeconds);

        for (var i = 0; i <= 4; i++)
        {
            var y = top + height * i / 4;
            ChartCanvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(MediaColor.FromArgb(80, 119, 135, 255)), StrokeThickness = 1 });
            AddLabel($"{TemperatureChartScale.MaximumCelsius - TemperatureChartScale.Range * i / 4}°", 0, y - 8, 36, TextAlignment.Right);
        }

        for (var i = 0; i <= 3; i++)
        {
            var point = start + TimeSpan.FromSeconds(duration * i / 3);
            AddLabel(TemperatureChartScale.FormatTimeLabel(point), left + width * i / 3 - 26, top + height + 8, 52, TextAlignment.Center);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < _samples.Count; i++)
            {
                var sample = _samples[i];
                var x = left + (sample.TimestampUtc - start).TotalSeconds / duration * width;
                var y = TemperatureChartScale.GetY(sample.Celsius, top, height);
                if (i == 0) context.BeginFigure(new WpfPoint(x, y), false, false);
                else context.LineTo(new WpfPoint(x, y), true, false);
            }
        }
        geometry.Freeze();
        var path = new Path
        {
            Data = geometry,
            Stroke = new LinearGradientBrush(MediaColor.FromRgb(85, 214, 160), MediaColor.FromRgb(119, 135, 255), 0),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            Width = ChartCanvas.ActualWidth,
            Height = ChartCanvas.ActualHeight
        };
        WpfCanvas.SetLeft(path, 0);
        WpfCanvas.SetTop(path, 0);
        ChartCanvas.Children.Add(path);
    }

    private void AddLabel(string text, double left, double top, double width, TextAlignment alignment)
    {
        var label = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Width = width,
            FontSize = 10,
            TextAlignment = alignment,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(133, 142, 175))
        };
        WpfCanvas.SetLeft(label, left);
        WpfCanvas.SetTop(label, top);
        ChartCanvas.Children.Add(label);
    }
}

internal static class TemperatureChartScale
{
    internal const int MinimumCelsius = 0;
    internal const int MaximumCelsius = 100;
    internal const int Range = MaximumCelsius - MinimumCelsius;

    internal static double GetY(int celsius, double top, double height)
    {
        var value = Math.Clamp(celsius, MinimumCelsius, MaximumCelsius);
        return top + (MaximumCelsius - value) / (double)Range * height;
    }

    internal static string FormatTimeLabel(DateTime timestampUtc) =>
        timestampUtc.ToLocalTime().ToString("HH':'mm");
}
