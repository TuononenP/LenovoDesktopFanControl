using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

internal sealed class FakeFanControlService : IWmiFanControlService
{
    public bool IsSupported { get; set; } = true;
    public bool IsFullSpeedSupported { get; set; } = true;
    public SmartFanMode SmartFanMode { get; set; } = SmartFanMode.Balanced;
    public bool FullSpeed { get; set; }
    public List<FanInfo> DiscoveredFans { get; set; } = [];
    public Dictionary<int, int> FanSpeeds { get; } = [];
    public Dictionary<int, int> Temperatures { get; } = [];
    public Exception? IsSupportedException { get; set; }
    public Exception? SetModeException { get; set; }
    public Exception? SetFullSpeedException { get; set; }
    public Exception? SetFanTableException { get; set; }
    public List<SmartFanMode> SetModeCalls { get; } = [];
    public List<bool> SetFullSpeedCalls { get; } = [];
    public List<(int FanId, byte[] Curve)> SetFanTableCalls { get; } = [];
    public bool IsDisposed { get; private set; }

    public Task<bool> IsSupportedAsync() => IsSupportedException == null
        ? Task.FromResult(IsSupported)
        : Task.FromException<bool>(IsSupportedException);

    public Task<bool> IsFullSpeedSupportedAsync() => Task.FromResult(IsFullSpeedSupported);

    public Task<SmartFanMode> GetSmartFanModeAsync() => Task.FromResult(SmartFanMode);

    public Task SetSmartFanModeAsync(SmartFanMode mode)
    {
        SetModeCalls.Add(mode);
        if (SetModeException != null)
            return Task.FromException(SetModeException);
        SmartFanMode = mode;
        return Task.CompletedTask;
    }

    public Task<List<FanInfo>> DiscoverFansAsync() => Task.FromResult(DiscoveredFans);

    public Task<int> GetFanSpeedAsync(int fanId) => Task.FromResult(FanSpeeds[fanId]);

    public Task<int> GetSensorTemperatureAsync(int sensorId) => Task.FromResult(Temperatures[sensorId]);

    public Task SetFanTableAsync(int fanId, byte[] fanTable)
    {
        SetFanTableCalls.Add((fanId, [.. fanTable]));
        return SetFanTableException == null
            ? Task.CompletedTask
            : Task.FromException(SetFanTableException);
    }

    public Task<bool> GetFullSpeedAsync() => Task.FromResult(FullSpeed);

    public Task SetFullSpeedAsync(bool enabled)
    {
        SetFullSpeedCalls.Add(enabled);
        if (SetFullSpeedException != null)
            return Task.FromException(SetFullSpeedException);
        FullSpeed = enabled;
        return Task.CompletedTask;
    }

    public void Dispose() => IsDisposed = true;
}

internal sealed class InMemorySettingsService(FanSettings? settings = null) : ISettingsService
{
    public FanSettings Settings { get; set; } = settings ?? new FanSettings();
    public int LoadCount { get; private set; }
    public int SaveCount { get; private set; }

    public FanSettings Load()
    {
        LoadCount++;
        return Settings;
    }

    public void Save(FanSettings settings)
    {
        SaveCount++;
        Settings = settings;
    }
}

internal sealed class FakeAutoStartService : IAutoStartService
{
    public bool Enabled { get; set; }
    public List<string> EnabledPaths { get; } = [];
    public int DisableCount { get; private set; }

    public bool IsEnabled() => Enabled;

    public void Enable(string executablePath)
    {
        Enabled = true;
        EnabledPaths.Add(executablePath);
    }

    public void Disable()
    {
        Enabled = false;
        DisableCount++;
    }
}

internal sealed class FakeLightingControlService : ILightingControlService
{
    public LightingDeviceInfo? Device { get; set; }
    public Exception? DiscoverException { get; set; }
    public List<bool> EnabledCalls { get; } = [];
    public List<double> BrightnessCalls { get; } = [];
    public List<(byte Red, byte Green, byte Blue)> ColorCalls { get; } = [];
    public List<(int ZoneIndex, byte Red, byte Green, byte Blue)> ZoneColorCalls { get; } = [];
    public List<(int ZoneIndex, double Brightness)> ZoneBrightnessCalls { get; } = [];
    public List<(int ZoneIndex, bool Enabled)> ZoneEnabledCalls { get; } = [];
    public bool IsDisposed { get; private set; }
    public int PersistStateCount { get; private set; }

    public bool IsControlAvailable { get; set; } = true;
    public event EventHandler? AvailabilityChanged;

    public TaskCompletionSource<object?>? BrightnessGate { get; set; }

    public void RaiseAvailabilityChanged() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    public Task<LightingDeviceInfo?> DiscoverAsync() => DiscoverException == null
        ? Task.FromResult(Device)
        : Task.FromException<LightingDeviceInfo?>(DiscoverException);

    public async Task SetEnabledAsync(bool enabled)
    {
        EnabledCalls.Add(enabled);
        if (BrightnessGate != null)
            await BrightnessGate.Task;
    }

    public Task SetBrightnessAsync(double brightness)
    {
        BrightnessCalls.Add(brightness);
        return Task.CompletedTask;
    }

    public Task SetColorAsync(byte red, byte green, byte blue)
    {
        ColorCalls.Add((red, green, blue));
        return Task.CompletedTask;
    }

    public Task SetZoneColorAsync(int zoneIndex, byte red, byte green, byte blue)
    {
        ZoneColorCalls.Add((zoneIndex, red, green, blue));
        return Task.CompletedTask;
    }

    public Task SetZoneBrightnessAsync(int zoneIndex, double brightness)
    {
        ZoneBrightnessCalls.Add((zoneIndex, brightness));
        return Task.CompletedTask;
    }

    public Task SetZoneEnabledAsync(int zoneIndex, bool enabled)
    {
        ZoneEnabledCalls.Add((zoneIndex, enabled));
        return Task.CompletedTask;
    }

    public Task PersistStateAsync()
    {
        PersistStateCount++;
        return Task.CompletedTask;
    }

    public void Dispose() => IsDisposed = true;
}
