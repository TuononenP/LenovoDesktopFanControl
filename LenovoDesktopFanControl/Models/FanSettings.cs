namespace LenovoDesktopFanControl.Models;

public class FanSettings
{
    public SmartFanMode Mode { get; set; } = SmartFanMode.Balanced;
    public byte[]? GlobalFanCurve { get; set; }
    public Dictionary<int, byte[]> FanCurves { get; set; } = new();
    public int PollingIntervalMs { get; set; } = 2000;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public string Language { get; set; } = "en";

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
}
