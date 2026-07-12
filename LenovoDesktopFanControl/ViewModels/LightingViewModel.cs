using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.ViewModels;

public sealed class LightingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILightingControlService? _service;
    private bool _isAvailable;
    private bool _isEnabled = true;
    private bool _isBusy;
    private int _brightness = 100;
    private LightingColorOption? _selectedColor;
    private string _deviceSummary = "";
    private string _status = "";

    public ObservableCollection<LightingColorOption> Colors { get; } =
    [
        new("Legion Blue", 91, 157, 255),
        new("White", 255, 255, 255),
        new("Red", 255, 55, 75),
        new("Orange", 255, 135, 40),
        new("Green", 55, 220, 125),
        new("Cyan", 0, 211, 254),
        new("Purple", 145, 85, 255)
    ];

    public bool IsAvailable { get => _isAvailable; private set { _isAvailable = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }
    public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    public int Brightness { get => _brightness; set { _brightness = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public LightingColorOption? SelectedColor { get => _selectedColor; set { _selectedColor = value; OnPropertyChanged(); } }
    public string DeviceSummary { get => _deviceSummary; private set { _deviceSummary = value; OnPropertyChanged(); } }
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    public ICommand ApplyCommand { get; }
    public ICommand ToggleCommand { get; }

    public LightingViewModel(ILightingControlService? service)
    {
        _service = service;
        _selectedColor = Colors[0];
        ApplyCommand = new RelayCommand(async () => await ApplyAsync());
        ToggleCommand = new RelayCommand(async () => await ToggleAsync());
    }

    public async Task InitializeAsync()
    {
        if (_service == null)
            return;

        try
        {
            var device = await _service.DiscoverAsync();
            IsAvailable = device != null;
            DeviceSummary = device == null
                ? ""
                : $"{device.LampCount} addressable lights · VID {device.VendorId:X4} · PID {device.ProductId:X4}";
            Status = device == null ? "Lenovo tower lighting was not detected" : "Lighting controller ready";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Lighting initialization failed: {ex.Message}");
        }
    }

    internal async Task ApplyAsync()
    {
        if (_service == null || !IsAvailable || SelectedColor == null || IsBusy)
            return;
        IsBusy = true;
        try
        {
            await _service.SetBrightnessAsync(Brightness / 100d);
            await _service.SetColorAsync(SelectedColor.Red, SelectedColor.Green, SelectedColor.Blue);
            await _service.SetEnabledAsync(IsEnabled);
            Status = $"Applied {SelectedColor.Name} at {Brightness}%";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Failed to apply lighting: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ToggleAsync()
    {
        if (_service == null || !IsAvailable || IsBusy)
            return;
        IsBusy = true;
        try
        {
            await _service.SetEnabledAsync(IsEnabled);
            Status = IsEnabled ? "Lighting enabled" : "Lighting disabled";
        }
        catch (Exception ex)
        {
            IsEnabled = !IsEnabled;
            Status = ex.Message;
            Log.Warn($"Failed to toggle lighting: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose() => _service?.Dispose();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
