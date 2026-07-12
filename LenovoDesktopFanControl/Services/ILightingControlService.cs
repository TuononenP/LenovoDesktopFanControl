using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public interface ILightingControlService : IDisposable
{
    Task<LightingDeviceInfo?> DiscoverAsync();
    Task SetEnabledAsync(bool enabled);
    Task SetBrightnessAsync(double brightness);
    Task SetColorAsync(byte red, byte green, byte blue);
}
