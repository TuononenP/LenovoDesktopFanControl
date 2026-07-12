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
    private readonly string? _nameResourceKey;
    private readonly object? _nameResourceArgument;

    public int FanId { get; }
    public int TelemetryId { get; }
    public int SensorId { get; }
    public string FanName { get; private set; }
    public int MaxRpm { get; }
    public int MinRpm { get; }
    public ObservableCollection<FanChannelViewModel> Channels { get; } = [];
    public bool HasMultipleChannels => Channels.Count > 1;

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
        set { _targetSpeedPercentage = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public ICommand ApplySpeedCommand { get; }
    public ICommand EditCurveCommand { get; }
    public ICommand ApplyCurveCommand { get; }

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
        FanName = infos.Count > 1 && info.NameResourceKey != "FanNamePump"
            ? LocalizationService.Get("FanNameN", info.FanId)
            : info.Name;
        _nameResourceKey = info.NameResourceKey;
        _nameResourceArgument = info.NameResourceArgument;
        MaxRpm = info.MaxRpm;
        MinRpm = info.MinRpm;
        _currentRpm = info.CurrentRpm;
        _temperature = info.Temperature;
        foreach (var channel in infos)
            Channels.Add(new FanChannelViewModel(channel));
        RefreshSummary();

        ApplySpeedCommand = new RelayCommand(async () => await ApplySpeedAsync());
        EditCurveCommand = new RelayCommand(() => _parent.OpenCurveEditor(this));
        ApplyCurveCommand = new RelayCommand(async () => await ApplyCurveAsync());
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

        FanName = _nameResourceArgument == null
            ? LocalizationService.Get(_nameResourceKey)
            : LocalizationService.Get(_nameResourceKey, _nameResourceArgument);
        OnPropertyChanged(nameof(FanName));
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
