using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.Views.Controls;

namespace LenovoDesktopFanControl.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // The RTX lighting controller can acknowledge I²C writes while it is still
    // starting up. Apply the saved state once more after that short window so
    // the persisted GPU color wins over the card's power-on profile.
    private static readonly TimeSpan GpuLightingStartupSettleDelay = TimeSpan.FromMilliseconds(900);
    private int _shutdownStarted;
    private int _refreshInProgress;
    private DateTime _lastTemperatureHistorySaveUtc;
    private readonly IWmiFanControlService _service;
    private readonly ISettingsService _settingsService;
    private readonly IAutoStartService _autoStartService;
    private readonly DispatcherTimer _timer;
    private readonly ISystemTemperatureService _systemTemperatureService;

    private FanSettings _settings;
    private bool _isSupported;
    private SmartFanMode _selectedFanMode;
    private bool _isFullSpeed;
    private bool _isBusy;
    private bool _startWithWindows;
    private bool _minimizeToTray = true;
    private bool _isFullSpeedSupported = true;
    private string _selectedLanguage = "en";
    private string _statusMessage = "";
    private ApplicationStatusKind _statusKind = ApplicationStatusKind.Neutral;
    private string _conflictWarning = "";

    public ObservableCollection<FanViewModel> Fans { get; } = [];
    public ObservableCollection<SystemTemperatureViewModel> SystemTemperatures { get; } = [];
    public LightingViewModel Lighting { get; }

    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } = [];

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            ChangeLanguage(value);
        }
    }

    public bool IsSupported
    {
        get => _isSupported;
        set { _isSupported = value; OnPropertyChanged(); }
    }

    public SmartFanMode SelectedFanMode
    {
        get => _selectedFanMode;
        set { _selectedFanMode = value; OnPropertyChanged(); }
    }

    public bool IsCustomMode => SelectedFanMode == SmartFanMode.Custom;

    public bool HasFans => Fans.Count > 0;

    public bool ShowNoFans => IsSupported && !HasFans;

    public bool IsFullSpeed
    {
        get => _isFullSpeed;
        set { _isFullSpeed = value; OnPropertyChanged(); }
    }

    public bool IsFullSpeedSupported
    {
        get => _isFullSpeedSupported;
        set { _isFullSpeedSupported = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveStatusKind));
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set { _startWithWindows = value; OnPropertyChanged(); }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray == value) return;
            _minimizeToTray = value;
            _settings.MinimizeToTray = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ApplicationStatusKind StatusKind
    {
        get => _statusKind;
        private set
        {
            _statusKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveStatusKind));
        }
    }

    public ApplicationStatusKind EffectiveStatusKind => IsBusy
        ? ApplicationStatusKind.Busy
        : StatusKind;

    public string ConflictWarning
    {
        get => _conflictWarning;
        set { _conflictWarning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasConflictWarning)); }
    }

    public bool HasConflictWarning => !string.IsNullOrEmpty(ConflictWarning);

    public ICommand ApplyModeCommand { get; }
    public ICommand FullSpeedCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleAutoStartCommand { get; }

    public MainViewModel(IWmiFanControlService service, ISettingsService settingsService,
        IAutoStartService? autoStartService = null,
        ILightingControlService? lightingControlService = null,
        ISystemTemperatureService? systemTemperatureService = null)
    {
        _service = service;
        _settingsService = settingsService;
        _autoStartService = autoStartService ?? new AutoStartService();
        _settings = new FanSettings();
        _systemTemperatureService = systemTemperatureService ?? new SystemTemperatureService();
        foreach (var name in new[] { "GPU", "CPU", "SSD", "Motherboard" })
            SystemTemperatures.Add(new SystemTemperatureViewModel(name, OpenTemperatureChart));
        Lighting = new LightingViewModel(lightingControlService);
        Lighting.Applied += OnLightingApplied;
        Lighting.SettingsChanged += OnLightingSettingsChanged;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2000)
        };
        _timer.Tick += async (_, _) => await RefreshAsync();

        ApplyModeCommand = new RelayCommand(async () => await ApplyModeAsync());
        FullSpeedCommand = new RelayCommand<bool?>(
            async enabled => await ToggleFullSpeedAsync(enabled == true));
        ResetCommand = new RelayCommand(async () => await ResetAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        ToggleAutoStartCommand = new RelayCommand(() => ToggleAutoStart());

        Fans.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFans));
            OnPropertyChanged(nameof(ShowNoFans));
        };

        var langs = LocalizationService.GetAvailableCultures();
        foreach (var lang in langs)
        {
            var displayName = lang switch
            {
                "en" => "English",
                "fi-FI" => "Suomi",
                _ => lang
            };
            AvailableLanguages.Add(new LanguageInfo { Code = lang, DisplayName = displayName });
        }

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void ChangeLanguage(string lang)
    {
        LocalizationService.SetLanguage(lang);
        Loc.SetCulture(LocalizationService.CurrentCulture);
        foreach (var fan in Fans)
            fan.RefreshLocalizedName();
        foreach (var temperature in SystemTemperatures)
            temperature.RefreshLocalizedName();
        _settings.Language = lang;
        _settingsService.Save(_settings);
        if (IsBusy)
            UpdateStatus(Loc.Instance["MsgInitializing"], ApplicationStatusKind.Busy);
        else if (!IsSupported)
            UpdateStatus(Loc.Instance["MsgNotSupported"], ApplicationStatusKind.Unsupported);
        else if (Fans.Count > 0)
            UpdateStatus(
                LocalizationService.Get("MsgConnected", Fans.Count),
                HasConflictWarning ? ApplicationStatusKind.Warning : ApplicationStatusKind.Connected);
        else
            UpdateStatus(Loc.Instance["MsgNoFans"], ApplicationStatusKind.Warning);
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Log.Info("System resume detected, re-applying fan curves");
            _ = ReapplyAfterResumeAsync();
        }
    }

    private async Task ReapplyAfterResumeAsync()
    {
        try
        {
            await Task.Delay(3000).ConfigureAwait(false);
            await _timer.Dispatcher.InvokeAsync(async () =>
            {
                if (IsSupported && SelectedFanMode == SmartFanMode.Custom && !IsFullSpeed && Fans.Count > 0)
                    await ReApplyFanCurvesAsync();

                Log.Info("Re-applying lighting state after system resume");
                await Lighting.ReapplyAsync();
            }).Task.Unwrap();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to re-apply fan curve after resume", ex);
        }
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            _settings = _settingsService.Load();
            foreach (var temperature in SystemTemperatures)
            {
                if (_settings.SystemTemperatureHistory.TryGetValue(temperature.SourceId, out var history))
                    temperature.RestoreTemperatureHistory(history);
            }
            _timer.Interval = TimeSpan.FromMilliseconds(
                Math.Clamp(_settings.PollingIntervalMs, 500, 10_000));

            var lang = string.IsNullOrEmpty(_settings.Language) ? "en" : _settings.Language;
            SelectedLanguage = lang;
            LocalizationService.SetLanguage(lang);
            Loc.SetCulture(LocalizationService.CurrentCulture);

            UpdateStatus(Loc.Instance["MsgInitializing"], ApplicationStatusKind.Busy);

            StartWithWindows = _autoStartService.IsEnabled();
            MinimizeToTray = _settings.MinimizeToTray;
            Lighting.IsEnabled = _settings.LightingEnabled;
            Lighting.Brightness = _settings.LightingBrightness;
            await Lighting.InitializeAsync();
            ApplySavedLightingZoneColors();
            Log.Info("Applying saved lighting state after startup initialization");
            await Lighting.ReapplyAsync();
            await Task.Delay(GpuLightingStartupSettleDelay);
            Log.Info("Re-applying saved GPU lighting state after controller startup settle");
            await Lighting.ReapplyGpuStateAsync();

            IsSupported = await _service.IsSupportedAsync();
            if (!IsSupported)
            {
                UpdateStatus(Loc.Instance["MsgNotSupported"], ApplicationStatusKind.Unsupported);
                return;
            }

            if (_service is WmiFanControlService)
                WmiFanControlService.LogAvailableWmiClasses();

            SelectedFanMode = await _service.GetSmartFanModeAsync();
            IsFullSpeed = await _service.GetFullSpeedAsync();
            IsFullSpeedSupported = await _service.IsFullSpeedSupportedAsync();

            var fans = await _service.DiscoverFansAsync();
            Fans.Clear();

            foreach (var zone in fans.GroupBy(info => info.FanId).OrderBy(group => group.Key))
            {
                var vm = new FanViewModel(_service, this, zone);
                if (_settings.FanNames.TryGetValue(vm.FanId, out var savedName) &&
                    !string.IsNullOrWhiteSpace(savedName))
                    vm.RestoreFanName(savedName);
                if (_settings.TemperatureHistory.TryGetValue(vm.FanId, out var savedHistory))
                    vm.RestoreTemperatureHistory(savedHistory);
                vm.FanNameChanged += OnFanNameChanged;
                var zoneCurve = _settings.GetOrDefaultCurve(zone.Key);
                vm.TargetSpeedPercentage = zoneCurve[^1] * 10;
                Fans.Add(vm);
            }
            UpdateSharedTemperaturePresentation();

            if (Fans.Count > 0)
            {
                UpdateStatus(LocalizationService.Get("MsgConnected", Fans.Count), ApplicationStatusKind.Connected);

                var conflicts = _service is WmiFanControlService
                    ? WmiFanControlService.GetRunningConflictingProcesses()
                    : (Environment.GetEnvironmentVariable(VisualTestFanControlService.ConflictEnvironmentVariable) ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                if (conflicts.Count > 0)
                {
                    ConflictWarning = LocalizationService.Get("MsgConflictWarning", string.Join(", ", conflicts));
                    StatusKind = ApplicationStatusKind.Warning;
                    Log.Warn($"Conflicting processes detected: {string.Join(", ", conflicts)}");
                }

                _timer.Start();

                if (_settings.Mode == SmartFanMode.Custom &&
                    SelectedFanMode != SmartFanMode.Custom &&
                    conflicts.Count == 0)
                {
                    Log.Info("Restoring saved Custom mode on startup");
                    await _service.SetSmartFanModeAsync(SmartFanMode.Custom);
                    SelectedFanMode = SmartFanMode.Custom;
                    OnPropertyChanged(nameof(IsCustomMode));
                }

                if (SelectedFanMode == SmartFanMode.Custom && !IsFullSpeed)
                {
                    await ReApplyFanCurvesAsync();
                }
            }
            else
            {
                UpdateStatus(Loc.Instance["MsgNoFans"], ApplicationStatusKind.Warning);
            }

            OnPropertyChanged(nameof(ShowNoFans));
        }
        catch (Exception ex)
        {
            UpdateStatus(LocalizationService.Get("MsgInitializationError", ex.Message), ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        if (!IsSupported || Fans.Count == 0) return;
        if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0) return;

        try
        {
            foreach (var fan in Fans)
            {
                foreach (var channel in fan.Channels)
                {
                    try
                    {
                        var rpm = await _service.GetFanSpeedAsync(channel.TelemetryId);
                        if (rpm >= 0)
                        {
                            channel.CurrentRpm = FanFirmwareCompatibility.NormalizeRpm(
                                rpm,
                                channel.MinRpm > 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(
                            $"Failed to refresh RPM for fan {fan.FanId} " +
                            $"(telemetry channel {channel.TelemetryId}): {ex.Message}");
                    }

                    try
                    {
                        var temperature = await _service.GetSensorTemperatureAsync(channel.SensorId);
                        if (FanFirmwareCompatibility.IsValidTemperature(temperature))
                            channel.Temperature = temperature;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Failed to refresh temperature for sensor {channel.SensorId}: {ex.Message}");
                    }
                }
                fan.RefreshSummary();
                if (fan.ShowTemperature && fan.Temperature is int currentTemperature)
                    fan.RecordTemperature(currentTemperature);
            }

            await RefreshSystemTemperaturesAsync();

            if (DateTime.UtcNow - _lastTemperatureHistorySaveUtc >= TimeSpan.FromMinutes(5))
            {
                SaveTemperatureHistory();
                _settingsService.Save(_settings);
                _lastTemperatureHistorySaveUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Telemetry refresh failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async Task RefreshSystemTemperaturesAsync()
    {
        try
        {
            var readings = await _systemTemperatureService.ReadAsync();
            foreach (var reading in readings)
            {
                var viewModel = SystemTemperatures.FirstOrDefault(item => item.SourceId == reading.Name);
                viewModel?.Update(reading);
            }

            // Lenovo's desktop GameZone firmware exposes a shared thermal sensor
            // for the chassis fans, but does not name it.  The current Legion T7
            // firmware reports it as sensor 2 and LibreHardwareMonitor exposes no
            // Super I/O sensor for the Lenovo 3784 board.  Use it only when a
            // named motherboard sensor is unavailable, and preserve the source
            // detail so it is not presented as a confirmed board-die reading.
            if (readings.FirstOrDefault(reading => reading.Name == "Motherboard")?.Celsius is null)
            {
                var fallback = FindFirmwareSystemTemperature();
                if (fallback != null)
                {
                    SystemTemperatures.FirstOrDefault(item => item.SourceId == "Motherboard")
                        ?.Update(fallback);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"System temperature refresh failed: {ex.Message}");
        }
    }

    private SystemTemperatureReading? FindFirmwareSystemTemperature()
    {
        var sensor = Fans
            .SelectMany(fan => fan.Channels)
            .Where(channel => channel.Temperature is int temperature &&
                              FanFirmwareCompatibility.IsValidTemperature(temperature))
            .GroupBy(channel => channel.SensorId)
            .Select(group => new
            {
                SensorId = group.Key,
                FanCount = group.Select(channel => channel.TelemetryId).Distinct().Count(),
                Celsius = (int)Math.Round(group.Average(channel => channel.Temperature!.Value))
            })
            .Where(sensor => sensor.FanCount >= 2)
            .OrderByDescending(sensor => sensor.FanCount)
            .ThenBy(sensor => sensor.SensorId)
            .FirstOrDefault();

        return sensor == null
            ? null
            : new SystemTemperatureReading(
                "Motherboard",
                sensor.Celsius,
                LocalizationService.Get("DetailFirmwareSharedSystemSensor", sensor.SensorId));
    }

    private void UpdateSharedTemperaturePresentation()
    {
        var sharedSensorIds = Fans
            .GroupBy(fan => fan.SensorId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var fan in Fans)
            fan.ShowTemperature = !sharedSensorIds.Contains(fan.SensorId);
    }

    public async Task ShutdownConflictingSoftwareAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await WmiFanControlService.ShutdownConflictingProcessesAsync();
            var remaining = WmiFanControlService.GetRunningConflictingProcesses();
            ConflictWarning = remaining.Count == 0
                ? ""
                : LocalizationService.Get("MsgConflictWarning", string.Join(", ", remaining));

            if (result.Failed.Count > 0 || remaining.Count > 0)
            {
                var names = result.Failed.Concat(remaining).Distinct().ToArray();
                UpdateStatus(
                    LocalizationService.Get("MsgConflictShutdownPartial", string.Join(", ", names)),
                    ApplicationStatusKind.Warning);
            }
            else
            {
                UpdateStatus(
                    LocalizationService.Get("MsgConflictShutdownSuccess", result.Stopped.Count),
                    ApplicationStatusKind.Connected);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus(
                LocalizationService.Get("MsgConflictShutdownError", ex.Message),
                ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenTemperatureChart(FanViewModel fan)
    {
        var chart = new TemperatureChartWindow(fan)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        chart.ShowDialog();
    }

    public void OpenTemperatureChart(SystemTemperatureViewModel temperature)
    {
        var chart = new TemperatureChartWindow(temperature)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        chart.ShowDialog();
    }

    public async Task ApplyModeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            EnsureNoConflictingSoftware();
            await _service.SetSmartFanModeAsync(SelectedFanMode);
            _settings.Mode = SelectedFanMode;
            _settingsService.Save(_settings);
            UpdateStatus(LocalizationService.Get("MsgModeSet", SelectedFanMode));
            OnPropertyChanged(nameof(IsCustomMode));

            if (SelectedFanMode == SmartFanMode.Custom && !IsFullSpeed && Fans.Count > 0)
            {
                await ReApplyFanCurvesAsync();
            }
        }
        catch (Exception ex)
        {
            UpdateStatus(LocalizationService.Get("MsgErrorSettingMode", ex.Message), ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task EnsureCustomModeAsync()
    {
        EnsureNoConflictingSoftware();
        if (SelectedFanMode != SmartFanMode.Custom)
        {
            SelectedFanMode = SmartFanMode.Custom;
            await _service.SetSmartFanModeAsync(SmartFanMode.Custom);
            OnPropertyChanged(nameof(IsCustomMode));
            _settings.Mode = SmartFanMode.Custom;
            _settingsService.Save(_settings);
        }
    }

    public async Task ToggleFullSpeedAsync(bool enabled)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            EnsureNoConflictingSoftware();
            IsFullSpeed = enabled;
            await _service.SetFullSpeedAsync(enabled);
            UpdateStatus(
                enabled ? Loc.Instance["MsgFullSpeedEnabled"] : Loc.Instance["MsgFullSpeedDisabled"],
                enabled ? ApplicationStatusKind.Warning : ApplicationStatusKind.Connected);
        }
        catch (Exception ex)
        {
            IsFullSpeed = !enabled;
            UpdateStatus(LocalizationService.Get("MsgError", ex.Message), ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResetAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (IsFullSpeedSupported)
                await _service.SetFullSpeedAsync(false);
            IsFullSpeed = false;

            SelectedFanMode = SmartFanMode.Balanced;
            await _service.SetSmartFanModeAsync(SmartFanMode.Balanced);

            foreach (var fan in Fans)
                fan.TargetSpeedPercentage = 50;

            _settings.Mode = SmartFanMode.Balanced;
            _settings.GlobalFanCurve = null;
            _settings.FanCurves.Clear();
            _settingsService.Save(_settings);

            OnPropertyChanged(nameof(IsCustomMode));
            UpdateStatus(Loc.Instance["MsgResetToDefaults"]);
        }
        catch (Exception ex)
        {
            UpdateStatus(LocalizationService.Get("MsgErrorResetting", ex.Message), ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReApplyFanCurvesAsync()
    {
        try
        {
            foreach (var fan in Fans)
            {
                var curve = _settings.GetOrDefaultCurve(fan.FanId);
                if (curve.Length == 10)
                    await _service.SetFanTableAsync(fan.FanId, curve);
            }
            if (Fans.Count > 0)
                UpdateStatus(LocalizationService.Get("MsgReappliedCurve", Fans.Count));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to re-apply saved fan curve", ex);
            UpdateStatus(LocalizationService.Get("MsgError", ex.Message), ApplicationStatusKind.Error);
        }
    }

    public void OpenCurveEditor(FanViewModel fan)
    {
        var currentCurve = GetFanCurve(fan.FanId);
        var editor = new FanCurveEditorWindow(
            fan.FanName,
            currentCurve,
            fan.MinRpm,
            fan.MaxRpm,
            fan.HasFirmwareRpmRange);
        if (editor.ShowDialog() == true)
        {
            var newCurve = editor.ResultCurve;
            if (newCurve != null && newCurve.Length == 10)
            {
                _ = fan.ApplyCurveAsync(newCurve);
            }
        }
    }

    public void SaveFanCurve(int fanId, byte[] curve)
    {
        _settings.SetCurve(fanId, curve);
        _settingsService.Save(_settings);
    }

    public byte[] GetFanCurve(int fanId) => _settings.GetOrDefaultCurve(fanId);

    public void ToggleAutoStart()
    {
        try
        {
            if (StartWithWindows)
            {
                var exePath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    _autoStartService.Enable(exePath);
                    Log.Info($"Auto-start enabled: {exePath}");
                    UpdateStatus(Loc.Instance["MsgStartWithWindowsEnabled"]);
                }
            }
            else
            {
                _autoStartService.Disable();
                Log.Info("Auto-start disabled");
                UpdateStatus(Loc.Instance["MsgStartWithWindowsDisabled"]);
            }
            _settings.StartWithWindows = StartWithWindows;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error("ToggleAutoStart failed", ex);
            UpdateStatus(LocalizationService.Get("MsgErrorTogglingAutostart", ex.Message), ApplicationStatusKind.Error);
        }
    }

    public void UpdateStatus(
        string message,
        ApplicationStatusKind kind = ApplicationStatusKind.Connected)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    private void EnsureNoConflictingSoftware()
    {
        if (_service is not WmiFanControlService)
            return;

        var conflicts = WmiFanControlService.GetRunningConflictingProcesses();
        ConflictWarning = conflicts.Count == 0
            ? ""
            : LocalizationService.Get("MsgConflictWarning", string.Join(", ", conflicts));
        if (conflicts.Count > 0)
            throw new InvalidOperationException(ConflictWarning);
    }

    public void StopTimer()
    {
        _timer.Stop();
        SaveLightingSettings();
        SaveTemperatureHistory();
        _settingsService.Save(_settings);
        Lighting.Dispose();
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        _timer.Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (IsSupported && SelectedFanMode == SmartFanMode.Custom)
        {
            try
            {
                await _service.SetSmartFanModeAsync(SmartFanMode.Balanced);
                Log.Info("Restored to Balanced mode on exit");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to restore Balanced mode on exit", ex);
            }
        }

        await Lighting.PersistStateAsync();
        SaveLightingSettings();
        SaveTemperatureHistory();
        _settingsService.Save(_settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnLightingApplied(object? sender, EventArgs e)
    {
        SaveLightingSettings();
        _settingsService.Save(_settings);
    }

    private void OnLightingSettingsChanged(object? sender, EventArgs e)
    {
        SaveLightingSettings();
        _settingsService.Save(_settings);
    }

    private void OnFanNameChanged(object? sender, EventArgs e)
    {
        if (sender is not FanViewModel fan)
            return;

        if (fan.HasCustomName)
            _settings.FanNames[fan.FanId] = fan.FanName;
        else
            _settings.FanNames.Remove(fan.FanId);
        _settingsService.Save(_settings);
    }

    private void ApplySavedLightingZoneColors()
    {
        Lighting.RestoreZoneState(() =>
        {
            Lighting.RestoreZoneNames(_settings.LightingZoneNames);
            Lighting.RestoreZoneBrightness(_settings.LightingZoneBrightness);

            foreach (var zone in Lighting.Zones)
                zone.IsEnabled = !_settings.LightingZoneEnabled.TryGetValue(zone.Index, out var enabled) || enabled;

            if (_settings.LightingZoneColors.Count == 0)
                return;

            var savedColors = _settings.LightingZoneColors.Values
                .Select(color => (color.Red, color.Green, color.Blue))
                .Distinct()
                .ToArray();
            var inheritedGlobalColor = savedColors.Length == 1
                ? Lighting.Colors.FirstOrDefault(color =>
                    color.Red == savedColors[0].Red &&
                    color.Green == savedColors[0].Green &&
                    color.Blue == savedColors[0].Blue)
                : null;

            foreach (var zone in Lighting.Zones)
            {
                if (!_settings.LightingZoneColors.TryGetValue(zone.Index, out var saved))
                {
                    // A newly supported device (for example the GPU lighting zone) inherits
                    // an existing all-zones color during the first run after upgrading.
                    if (inheritedGlobalColor != null)
                        zone.SelectedColor = inheritedGlobalColor;
                    continue;
                }

                var match = Lighting.Colors.FirstOrDefault(c =>
                    c.Red == saved.Red && c.Green == saved.Green && c.Blue == saved.Blue);
                zone.SelectedColor = match;
            }

            var firstColor = Lighting.Zones.FirstOrDefault()?.SelectedColor;
            var hasSingleGlobalColor = firstColor != null && Lighting.Zones.All(zone =>
                zone.SelectedColor is { } color &&
                color.Red == firstColor.Red &&
                color.Green == firstColor.Green &&
                color.Blue == firstColor.Blue);
            Lighting.RestoreGlobalColorSelection(hasSingleGlobalColor ? firstColor : null);
        });
    }

    private void SaveLightingSettings()
    {
        _settings.LightingEnabled = Lighting.IsEnabled;
        _settings.LightingBrightness = Lighting.Brightness;
        _settings.LightingZoneColors.Clear();
        _settings.LightingZoneBrightness.Clear();
        _settings.LightingZoneEnabled.Clear();
        _settings.LightingZoneNames.Clear();
        foreach (var zone in Lighting.Zones)
        {
            _settings.LightingZoneBrightness[zone.Index] = zone.Brightness;
            _settings.LightingZoneEnabled[zone.Index] = zone.IsEnabled;
            if (!string.Equals(zone.Name, zone.DefaultName, StringComparison.Ordinal))
                _settings.LightingZoneNames[zone.Index] = zone.Name;
            if (zone.SelectedColor == null) continue;
            _settings.LightingZoneColors[zone.Index] =
                new LightingZoneColor(zone.Index, zone.SelectedColor.Red, zone.SelectedColor.Green, zone.SelectedColor.Blue);
        }
    }

    private void SaveTemperatureHistory()
    {
        _settings.TemperatureHistory.Clear();
        foreach (var fan in Fans)
        {
            if (!fan.ShowTemperature)
                continue;
            fan.TemperatureHistory.Compact(DateTime.UtcNow);
            if (fan.TemperatureHistory.Samples.Count > 0)
                _settings.TemperatureHistory[fan.FanId] = fan.TemperatureHistory;
        }

        _settings.SystemTemperatureHistory.Clear();
        foreach (var temperature in SystemTemperatures)
        {
            temperature.TemperatureHistory.Compact(DateTime.UtcNow);
            if (temperature.TemperatureHistory.Samples.Count > 0)
                _settings.SystemTemperatureHistory[temperature.SourceId] = temperature.TemperatureHistory;
        }
    }
}
