namespace LenovoDesktopFanControl.Models;

public sealed record LightingDeviceInfo(
    string Name,
    string DeviceId,
    int LampCount,
    ushort VendorId,
    ushort ProductId);

public sealed record LightingColorOption(string Name, byte Red, byte Green, byte Blue);
