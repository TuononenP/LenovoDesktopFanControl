using System.Windows;

namespace LenovoDesktopFanControl;

using SystemColors = System.Windows.SystemColors;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private readonly Dictionary<string, System.Windows.Media.Color> _standardPalette = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\LenovoDesktopFanControl",
            createdNew: out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            System.Windows.MessageBox.Show(
                "Lenovo Desktop Fan Control is already running.",
                "Lenovo Desktop Fan Control",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        foreach (var key in PaletteKeys)
            _standardPalette[key] = (System.Windows.Media.Color)Resources[key];

        ApplySystemContrastPalette();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast))
            return;

        if (Dispatcher.CheckAccess())
            ApplySystemContrastPalette();
        else
            _ = Dispatcher.BeginInvoke(ApplySystemContrastPalette);
    }

    private void ApplySystemContrastPalette()
    {
        if (!SystemParameters.HighContrast)
        {
            foreach (var entry in _standardPalette)
                Resources[entry.Key] = entry.Value;
            return;
        }

        SetPaletteColor("WindowColor", SystemColors.WindowColor);
        SetPaletteColor("SurfaceColor", SystemColors.WindowColor);
        SetPaletteColor("SurfaceElevatedColor", SystemColors.ControlColor);
        SetPaletteColor("SurfaceHoverColor", SystemColors.ControlColor);
        SetPaletteColor("BorderColor", SystemColors.WindowTextColor);
        SetPaletteColor("AccentColor", SystemColors.HighlightColor);
        SetPaletteColor("AccentHoverColor", SystemColors.HotTrackColor);
        SetPaletteColor("TextPrimaryColor", SystemColors.WindowTextColor);
        SetPaletteColor("TextOnAccentColor", SystemColors.HighlightTextColor);
        SetPaletteColor("TextSecondaryColor", SystemColors.WindowTextColor);
        SetPaletteColor("TextMutedColor", SystemColors.WindowTextColor);
        SetPaletteColor("SuccessColor", SystemColors.WindowTextColor);
        SetPaletteColor("WarningColor", SystemColors.WindowTextColor);
        SetPaletteColor("DangerColor", SystemColors.WindowTextColor);
        SetPaletteColor("UnsupportedColor", SystemColors.WindowTextColor);
        SetPaletteColor("AccentSoftColor", SystemColors.ControlColor);
        SetPaletteColor("WarningSoftColor", SystemColors.ControlColor);
        SetPaletteColor("DangerSoftColor", SystemColors.ControlColor);
    }

    private void SetPaletteColor(string key, System.Windows.Media.Color color) => Resources[key] = color;

    private static readonly string[] PaletteKeys =
    [
        "WindowColor", "SurfaceColor", "SurfaceElevatedColor", "SurfaceHoverColor",
        "BorderColor", "AccentColor", "AccentHoverColor", "TextPrimaryColor",
        "TextOnAccentColor", "TextSecondaryColor", "TextMutedColor", "SuccessColor", "WarningColor",
        "DangerColor", "UnsupportedColor", "AccentSoftColor", "WarningSoftColor", "DangerSoftColor"
    ];
}
