using System.Text.Json.Serialization;

namespace LenovoDesktopFanControl.Models;

public class FanSettings
{
    public SmartFanMode Mode { get; set; } = SmartFanMode.Balanced;
    public byte[]? GlobalFanCurve { get; set; }
    public Dictionary<int, byte[]> FanCurves { get; set; } = new();
    public Dictionary<int, string> FanNames { get; set; } = new();
    public Dictionary<int, TemperatureHistory> TemperatureHistory { get; set; } = new();
    public Dictionary<string, TemperatureHistory> SystemTemperatureHistory { get; set; } = new();
    public int PollingIntervalMs { get; set; } = 2000;
    // The background lighting host starts at sign-in unless the user opts out.
    public bool StartWithWindows { get; set; } = true;
    // Retained only to migrate settings written by versions that kept a
    // separate background-host setting. New settings use StartWithWindows.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? KeepLightingActiveInBackground { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    // An empty value means language has not been chosen yet. It is resolved from
    // the Windows display language on first launch and then persisted.
    public string Language { get; set; } = string.Empty;
    public bool LightingEnabled { get; set; } = true;
    public int LightingBrightness { get; set; } = 100;
    public Dictionary<int, LightingZoneColor> LightingZoneColors { get; set; } = new();
    public Dictionary<int, int> LightingZoneBrightness { get; set; } = new();
    public Dictionary<int, bool> LightingZoneEnabled { get; set; } = new();
    public Dictionary<int, string> LightingZoneNames { get; set; } = new();

    public byte[] GetOrDefaultCurve(int fanId)
    {
        if (FanCurves.TryGetValue(fanId, out var curve) &&
            new FanTable { Speeds = curve }.IsValid())
            return curve;
        if (GlobalFanCurve is { Length: FanTable.PointCount } globalCurve &&
            new FanTable { Speeds = globalCurve }.IsValid())
            return globalCurve;
        return FanTable.Default().Speeds;
    }

    public void SetCurve(int fanId, byte[] curve)
    {
        if (!new FanTable { Speeds = curve }.IsValid())
            throw new ArgumentException("Invalid fan curve", nameof(curve));

        FanCurves[fanId] = [.. curve];
    }

    public void SetGlobalCurve(byte[] curve)
    {
        if (!new FanTable { Speeds = curve }.IsValid())
            throw new ArgumentException("Invalid global fan curve", nameof(curve));

        GlobalFanCurve = [.. curve];
        FanCurves.Clear();
    }

    public bool MigrateLegacyBackgroundLightingSetting()
    {
        if (KeepLightingActiveInBackground is not { } keepLightingActive)
            return false;

        StartWithWindows = keepLightingActive;
        KeepLightingActiveInBackground = null;
        return true;
    }
}
