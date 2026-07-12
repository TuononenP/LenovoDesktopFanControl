namespace LenovoDesktopFanControl.Models;

public sealed record LightingDeviceInfo(
    string Name,
    string DeviceId,
    int LampCount,
    ushort VendorId,
    ushort ProductId,
    IReadOnlyList<LightingZoneInfo> Zones);

public sealed record LightingZoneInfo(
    int Index,
    string Name,
    LightingZoneKind Kind,
    int LampCount,
    IReadOnlyList<int> LampIndices);

public enum LightingZoneKind
{
    Undefined,
    Control,
    Accent,
    Branding,
    Status,
    Illumination,
    Presentation,
    Mixed
}

public sealed record LightingColorOption(string Name, byte Red, byte Green, byte Blue);

public sealed record LightingZoneColor(int ZoneIndex, byte Red, byte Green, byte Blue);