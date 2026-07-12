using System.ComponentModel;
using System.Runtime.CompilerServices;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.ViewModels;

public sealed class FanChannelViewModel : INotifyPropertyChanged
{
    private int? _currentRpm;
    private int? _temperature;

    public int TelemetryId { get; }
    public int SensorId { get; }
    public string Name { get; }
    public int MaxRpm { get; }
    public int MinRpm { get; }

    public int? CurrentRpm
    {
        get => _currentRpm;
        set { _currentRpm = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedPercentage)); }
    }

    public int? Temperature
    {
        get => _temperature;
        set { _temperature = value; OnPropertyChanged(); }
    }

    public int SpeedPercentage => MaxRpm > 0 && CurrentRpm is int rpm
        ? Math.Clamp((int)Math.Round((double)rpm / MaxRpm * 100), 0, 100)
        : 0;

    public FanChannelViewModel(FanInfo info)
    {
        TelemetryId = info.TelemetryId >= 0 ? info.TelemetryId : info.FanId;
        SensorId = info.SensorId;
        Name = info.Name;
        MaxRpm = info.MaxRpm;
        MinRpm = info.MinRpm;
        _currentRpm = info.CurrentRpm;
        _temperature = info.Temperature;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
