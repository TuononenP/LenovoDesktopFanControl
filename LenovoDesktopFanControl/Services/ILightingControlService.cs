using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public interface ILightingControlService : IDisposable
{
    Task<LightingDeviceInfo?> DiscoverAsync();
    Task SetEnabledAsync(bool enabled);
    Task SetBrightnessAsync(double brightness);
    Task SetColorAsync(byte red, byte green, byte blue);
    Task SetZoneColorAsync(int zoneIndex, byte red, byte green, byte blue);
    Task SetZoneBrightnessAsync(int zoneIndex, double brightness);
    Task SetZoneEnabledAsync(int zoneIndex, bool enabled);
    Task ReapplyGpuStateAsync() => Task.CompletedTask;
    Task PersistStateAsync();

    bool IsControlAvailable { get; }

    event EventHandler? AvailabilityChanged;
}
