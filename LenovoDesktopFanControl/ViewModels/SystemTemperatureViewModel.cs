using System.ComponentModel;
using System.Runtime.CompilerServices;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.ViewModels;

public sealed class SystemTemperatureViewModel : INotifyPropertyChanged
{
    private int? _temperature;
    private string _detail = "";
    public string Name { get; }
    public int? Temperature { get => _temperature; private set { _temperature = value; OnPropertyChanged(); OnPropertyChanged(nameof(TemperatureText)); } }
    public string Detail { get => _detail; private set { _detail = value; OnPropertyChanged(); } }
    public string TemperatureText => Temperature is int value ? $"{value} °C" : "—";
    public SystemTemperatureViewModel(string name) => Name = name;
    public void Update(SystemTemperatureReading reading) { Temperature = reading.Celsius; Detail = reading.Detail; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
