using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace LenovoDesktopFanControl.Views.Controls;

public partial class FanCurveEditorWindow : Window
{
    private readonly Slider[] _sliders = new Slider[10];
    private readonly TextBlock[] _valueLabels = new TextBlock[10];
    private readonly byte[] _values = new byte[10];
    public byte[]? ResultCurve { get; private set; }

    private static readonly byte[] DefaultCurve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly int[] CurveTemperatures = [30, 40, 50, 55, 60, 65, 70, 80, 90, 100];

    public FanCurveEditorWindow(string fanName, byte[] currentCurve)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeWindowTheme.Apply(this);
        Header.Text = LocalizationService.Get("CurveEditorHeader", fanName);

        var source = currentCurve.Length == 10 ? currentCurve : DefaultCurve;

        for (var i = 0; i < 10; i++)
        {
            _values[i] = source[i];
            var slider = new Slider
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Minimum = FanTable.MinimumSpeeds[i],
                Maximum = 10,
                Value = source[i],
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                TickPlacement = TickPlacement.TopLeft,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            slider.SetResourceReference(FrameworkElement.StyleProperty, "VerticalSliderStyle");
            AutomationProperties.SetName(
                slider,
                LocalizationService.Get("AutomationCurvePoint", CurveTemperatures[i]));
            AutomationProperties.SetHelpText(
                slider,
                LocalizationService.Get("AutomationCurvePointHelp"));
            var idx = i;
            slider.ValueChanged += (_, _) => OnSliderChanged(idx);
            _sliders[i] = slider;
            SlidersGrid.Children.Add(slider);
            Grid.SetColumn(slider, i);

            var label = new TextBlock
            {
                Text = $"{source[i] * 10}%",
                FontSize = 10,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _valueLabels[i] = label;
            LabelsGrid.Children.Add(label);
            Grid.SetColumn(label, i);
        }

        Loaded += (_, _) => DrawCurve();
    }

    private void OnSliderChanged(int idx)
    {
        _values[idx] = (byte)Math.Round(_sliders[idx].Value);
        _valueLabels[idx].Text = $"{_values[idx] * 10}%";
        DrawCurve();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawCurve();
    }

    private void DrawCurve()
    {
        CurveCanvas.Children.Clear();

        if (CurveCanvas.ActualWidth <= 0 || CurveCanvas.ActualHeight <= 0)
            return;

        const double horizontalInset = 8;
        const double verticalInset = 12;
        var w = Math.Max(0, CurveCanvas.ActualWidth - horizontalInset * 2);
        var h = Math.Max(0, CurveCanvas.ActualHeight - verticalInset * 2);
        var stepW = w / 10;
        var firstPointX = horizontalInset + stepW / 2;
        var accentBrush = (WpfBrush)FindResource("AccentBrush");
        var gridBrush = (WpfBrush)FindResource("BorderBrush");

        for (var i = 0; i <= 5; i++)
        {
            var y = verticalInset + h * i / 5;
            CurveCanvas.Children.Add(new Line
            {
                X1 = horizontalInset,
                X2 = horizontalInset + w,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                Opacity = 0.55
            });
        }

        var polyline = new Polyline
        {
            Stroke = accentBrush,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round
        };

        for (var i = 0; i < 10; i++)
        {
            var x = firstPointX + i * stepW;
            var y = verticalInset + h - (_values[i] / 10.0 * h);
            polyline.Points.Add(new System.Windows.Point(x, y));
        }

        CurveCanvas.Children.Add(polyline);

        for (var i = 0; i < polyline.Points.Count; i++)
        {
            var point = polyline.Points[i];
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = accentBrush,
                Stroke = WpfBrushes.White,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(dot, point.X - 4.5);
            Canvas.SetTop(dot, point.Y - 4.5);
            CurveCanvas.Children.Add(dot);
        }
    }

    private bool Validate()
    {
        return new FanTable { Speeds = [.. _values] }.IsValid();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ResultCurve = (byte[])_values.Clone();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < 10; i++)
        {
            _values[i] = DefaultCurve[i];
            _sliders[i].Value = DefaultCurve[i];
            _valueLabels[i].Text = $"{DefaultCurve[i] * 10}%";
        }
        DrawCurve();
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
