using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.ViewModels;

public class FanViewModel : INotifyPropertyChanged
{
    private readonly IWmiFanControlService _service;
    private readonly MainViewModel _parent;

    private int? _currentRpm;
    private int? _temperature;
    private int _targetSpeedPercentage = 50;
    private bool _isBusy;
    private string _fanName = "";
    private bool _hasCustomName;
    private bool _isEditingName;
    private bool _showTemperature = true;
    private readonly string? _nameResourceKey;
    private readonly object? _nameResourceArgument;

    public int FanId { get; }
    public int TelemetryId { get; }
    public int SensorId { get; }
    public string DefaultFanName { get; private set; } = "";
    public string FanName
    {
        get => _fanName;
        set => SetFanName(value, notifyChange: true);
    }
    public bool HasCustomName => _hasCustomName;
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
    public int MaxRpm { get; }
    public int MinRpm { get; }
    public bool HasFirmwareRpmRange { get; }
    public string FirmwareRpmRange => MinRpm > 0
        ? LocalizationService.Get("FirmwareRpmRange", MinRpm, MaxRpm)
        : LocalizationService.Get("FirmwareMaxRpm", MaxRpm);
    public ObservableCollection<FanChannelViewModel> Channels { get; } = [];
    public TemperatureHistory TemperatureHistory { get; } = new();
    public bool HasMultipleChannels => Channels.Count > 1;

    public bool ShowTemperature
    {
        get => _showTemperature;
        internal set
        {
            if (_showTemperature == value) return;
            _showTemperature = value;
            OnPropertyChanged();
        }
    }

