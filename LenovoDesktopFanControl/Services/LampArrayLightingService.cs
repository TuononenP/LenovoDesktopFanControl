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
                for (var index = 0; index < lampArray.LampCount; index++)
                {
                    var info = lampArray.GetLampInfo(index);
                    Log.Info(
                        $"  lamp {index}: position=({info.Position.X:F1}, {info.Position.Y:F1}, " +
                        $"{info.Position.Z:F1}), purposes={info.Purposes}, latency={info.UpdateLatency}");
                }

                return new LightingDeviceInfo(
                    string.IsNullOrWhiteSpace(device.Name) ? "Lenovo Legion Tower Lighting" : device.Name,
                    device.Id,
                    lampArray.LampCount,
                    lampArray.HardwareVendorId,
                    lampArray.HardwareProductId);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"LampArray discovery failed: {ex.Message}");
        }

        return null;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        var lampArray = GetLampArray();
        if (enabled)
        {
            lampArray.BrightnessLevel = Math.Max(0.01, _lastBrightness);
            lampArray.SetColor(_lastColor);
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
        lampArray.BrightnessLevel = _lastBrightness;
        return Task.CompletedTask;
    }

    public Task SetColorAsync(byte red, byte green, byte blue)
    {
        var lampArray = GetLampArray();
        _lastColor = WinColor.FromArgb(255, red, green, blue);
        lampArray.SetColor(_lastColor);
        return Task.CompletedTask;
    }

    private LampArray GetLampArray() => _lampArray ??
        throw new InvalidOperationException("The Lenovo lighting controller is not available");

    public void Dispose() => _lampArray = null;
}
