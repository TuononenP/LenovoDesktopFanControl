namespace LenovoDesktopFanControl.Models;

public class FanInfo
{
    public int FanId { get; init; }
    public int TelemetryId { get; init; } = -1;
    public int SensorId { get; init; }
    public string Name { get; init; } = "";
    public string? NameResourceKey { get; init; }
    public object? NameResourceArgument { get; init; }
    public int? CurrentRpm { get; set; }
    public int? Temperature { get; set; }
    public bool IsAvailable { get; set; }
    public int MaxRpm { get; set; } = 2500;
    public int MinRpm { get; set; }
}
