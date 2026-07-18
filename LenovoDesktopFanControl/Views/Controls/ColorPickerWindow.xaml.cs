using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace LenovoDesktopFanControl.Views.Controls;

public partial class ColorPickerWindow : Window, INotifyPropertyChanged
{
    private const int PreviewDebounceMilliseconds = 200;
    private byte _red;
    private byte _green;
    private byte _blue;
    private string _hexValue = "#000000";
    private readonly Action<LightingColorOption>? _previewColor;
    private readonly DispatcherTimer _previewTimer;
    private bool _hasPendingPreview;

    public LightingColorOption? SelectedColor { get; private set; }

    public byte Red
    {
        get => _red;
        set => SetColor(value, _green, _blue);
    }

    public byte Green
    {
        get => _green;
        set => SetColor(_red, value, _blue);
    }

    public byte Blue
    {
        get => _blue;
        set => SetColor(_red, _green, value);
    }

    public string HexValue
    {
        get => _hexValue;
        set
        {
            if (TryParseHex(value, out var red, out var green, out var blue))
            {
                SetColor(red, green, blue);
                return;
            }

            if (_hexValue == value)
                return;
            _hexValue = value;
            OnPropertyChanged();
        }
    }

    public WpfBrush PreviewBrush => new SolidColorBrush(WpfColor.FromRgb(Red, Green, Blue));

    public ColorPickerWindow(
        LightingColorOption initialColor,
        Action<LightingColorOption>? previewColor = null)
    {
        _red = initialColor.Red;
        _green = initialColor.Green;
        _blue = initialColor.Blue;
        _hexValue = FormatHex(_red, _green, _blue);
        _previewColor = previewColor;
        InitializeComponent();
        DataContext = this;
        _previewTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds),
            DispatcherPriority.Background,
            (_, _) => FlushPreview(),
            Dispatcher)
        {
            IsEnabled = false
        };
        SourceInitialized += (_, _) => NativeWindowTheme.Apply(this);
        Closed += (_, _) => _previewTimer.Stop();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        FlushPreview();
        SelectedColor = new LightingColorOption("", Red, Green, Blue);
        DialogResult = true;
    }

    private void SetColor(byte red, byte green, byte blue)
    {
        if (_red == red && _green == green && _blue == blue)
            return;

        _red = red;
        _green = green;
        _blue = blue;
        _hexValue = FormatHex(red, green, blue);
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        OnPropertyChanged(nameof(HexValue));
        OnPropertyChanged(nameof(PreviewBrush));
        QueuePreview();
    }

    private void QueuePreview()
    {
        if (_previewColor == null)
            return;

        _hasPendingPreview = true;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void FlushPreview()
    {
        _previewTimer.Stop();
        if (!_hasPendingPreview)
            return;

        _hasPendingPreview = false;
        _previewColor?.Invoke(new LightingColorOption("", Red, Green, Blue));
    }

    private static bool TryParseHex(string? value, out byte red, out byte green, out byte blue)
    {
        var hex = value?.Trim().TrimStart('#') ?? "";
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) &&
            byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) &&
            byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
            return true;

        red = green = blue = 0;
        return false;
    }

    private static string FormatHex(byte red, byte green, byte blue) => $"#{red:X2}{green:X2}{blue:X2}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
