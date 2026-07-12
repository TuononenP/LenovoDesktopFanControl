using LenovoDesktopFanControl.Models;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using WinColor = Windows.UI.Color;

namespace LenovoDesktopFanControl.Services;

public sealed class LampArrayLightingService : ILightingControlService
{
    private const ushort LenovoVendorId = 0x17EF;
    private const ushort T7LightingProductId = 0xC955;
    private LampArray? _lampArray;
    private WinColor _lastColor = WinColor.FromArgb(255, 91, 157, 255);
    private double _lastBrightness = 1;
    private List<LightingZoneInfo> _zones = [];
    private readonly Dictionary<int, (byte R, byte G, byte B)> _zoneColors = [];

    public async Task<LightingDeviceInfo?> DiscoverAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(LampArray.GetDeviceSelector());
            foreach (var device in devices)
            {
                var lampArray = await LampArray.FromIdAsync(device.Id);
                if (lampArray == null)
                    continue;

                Log.Info(
                    $"LampArray discovered: name={device.Name}, lamps={lampArray.LampCount}, " +
                    $"VID=0x{lampArray.HardwareVendorId:X4}, PID=0x{lampArray.HardwareProductId:X4}, " +
                    $"id={device.Id}");

                if (lampArray.HardwareVendorId != LenovoVendorId ||
                    lampArray.HardwareProductId != T7LightingProductId)
                    continue;

                _lampArray = lampArray;
                _lastBrightness = lampArray.BrightnessLevel;

                _zones = BuildSpatialZones(lampArray);

                for (var index = 0; index < lampArray.LampCount; index++)
                {
                    var info = lampArray.GetLampInfo(index);
                    Log.Info(
                        $"  lamp {index}: position=({info.Position.X:F1}, {info.Position.Y:F1}, " +
                        $"{info.Position.Z:F1}), purposes={info.Purposes}, latency={info.UpdateLatency}");
                }

                foreach (var zone in _zones)
                    Log.Info($"  zone {zone.Index}: {zone.Name} ({zone.LampCount} lamps, indices=[{string.Join(",", zone.LampIndices)}])");

                return new LightingDeviceInfo(
                    string.IsNullOrWhiteSpace(device.Name) ? "Lenovo Legion Tower Lighting" : device.Name,
                    device.Id,
                    lampArray.LampCount,
                    lampArray.HardwareVendorId,
                    lampArray.HardwareProductId,
                    _zones);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"LampArray discovery failed: {ex.Message}");
        }

        return null;
    }

    private static List<LightingZoneInfo> BuildSpatialZones(LampArray lampArray)
    {
        var byZ = new Dictionary<float, List<int>>();
        for (var i = 0; i < lampArray.LampCount; i++)
        {
            var info = lampArray.GetLampInfo(i);
            var zKey = (float)Math.Round(info.Position.Z, 2);
            if (!byZ.TryGetValue(zKey, out var list))
            {
                list = [];
                byZ[zKey] = list;
            }
            list.Add(i);
        }

        var sortedZ = byZ.Keys.OrderBy(z => z).ToList();
        var zones = new List<LightingZoneInfo>();
        var zoneIndex = 0;

        var nameByPosition = sortedZ.Count switch
        {
            >= 3 => new[] { LocalizationService.Get("ZoneRearPanel"), LocalizationService.Get("ZoneSidePanel"), LocalizationService.Get("ZoneFrontPanel") },
            2 => new[] { LocalizationService.Get("ZoneRearPanel"), LocalizationService.Get("ZoneFrontPanel") },
            _ => new[] { LocalizationService.Get("ZoneAccent") }
        };

        for (var i = 0; i < sortedZ.Count; i++)
        {
            var z = sortedZ[i];
            var lamps = byZ[z];
            var name = i < nameByPosition.Length ? nameByPosition[i] : $"{LocalizationService.Get("ZoneGeneric")} {i}";
            zones.Add(new LightingZoneInfo(
                zoneIndex++,
                name,
                LightingZoneKind.Accent,
                lamps.Count,
                lamps));
        }

        return zones;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        var lampArray = GetLampArray();
        Log.Info($"LampArray.SetEnabledAsync({enabled}), brightness={_lastBrightness:F2}");
        if (enabled)
        {
            lampArray.BrightnessLevel = Math.Max(0.01, _lastBrightness);
            ApplyAllZoneColors();
        }
        else
        {
            _lastBrightness = lampArray.BrightnessLevel > 0 ? lampArray.BrightnessLevel : _lastBrightness;
            lampArray.SetColor(WinColor.FromArgb(255, 0, 0, 0));
        }
        return Task.CompletedTask;
    }

    public Task SetBrightnessAsync(double brightness)
    {
        var lampArray = GetLampArray();
        _lastBrightness = Math.Clamp(brightness, 0, 1);
        Log.Info($"LampArray.SetBrightnessAsync({brightness:F2}) -> {_lastBrightness:F2}");
        lampArray.BrightnessLevel = _lastBrightness;
        return Task.CompletedTask;
    }

    public Task SetColorAsync(byte red, byte green, byte blue)
    {
        var lampArray = GetLampArray();
        _lastColor = WinColor.FromArgb(255, red, green, blue);
        foreach (var zone in _zones)
            _zoneColors[zone.Index] = (red, green, blue);
        Log.Info($"LampArray.SetColorAsync(r={red}, g={green}, b={blue}) -> all {lampArray.LampCount} lamps");
        lampArray.SetColor(_lastColor);
        return Task.CompletedTask;
    }

    public Task SetZoneColorAsync(int zoneIndex, byte red, byte green, byte blue)
    {
        var lampArray = GetLampArray();
        if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex));

        var zone = _zones[zoneIndex];
        var color = WinColor.FromArgb(255, red, green, blue);
        _zoneColors[zoneIndex] = (red, green, blue);
        Log.Info($"LampArray.SetZoneColorAsync(zone={zoneIndex} '{zone.Name}', r={red}, g={green}, b={blue}) -> {zone.LampCount} lamps");
        lampArray.SetSingleColorForIndices(color, [.. zone.LampIndices]);
        return Task.CompletedTask;
    }

    private void ApplyAllZoneColors()
    {
        var lampArray = GetLampArray();
        foreach (var zone in _zones)
        {
            var color = _zoneColors.TryGetValue(zone.Index, out var c)
                ? WinColor.FromArgb(255, c.R, c.G, c.B)
                : _lastColor;
            lampArray.SetSingleColorForIndices(color, [.. zone.LampIndices]);
        }
    }

    private LampArray GetLampArray() => _lampArray ??
        throw new InvalidOperationException("The Lenovo lighting controller is not available");

    public void Dispose() => _lampArray = null;
}