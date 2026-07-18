using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.ViewModels;
using MediaColor = System.Windows.Media.Color;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfPoint = System.Windows.Point;

namespace LenovoDesktopFanControl.Views.Controls;

public partial class TemperatureChartWindow : Window
{
    private readonly IReadOnlyList<TemperatureHistorySample> _samples;

    public string FanName { get; }
    public string CurrentLabel { get; }
    public string LowLabel { get; }
    public string HighLabel { get; }

    public TemperatureChartWindow(FanViewModel fan)
    {
        FanName = fan.FanName;
        _samples = fan.TemperatureHistory.Samples.OrderBy(sample => sample.TimestampUtc).ToArray();
        var values = _samples.Select(sample => sample.Celsius).ToArray();
        CurrentLabel = fan.Temperature is int current ? $"{current} °C" : "—";
        LowLabel = values.Length > 0 ? $"{values.Min()} °C" : "—";
        HighLabel = values.Length > 0 ? $"{values.Max()} °C" : "—";
        DataContext = this;
        InitializeComponent();
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
        var minimum = Math.Min(20, _samples.Min(sample => sample.Celsius) - 3);
        var maximum = Math.Max(40, _samples.Max(sample => sample.Celsius) + 3);
        var range = Math.Max(1, maximum - minimum);
        var start = _samples[0].TimestampUtc;
        var end = _samples[^1].TimestampUtc;
        var duration = Math.Max(1, (end - start).TotalSeconds);

        for (var i = 0; i <= 4; i++)
        {
            var y = top + height * i / 4;
            ChartCanvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(MediaColor.FromArgb(80, 119, 135, 255)), StrokeThickness = 1 });
            AddLabel($"{maximum - range * i / 4}°", 0, y - 8, 36, TextAlignment.Right);
        }

        for (var i = 0; i <= 3; i++)
        {
            var point = start + TimeSpan.FromSeconds(duration * i / 3);
            AddLabel(point.ToLocalTime().ToString(duration > 3600 ? "HH:mm" : "HH:mm:ss"), left + width * i / 3 - 26, top + height + 8, 52, TextAlignment.Center);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < _samples.Count; i++)
            {
                var sample = _samples[i];
                var x = left + (sample.TimestampUtc - start).TotalSeconds / duration * width;
                var y = top + (maximum - sample.Celsius) / range * height;
                if (i == 0) context.BeginFigure(new WpfPoint(x, y), false, false);
                else context.LineTo(new WpfPoint(x, y), true, false);
            }
        }
        geometry.Freeze();
        ChartCanvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = new LinearGradientBrush(MediaColor.FromRgb(85, 214, 160), MediaColor.FromRgb(119, 135, 255), 0),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round
        });
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
