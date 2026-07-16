using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.ViewModels;

public sealed class LightingZoneViewModel : INotifyPropertyChanged
{
    private LightingColorOption? _selectedColor;

    public LightingZoneViewModel(LightingZoneInfo info, LightingColorOption? defaultColor = null)
    {
        Index = info.Index;
        Name = info.Name;
        Kind = info.Kind;
        LampCount = info.LampCount;
        SelectedColor = defaultColor;
    }

    public int Index { get; }
    public string Name { get; }
    public LightingZoneKind Kind { get; }
    public int LampCount { get; }

    public LightingColorOption? SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (_selectedColor == value) return;
            _selectedColor = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class LightingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILightingControlService? _service;
    private readonly Dispatcher _dispatcher;
    private bool _isAvailable;
    private bool _isEnabled = true;
    private bool _isBusy;
    private int _brightness = 100;
    private LightingColorOption? _globalColor;
    private string _deviceSummary = "";
    private string _status = "";
    private bool _pendingReapply;

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

    public ObservableCollection<LightingZoneViewModel> Zones { get; } = [];

    public bool IsAvailable { get => _isAvailable; private set { _isAvailable = value; OnPropertyChanged(); } }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            if (!_isBusy && _pendingReapply)
            {
                _pendingReapply = false;
                Log.Info("Draining deferred lighting reapply after busy state ended");
                _ = ReapplyAsync();
            }
        }
    }
    public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    public int Brightness { get => _brightness; set { _brightness = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public LightingColorOption? GlobalColor
    {
        get => _globalColor;
        set
        {
            if (_globalColor == value) return;
            _globalColor = value;
            OnPropertyChanged();
            if (value != null)
                ApplyGlobalColor(value);
        }
    }
    public string DeviceSummary { get => _deviceSummary; private set { _deviceSummary = value; OnPropertyChanged(); } }
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    public bool HasZones => Zones.Count >= 1;

    public ICommand ApplyCommand { get; }
    public ICommand ToggleCommand { get; }

    public event EventHandler? Applied;

    public LightingViewModel(ILightingControlService? service)
    {
        _service = service;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _globalColor = Colors[0];
        ApplyCommand = new RelayCommand(async () => await ApplyAsync());
        ToggleCommand = new RelayCommand(async () => await ToggleAsync());

        if (_service != null)
            _service.AvailabilityChanged += OnServiceAvailabilityChanged;
    }

    private void OnServiceAvailabilityChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.CheckAccess())
            HandleAvailabilityChanged();
        else
            _ = _dispatcher.BeginInvoke(HandleAvailabilityChanged);
    }

    private async void HandleAvailabilityChanged()
    {
        if (_service == null || !IsAvailable)
            return;

        if (!_service.IsControlAvailable)
            return;

        if (IsBusy)
        {
            Log.Info("Lighting control became available but busy; deferring reapply");
            _pendingReapply = true;
            return;
        }

        Log.Info("Lighting control became available, re-applying lighting state");
        await ReapplyAsync();
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

            Zones.Clear();
            if (device != null)
            {
                foreach (var zone in device.Zones)
                    Zones.Add(new LightingZoneViewModel(zone, Colors[0]));
                OnPropertyChanged(nameof(HasZones));
            }
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Lighting initialization failed: {ex.Message}");
        }
    }

    public async Task ReapplyAsync()
    {
        if (_service == null || !IsAvailable)
            return;
        if (IsBusy)
        {
            Log.Info("ReapplyAsync requested while busy; deferring");
            _pendingReapply = true;
            return;
        }
        Log.Info($"ReapplyAsync: brightness={Brightness}%, zones={Zones.Count}, enabled={IsEnabled}");
        try
        {
            await _service.SetBrightnessAsync(Brightness / 100d);
            foreach (var zone in Zones)
            {
                if (zone.SelectedColor == null) continue;
                await _service.SetZoneColorAsync(zone.Index, zone.SelectedColor.Red, zone.SelectedColor.Green, zone.SelectedColor.Blue);
            }
            await _service.SetEnabledAsync(IsEnabled);
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to re-apply lighting on activation: {ex.Message}");
        }
    }

    internal async Task ApplyAsync()
    {
        if (_service == null || !IsAvailable || IsBusy)
        {
            Log.Info($"ApplyAsync skipped: service={_service != null}, available={IsAvailable}, busy={IsBusy}");
            return;
        }
        IsBusy = true;
        Log.Info($"ApplyAsync: brightness={Brightness}%, zones={Zones.Count}, enabled={IsEnabled}");
        try
        {
            await _service.SetBrightnessAsync(Brightness / 100d);
            foreach (var zone in Zones)
            {
                if (zone.SelectedColor == null) continue;
                Log.Info($"  zone {zone.Index} '{zone.Name}': {zone.SelectedColor.Name} (r={zone.SelectedColor.Red}, g={zone.SelectedColor.Green}, b={zone.SelectedColor.Blue})");
                await _service.SetZoneColorAsync(zone.Index, zone.SelectedColor.Red, zone.SelectedColor.Green, zone.SelectedColor.Blue);
            }
            await _service.SetEnabledAsync(IsEnabled);
            Status = $"Applied lighting at {Brightness}%";
            Log.Info($"ApplyAsync succeeded");
            Applied?.Invoke(this, EventArgs.Empty);
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

    private async void ApplyGlobalColor(LightingColorOption color)
    {
        if (_service == null || !IsAvailable)
        {
            Log.Info($"ApplyGlobalColor skipped: service={_service != null}, available={IsAvailable}");
            return;
        }

        Log.Info($"ApplyGlobalColor: {color.Name} (r={color.Red}, g={color.Green}, b={color.Blue}) to all {Zones.Count} zones");
        foreach (var zone in Zones)
            zone.SelectedColor = color;

        try
        {
            await _service.SetColorAsync(color.Red, color.Green, color.Blue);
            Status = $"Applied {color.Name} to all lights";
            Log.Info($"ApplyGlobalColor succeeded");
            Applied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Failed to apply global color: {ex.Message}");
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

    public void Dispose()
    {
        if (_service != null)
            _service.AvailabilityChanged -= OnServiceAvailabilityChanged;
        _service?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}