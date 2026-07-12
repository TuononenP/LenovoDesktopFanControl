using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public interface IWmiFanControlService : IDisposable
{
    Task<bool> IsSupportedAsync();
    Task<bool> IsFullSpeedSupportedAsync();
    Task<SmartFanMode> GetSmartFanModeAsync();
    Task SetSmartFanModeAsync(SmartFanMode mode);
    Task<List<FanInfo>> DiscoverFansAsync();
    Task<int> GetFanSpeedAsync(int fanId);
    Task<int> GetSensorTemperatureAsync(int sensorId);
    Task SetFanTableAsync(int fanId, byte[] fanTable);
    Task<bool> GetFullSpeedAsync();
    Task SetFullSpeedAsync(bool enabled);
}
