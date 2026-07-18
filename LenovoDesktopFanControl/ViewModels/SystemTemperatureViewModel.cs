using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.ViewModels;

public sealed class SystemTemperatureViewModel : INotifyPropertyChanged
{
    private int? _temperature;
    private string _detail = "";
    public string Name { get; }
    public TemperatureHistory TemperatureHistory { get; } = new();
    public ICommand OpenTemperatureChartCommand { get; }
    public int? Temperature { get => _temperature; private set { _temperature = value; OnPropertyChanged(); OnPropertyChanged(nameof(TemperatureText)); } }
    public string Detail { get => _detail; private set { _detail = value; OnPropertyChanged(); } }
    public string TemperatureText => Temperature is int value ? $"{value} °C" : "—";
    public SystemTemperatureViewModel(string name, Action<SystemTemperatureViewModel>? openTemperatureChart = null)
    {
        Name = name;
        OpenTemperatureChartCommand = new RelayCommand(() => openTemperatureChart?.Invoke(this));
    }

    public void Update(SystemTemperatureReading reading)
    {
        Temperature = reading.Celsius;
        Detail = reading.Detail;
        if (reading.Celsius is int celsius)
            TemperatureHistory.Add(DateTime.UtcNow, celsius);
    }

    internal void RestoreTemperatureHistory(TemperatureHistory history)
    {
        TemperatureHistory.Samples = [.. history.Samples];
        TemperatureHistory.Compact(DateTime.UtcNow);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
