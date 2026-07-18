using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

/// <summary>
/// Provides deterministic fan data only when explicitly enabled for runtime visual testing.
/// Production launches continue to use <see cref="WmiFanControlService"/>.
/// </summary>
internal sealed class VisualTestFanControlService(int fanCount) : IWmiFanControlService
{
    internal const string FanCountEnvironmentVariable = "LENOVO_FAN_CONTROL_VISUAL_TEST_FANS";
    internal const string ConflictEnvironmentVariable = "LENOVO_FAN_CONTROL_VISUAL_TEST_CONFLICTS";

    private readonly int _fanCount = Math.Clamp(fanCount, 0, 8);
    private SmartFanMode _mode = SmartFanMode.Balanced;
    private bool _isFullSpeed;

    public static bool TryCreate(out IWmiFanControlService? service)
    {
        service = null;
        var value = Environment.GetEnvironmentVariable(FanCountEnvironmentVariable);
        if (!int.TryParse(value, out var fanCount))
            return false;

        service = new VisualTestFanControlService(fanCount);
        return true;
    }

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<bool> IsFullSpeedSupportedAsync() => Task.FromResult(true);

    public Task<SmartFanMode> GetSmartFanModeAsync() => Task.FromResult(_mode);

    public Task SetSmartFanModeAsync(SmartFanMode mode)
    {
        _mode = mode;
        return Task.CompletedTask;
    }

    public Task<List<FanInfo>> DiscoverFansAsync()
    {
        var fans = Enumerable.Range(0, _fanCount)
            .Select(index => new FanInfo
            {
                FanId = index,
                SensorId = index,
                Name = GetFanName(index),
                NameResourceKey = index switch
                {
                    0 => "FanNameCpu",
                    1 => "FanNameGpu",
                    2 => "FanNameChassis",
                    _ => "FanNameN"
                },
                NameResourceArgument = index < 3 ? null : index + 1,
                CurrentRpm = 920 + index * 280,
                Temperature = 42 + index * 5,
                IsAvailable = true,
                MaxRpm = 2500 + index * 250,
                MinRpm = 500,
                HasFirmwareRpmRange = true
            })
            .ToList();
        return Task.FromResult(fans);
    }

    public Task<int> GetFanSpeedAsync(int fanId) => Task.FromResult(920 + fanId * 280);

    public Task<int> GetSensorTemperatureAsync(int sensorId) => Task.FromResult(42 + sensorId * 5);

    public Task SetFanTableAsync(int fanId, byte[] fanTable) => Task.CompletedTask;

    public Task<bool> GetFullSpeedAsync() => Task.FromResult(_isFullSpeed);

    public Task SetFullSpeedAsync(bool enabled)
    {
        _isFullSpeed = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    private static string GetFanName(int index) => index switch
    {
        0 => LocalizationService.Get("FanNameCpu"),
        1 => LocalizationService.Get("FanNameGpu"),
        2 => LocalizationService.Get("FanNameChassis"),
        _ => LocalizationService.Get("FanNameN", index + 1)
    };
}
