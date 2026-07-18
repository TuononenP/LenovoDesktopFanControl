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
    internal const int MaxNameLength = 48;

    private LightingColorOption? _selectedColor;
    private bool _isEnabled = true;
    private bool _isEditingName;
    private bool _isColorApplying;
    private int _brightness = 100;
    private string _name;

    public LightingZoneViewModel(LightingZoneInfo info, LightingColorOption? defaultColor = null)
    {
        Index = info.Index;
        DefaultName = string.IsNullOrWhiteSpace(info.Name)
            ? $"Light {info.Index + 1}"
            : info.Name.Trim();
        _name = DefaultName;
        Kind = info.Kind;
        LampCount = info.LampCount;
        SelectedColor = defaultColor;
        EditNameCommand = new RelayCommand(() => IsEditingName = true);
    }

    public int Index { get; }
    public string DefaultName { get; }
    public string Name
    {
        get => _name;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? DefaultName
                : value.Trim();
            if (normalized.Length > MaxNameLength)
                normalized = normalized[..MaxNameLength];

            if (_name == normalized)
            {
                // Refresh an editor that was cleared or contained only extra whitespace.
                if (!string.Equals(value, normalized, StringComparison.Ordinal))
                    OnPropertyChanged();
                return;
            }

            _name = normalized;
            OnPropertyChanged();
        }
    }
    public LightingZoneKind Kind { get; }
    public bool IsGraphicsCard => Kind == LightingZoneKind.GraphicsCard;
    public int LampCount { get; }
    public bool IsEditingName
    {
        get => _isEditingName;
        set
        {
            if (_isEditingName == value) return;
            _isEditingName = value;
            OnPropertyChanged();
        }
    }
    public ICommand EditNameCommand { get; }

    public int Brightness
    {
        get => _brightness;
        set
        {
            var brightness = Math.Clamp(value, 0, 100);
            if (_brightness == brightness) return;
            _brightness = brightness;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanChangeColor));
        }
    }

    public bool IsColorApplying
    {
        get => _isColorApplying;
        internal set
        {
            if (_isColorApplying == value) return;
            _isColorApplying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanChangeColor));
        }
    }

    public bool CanChangeColor => IsEnabled && !IsColorApplying;

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
    private const int GpuColorDebounceMilliseconds = 100;
    private const int BrightnessDebounceMilliseconds = 200;
    private readonly ILightingControlService? _service;
    private readonly ILightingColorPicker _colorPicker;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _brightnessTimer;
    private readonly Dictionary<LightingZoneViewModel, DispatcherTimer> _zoneBrightnessTimers = [];
    private bool _isAvailable;
    private bool _isEnabled = true;
    private bool _isBusy;
    private int _brightness = 100;
    private LightingColorOption? _globalColor;
    private string _deviceSummary = "";
    private string _status = "";
    private bool _pendingReapply;
    private bool _restoringZoneNames;
    private bool _restoringZoneBrightness;
    private bool _restoringZoneState;

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
    public int Brightness
    {
        get => _brightness;
        set
        {
            var brightness = Math.Clamp(value, 0, 100);
            if (_brightness == brightness) return;
            _brightness = brightness;
            OnPropertyChanged();
            QueueBrightnessApply();
        }
    }
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

    internal void RestoreGlobalColorSelection(LightingColorOption? color)
    {
        if (_globalColor == color) return;
        _globalColor = color;
        OnPropertyChanged(nameof(GlobalColor));
    }

    internal LightingColorOption FindOrAddColor(byte red, byte green, byte blue)
    {
        var existing = Colors.FirstOrDefault(color =>
            color.Red == red && color.Green == green && color.Blue == blue);
        if (existing != null)
            return existing;

        var hex = $"#{red:X2}{green:X2}{blue:X2}";
        var custom = new LightingColorOption(
            LocalizationService.Get("ColorCustomValue", hex),
            red,
            green,
            blue);
        Colors.Add(custom);
        return custom;
    }

    public string DeviceSummary { get => _deviceSummary; private set { _deviceSummary = value; OnPropertyChanged(); } }
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    public bool HasZones => Zones.Count >= 1;

    public ICommand ToggleCommand { get; }
    public ICommand ToggleZoneCommand { get; }
    public ICommand PickGlobalColorCommand { get; }
    public ICommand PickZoneColorCommand { get; }

    public event EventHandler? Applied;
    public event EventHandler? SettingsChanged;

    public LightingViewModel(
        ILightingControlService? service,
        ILightingColorPicker? colorPicker = null)
    {
        _service = service;
        _colorPicker = colorPicker ?? new CustomLightingColorPicker();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _brightnessTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(BrightnessDebounceMilliseconds),
            DispatcherPriority.Background,
            (sender, _) =>
            {
                if (sender is DispatcherTimer timer)
                    timer.Stop();
                ApplyBrightness(_brightness);
            },
            _dispatcher);
        _globalColor = Colors[0];
        ToggleCommand = new RelayCommand(async () => await ToggleAsync());
        ToggleZoneCommand = new RelayCommand<LightingZoneViewModel>(
            zone => _ = ToggleZoneAsync(zone),
            zone => zone != null);
        PickGlobalColorCommand = new RelayCommand(PickGlobalColor);
        PickZoneColorCommand = new RelayCommand<LightingZoneViewModel>(
            PickZoneColor,
            zone => zone is { CanChangeColor: true });

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
            Status = device == null ? "Lenovo tower lighting was not detected" : "";

            ClearZoneBrightnessTimers();
            Zones.Clear();
            if (device != null)
            {
                foreach (var zone in device.Zones)
                {
                    var zoneViewModel = new LightingZoneViewModel(zone, Colors[0]);
                    zoneViewModel.PropertyChanged += OnZonePropertyChanged;
                    Zones.Add(zoneViewModel);
                }
                OnPropertyChanged(nameof(HasZones));
            }
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Lighting initialization failed: {ex.Message}");
        }
    }

    internal void RestoreZoneNames(IReadOnlyDictionary<int, string> names)
    {
        _restoringZoneNames = true;
        try
        {
            foreach (var zone in Zones)
            {
                if (names.TryGetValue(zone.Index, out var name) &&
                    !string.IsNullOrWhiteSpace(name))
                    zone.Name = name;
            }
        }
        finally
        {
            _restoringZoneNames = false;
        }
    }

    internal void RestoreZoneBrightness(IReadOnlyDictionary<int, int> brightnessByZone)
    {
        _restoringZoneBrightness = true;
        try
        {
            foreach (var zone in Zones)
                zone.Brightness = brightnessByZone.TryGetValue(zone.Index, out var brightness)
                    ? brightness
                    : 100;
        }
        finally
        {
            _restoringZoneBrightness = false;
        }
    }

    internal void RestoreZoneState(Action restore)
    {
        _restoringZoneState = true;
        try
        {
            restore();
        }
        finally
        {
            _restoringZoneState = false;
        }
    }

    private void OnZonePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoringZoneState)
            return;

        if (!_restoringZoneNames &&
            e.PropertyName == nameof(LightingZoneViewModel.Name))
            SettingsChanged?.Invoke(this, EventArgs.Empty);

        if (!_restoringZoneBrightness &&
            e.PropertyName == nameof(LightingZoneViewModel.Brightness) &&
            sender is LightingZoneViewModel zone)
            QueueZoneBrightnessApply(zone);

        if (e.PropertyName == nameof(LightingZoneViewModel.SelectedColor) &&
            sender is LightingZoneViewModel colorZone &&
            colorZone.SelectedColor is { } color)
        {
            RefreshGlobalColorSelection();
            _ = ApplyZoneColorAsync(colorZone, color);
        }
    }

    private void RefreshGlobalColorSelection()
    {
        var firstColor = Zones.FirstOrDefault()?.SelectedColor;
        var uniformColor = firstColor != null && Zones.All(zone =>
            zone.SelectedColor is { } color &&
            color.Red == firstColor.Red &&
            color.Green == firstColor.Green &&
            color.Blue == firstColor.Blue)
            ? Colors.FirstOrDefault(color =>
                color.Red == firstColor.Red &&
                color.Green == firstColor.Green &&
                color.Blue == firstColor.Blue)
            : null;

        if (_globalColor == uniformColor)
            return;

        _globalColor = uniformColor;
        OnPropertyChanged(nameof(GlobalColor));
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
        CancelPendingBrightnessApplies();
        Log.Info($"ReapplyAsync: brightness={Brightness}%, zones={Zones.Count}, enabled={IsEnabled}");
        try
        {
            await _service.SetBrightnessAsync(Brightness / 100d);
            foreach (var zone in Zones)
            {
                await _service.SetZoneEnabledAsync(zone.Index, zone.IsEnabled);
                await _service.SetZoneBrightnessAsync(zone.Index, zone.Brightness / 100d);
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

    public Task ReapplyGpuStateAsync() =>
        _service?.ReapplyGpuStateAsync() ?? Task.CompletedTask;

    internal async Task ApplyAsync()
    {
        if (_service == null || !IsAvailable || IsBusy)
        {
            Log.Info($"ApplyAsync skipped: service={_service != null}, available={IsAvailable}, busy={IsBusy}");
            return;
        }
        CancelPendingBrightnessApplies();
        IsBusy = true;
        Log.Info($"ApplyAsync: brightness={Brightness}%, zones={Zones.Count}, enabled={IsEnabled}");
        try
        {
            await _service.SetBrightnessAsync(Brightness / 100d);
            foreach (var zone in Zones)
            {
                await _service.SetZoneEnabledAsync(zone.Index, zone.IsEnabled);
                await _service.SetZoneBrightnessAsync(zone.Index, zone.Brightness / 100d);
                if (zone.SelectedColor == null) continue;
                Log.Info($"  zone {zone.Index} '{zone.Name}': enabled={zone.IsEnabled}, brightness={zone.Brightness}%, {zone.SelectedColor.Name} (r={zone.SelectedColor.Red}, g={zone.SelectedColor.Green}, b={zone.SelectedColor.Blue})");
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
        RestoreZoneState(() =>
        {
            foreach (var zone in Zones)
                zone.SelectedColor = color;
        });

        try
        {
            foreach (var zone in Zones)
                await _service.SetZoneColorAsync(zone.Index, color.Red, color.Green, color.Blue);
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

    private void PickGlobalColor()
    {
        var originalColors = Zones
            .Select(zone => (Zone: zone, Color: zone.SelectedColor))
            .ToList();
        var previewChain = Task.CompletedTask;
        var previewIsActive = 1;
        var selected = _colorPicker.Pick(
            GlobalColor ?? Zones.FirstOrDefault()?.SelectedColor ?? Colors[0],
            color => previewChain = QueuePreview(previewChain, () =>
                Volatile.Read(ref previewIsActive) == 1
                    ? PreviewZoneColorsAsync(Zones, color)
                    : Task.CompletedTask));

        Interlocked.Exchange(ref previewIsActive, 0);

        if (selected == null)
        {
            foreach (var (zone, color) in originalColors)
            {
                if (color != null)
                    _ = PreviewZoneColorsAsync([zone], color);
            }
            return;
        }

        GlobalColor = FindOrAddColor(selected.Red, selected.Green, selected.Blue);
    }

    private void PickZoneColor(LightingZoneViewModel? zone)
    {
        if (zone is not { CanChangeColor: true })
            return;

        var originalColor = zone.SelectedColor;
        var previewChain = Task.CompletedTask;
        var previewIsActive = 1;
        var selected = _colorPicker.Pick(
            originalColor ?? Colors[0],
            color => previewChain = QueuePreview(previewChain, () =>
                Volatile.Read(ref previewIsActive) == 1
                    ? PreviewZoneColorsAsync([zone], color)
                    : Task.CompletedTask));

        Interlocked.Exchange(ref previewIsActive, 0);

        if (selected == null)
        {
            if (originalColor != null)
                _ = PreviewZoneColorsAsync([zone], originalColor);
            return;
        }

        zone.SelectedColor = FindOrAddColor(selected.Red, selected.Green, selected.Blue);
    }

    private async Task PreviewZoneColorsAsync(
        IEnumerable<LightingZoneViewModel> zones,
        LightingColorOption color)
    {
        if (_service == null || !IsAvailable)
            return;

        try
        {
            foreach (var zone in zones)
            {
                await _service.SetZoneColorAsync(
                    zone.Index,
                    color.Red,
                    color.Green,
                    color.Blue);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to preview lighting color: {ex.Message}");
        }
    }

    private static Task QueuePreview(Task previous, Func<Task> operation) =>
        previous.IsCompletedSuccessfully
            ? operation()
            : previous.ContinueWith(
                    _ => operation(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();

    private async Task ToggleZoneAsync(LightingZoneViewModel? zone)
    {
        if (zone == null || _service == null || !IsAvailable || IsBusy)
            return;

        try
        {
            await _service.SetZoneEnabledAsync(zone.Index, zone.IsEnabled);
            Status = zone.IsEnabled
                ? $"{zone.Name} enabled"
                : $"{zone.Name} disabled";
            Applied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            zone.IsEnabled = !zone.IsEnabled;
            Status = ex.Message;
            Log.Warn($"Failed to toggle lighting zone {zone.Index}: {ex.Message}");
        }
    }

    private async Task ApplyZoneBrightnessAsync(LightingZoneViewModel zone)
    {
        if (_service == null || !IsAvailable)
            return;

        try
        {
            await _service.SetZoneBrightnessAsync(zone.Index, zone.Brightness / 100d);
            Status = $"{zone.Name} brightness set to {zone.Brightness}%";
            Applied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Failed to set lighting zone {zone.Index} brightness: {ex.Message}");
        }
    }

    private void QueueBrightnessApply()
    {
        _brightnessTimer.Stop();
        _brightnessTimer.Start();
    }

    private void QueueZoneBrightnessApply(LightingZoneViewModel zone)
    {
        if (!_zoneBrightnessTimers.TryGetValue(zone, out var timer))
        {
            timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(BrightnessDebounceMilliseconds)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _ = ApplyZoneBrightnessAsync(zone);
            };
            _zoneBrightnessTimers.Add(zone, timer);
        }

        timer.Stop();
        timer.Start();
    }

    private void CancelPendingBrightnessApplies()
    {
        _brightnessTimer.Stop();
        foreach (var timer in _zoneBrightnessTimers.Values)
            timer.Stop();
    }

    private void ClearZoneBrightnessTimers()
    {
        foreach (var timer in _zoneBrightnessTimers.Values)
            timer.Stop();
        _zoneBrightnessTimers.Clear();
    }

    private async Task ApplyZoneColorAsync(
        LightingZoneViewModel zone,
        LightingColorOption color)
    {
        if (_service == null || !IsAvailable)
            return;

        var showPendingState = zone.IsGraphicsCard;
        if (showPendingState)
            zone.IsColorApplying = true;

        try
        {
            if (showPendingState)
                await Task.Delay(GpuColorDebounceMilliseconds);

            await _service.SetZoneColorAsync(
                zone.Index,
                color.Red,
                color.Green,
                color.Blue);
            Status = $"{zone.Name} color set to {color.Name}";
            Applied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Failed to set lighting zone {zone.Index} color: {ex.Message}");
        }
        finally
        {
            if (showPendingState)
                zone.IsColorApplying = false;
        }
    }

    private async void ApplyBrightness(int brightness)
    {
        if (_service == null || !IsAvailable)
        {
            Log.Info($"ApplyBrightness skipped: service={_service != null}, available={IsAvailable}");
            return;
        }

        try
        {
            await _service.SetBrightnessAsync(brightness / 100d);
            Status = $"Brightness set to {brightness}%";
            Log.Info($"ApplyBrightness succeeded: {brightness}%");
            Applied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Warn($"Failed to apply brightness: {ex.Message}");
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

    internal async Task PersistStateAsync()
    {
        if (_service == null || !IsAvailable)
            return;

        try
        {
            await _service.PersistStateAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to persist lighting state on exit: {ex.Message}");
        }
    }

    public void Dispose()
    {
        CancelPendingBrightnessApplies();
        ClearZoneBrightnessTimers();
        foreach (var zone in Zones)
            zone.PropertyChanged -= OnZonePropertyChanged;
        if (_service != null)
            _service.AvailabilityChanged -= OnServiceAvailabilityChanged;
        _service?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