    public int? CurrentRpm
    {
        get => _currentRpm;
        set { _currentRpm = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedPercentage)); }
    }

    public int SpeedPercentage => MaxRpm > 0 && CurrentRpm is int rpm
        ? Math.Clamp((int)Math.Round((double)rpm / MaxRpm * 100), 0, 100)
        : 0;

    public int? Temperature
    {
        get => _temperature;
        set { _temperature = value; OnPropertyChanged(); }
    }

    public int TargetSpeedPercentage
    {
        get => _targetSpeedPercentage;
        set
        {
            var clamped = Math.Clamp(value, FanTable.MinimumTargetPercentage, 100);
            if (_targetSpeedPercentage == clamped) return;
            _targetSpeedPercentage = clamped;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public ICommand ApplySpeedCommand { get; }
    public ICommand EditCurveCommand { get; }
    public ICommand ApplyCurveCommand { get; }
    public ICommand EditNameCommand { get; }
    public ICommand OpenTemperatureChartCommand { get; }

    public FanViewModel(IWmiFanControlService service, MainViewModel parent, FanInfo info)
        : this(service, parent, [info])
    {
    }

    public FanViewModel(
        IWmiFanControlService service,
        MainViewModel parent,
        IEnumerable<FanInfo> channelInfos)
    {
        var infos = channelInfos.ToList();
        if (infos.Count == 0)
            throw new ArgumentException("A fan zone must contain at least one channel", nameof(channelInfos));
        var info = infos[0];
        _service = service;
        _parent = parent;
        FanId = info.FanId;
        TelemetryId = info.TelemetryId >= 0 ? info.TelemetryId : info.FanId;
        SensorId = info.SensorId;
        DefaultFanName = infos.Count > 1 && (info.NameResourceKey is null or "FanNameN")
            ? LocalizationService.Get("FanNameN", info.FanId)
            : info.Name;
        _fanName = DefaultFanName;
        _nameResourceKey = infos.Count > 1 && info.NameResourceKey == "FanNameN"
            ? "FanNameN"
            : info.NameResourceKey;
        _nameResourceArgument = infos.Count > 1 && info.NameResourceKey == "FanNameN"
            ? info.FanId
            : info.NameResourceArgument;
        MaxRpm = info.MaxRpm;
        MinRpm = info.MinRpm;
        HasFirmwareRpmRange = info.HasFirmwareRpmRange;
        _currentRpm = info.CurrentRpm;
        _temperature = info.Temperature;
        foreach (var channel in infos)
            Channels.Add(new FanChannelViewModel(channel));
        RefreshSummary();

        ApplySpeedCommand = new RelayCommand(async () => await ApplySpeedAsync());
        EditCurveCommand = new RelayCommand(() => _parent.OpenCurveEditor(this));
        ApplyCurveCommand = new RelayCommand(async () => await ApplyCurveAsync());
        EditNameCommand = new RelayCommand(() => IsEditingName = true);
        OpenTemperatureChartCommand = new RelayCommand(() => _parent.OpenTemperatureChart(this));
    }

    public void RefreshSummary()
    {
        var rpms = Channels.Where(channel => channel.CurrentRpm.HasValue)
            .Select(channel => channel.CurrentRpm!.Value)
            .ToArray();
        CurrentRpm = rpms.Length == 0 ? null : (int)Math.Round(rpms.Average());
        Temperature = Channels.Select(channel => channel.Temperature).FirstOrDefault(value => value.HasValue);
    }

    public void RefreshLocalizedName()
    {
        if (string.IsNullOrEmpty(_nameResourceKey))
            return;

        DefaultFanName = _nameResourceArgument == null
            ? LocalizationService.Get(_nameResourceKey)
            : LocalizationService.Get(_nameResourceKey, _nameResourceArgument);
        if (!_hasCustomName)
        {
            _fanName = DefaultFanName;
            OnPropertyChanged(nameof(FanName));
        }
        OnPropertyChanged(nameof(FirmwareRpmRange));
    }

    internal void RestoreFanName(string name) => SetFanName(name, notifyChange: false);

    internal void RestoreTemperatureHistory(TemperatureHistory history)
    {
        TemperatureHistory.Samples = [.. history.Samples];
        TemperatureHistory.Compact(DateTime.UtcNow);
    }

    public void RecordTemperature(int temperature) => TemperatureHistory.Add(DateTime.UtcNow, temperature);

    private void SetFanName(string? value, bool notifyChange)
    {
        var name = string.IsNullOrWhiteSpace(value) ? DefaultFanName : value.Trim();
        if (name.Length > 48)
            name = name[..48];
        var hasCustomName = !string.Equals(name, DefaultFanName, StringComparison.Ordinal);
        if (_fanName == name && _hasCustomName == hasCustomName)
        {
            if (!string.Equals(value, name, StringComparison.Ordinal))
                OnPropertyChanged(nameof(FanName));
            return;
        }

        _fanName = name;
        _hasCustomName = hasCustomName;
        OnPropertyChanged(nameof(FanName));
        OnPropertyChanged(nameof(HasCustomName));
        if (notifyChange)
            FanNameChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ApplySpeedAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _parent.EnsureCustomModeAsync();
            var table = FanTable.FromPercentage(TargetSpeedPercentage);
            await _service.SetFanTableAsync(FanId, table.Speeds);
            _parent.SaveFanCurve(FanId, table.Speeds);
            _parent.UpdateStatus(LocalizationService.Get("MsgAppliedSpeed", TargetSpeedPercentage, FanName));
        }
        catch (Exception ex)
        {
            _parent.UpdateStatus(
                LocalizationService.Get("MsgError", ex.Message),
                ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyCurveAsync(byte[] curve)
    {
        if (curve.Length != 10) return;
        IsBusy = true;
        try
        {
            await _parent.EnsureCustomModeAsync();
            await _service.SetFanTableAsync(FanId, curve);
            _parent.SaveFanCurve(FanId, curve);
            _parent.UpdateStatus(LocalizationService.Get("MsgAppliedCurve", FanName));
        }
        catch (Exception ex)
        {
            _parent.UpdateStatus(
                LocalizationService.Get("MsgError", ex.Message),
                ApplicationStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyCurveAsync()
    {
        var curve = _parent.GetFanCurve(FanId);
        await ApplyCurveAsync(curve);
    }

    public event EventHandler? FanNameChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
