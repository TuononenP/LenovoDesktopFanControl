using System.Numerics;
using LenovoDesktopFanControl.Models;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using WinColor = Windows.UI.Color;

namespace LenovoDesktopFanControl.Services;

public sealed class LampArrayLightingService : ILightingControlService
{
    private const int RecoveryRetryCount = 5;
    private const int GpuApplyDebounceMilliseconds = 75;
    private const float PhysicalGroupGapMeters = 0.15f;
    private static readonly string[] TowerZoneNameResourceKeys =
    [
        "ZoneCaseFrontFans",
        "ZoneCaseTopFans",
        "ZoneCpuWatercooler",
        "ZoneLegionLogo",
        "ZoneBackFan"
    ];
    private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly DynamicLightingFirmwareRecovery _firmwareRecovery;
    private readonly ILenovoGpuLightingController _gpuLightingController;
    private readonly ILenovoTowerLightingPersistence _towerLightingPersistence;
    private LampArray? _lampArray;
    private WinColor _lastColor = WinColor.FromArgb(255, 91, 157, 255);
    private (byte R, byte G, byte B) _gpuColor = (91, 157, 255);
    private double _lastBrightness = 1;
    private bool _lastEnabled = true;
    private List<LightingZoneInfo> _zones = [];
    private readonly Dictionary<int, (byte R, byte G, byte B)> _zoneColors = [];
    private readonly Dictionary<int, double> _zoneBrightness = [];
    private readonly Dictionary<int, bool> _zoneEnabled = [];
    private int _gpuZoneIndex = -1;
    private CancellationTokenSource? _gpuApplyCancellation;
    private long _gpuApplyVersion;

    public bool IsControlAvailable => _lampArray?.IsAvailable ?? false;

    public event EventHandler? AvailabilityChanged;

    public LampArrayLightingService()
        : this(new WmiDynamicLightingFirmwareService(), new LenovoRtxGpuLightingController())
    {
    }

    internal LampArrayLightingService(
        IDynamicLightingFirmwareService firmwareService,
        ILenovoGpuLightingController? gpuLightingController = null,
        ILenovoTowerLightingPersistence? towerLightingPersistence = null)
    {
        _firmwareRecovery = new DynamicLightingFirmwareRecovery(firmwareService);
        _gpuLightingController = gpuLightingController ?? new LenovoRtxGpuLightingController();
        _towerLightingPersistence = towerLightingPersistence ?? new LenovoTowerLightingPersistence();
    }

    private void OnLampArrayAvailabilityChanged(object? sender, object? e)
    {
        Log.Info($"LampArray.AvailabilityChanged: IsAvailable={(_lampArray?.IsAvailable ?? false)}");
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<LightingDeviceInfo?> DiscoverAsync()
    {
        try
        {
            DetachLampArray();

            var candidate = await FindLenovoLampArrayAsync();
            if (candidate == null)
                return null;

            if (candidate.LampArray.LampCount == 0)
            {
                Log.Warn("Lenovo LampArray reported zero lamps; checking the firmware Dynamic Lighting state");
                var enabled = await _firmwareRecovery.TryEnableAsync(
                    candidate.LampArray.HardwareVendorId,
                    candidate.LampArray.HardwareProductId,
                    candidate.LampArray.LampCount);

                if (!enabled)
                    return null;

                for (var attempt = 1; attempt <= RecoveryRetryCount; attempt++)
                {
                    await Task.Delay(RecoveryRetryDelay);
                    candidate = await FindLenovoLampArrayAsync();
                    if (candidate?.LampArray.LampCount > 0)
                    {
                        Log.Info(
                            $"Dynamic Lighting recovery succeeded on discovery attempt {attempt}: " +
                            $"{candidate.LampArray.LampCount} lamps");
                        break;
                    }

                    Log.Info($"Waiting for Lenovo LampArray re-enumeration ({attempt}/{RecoveryRetryCount})");
                }

                if (candidate == null || candidate.LampArray.LampCount <= 0)
                {
                    Log.Warn("Dynamic Lighting was enabled, but the Lenovo controller still reports zero lamps");
                    return null;
                }
            }

            return AttachLampArray(candidate);
        }
        catch (Exception ex)
        {
            Log.Warn($"LampArray discovery failed: {ex.Message}");
        }

        return null;
    }

    private static async Task<LampArrayCandidate?> FindLenovoLampArrayAsync()
    {
        LampArrayCandidate? zeroLampCandidate = null;
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

            if (lampArray.HardwareVendorId == DynamicLightingFirmwareRecovery.LenovoVendorId &&
                lampArray.HardwareProductId == DynamicLightingFirmwareRecovery.LenovoLightingProductId)
            {
                var candidate = new LampArrayCandidate(device, lampArray);
                if (lampArray.LampCount > 0)
                    return candidate;

                zeroLampCandidate ??= candidate;
            }
        }

        return zeroLampCandidate;
    }

    private LightingDeviceInfo AttachLampArray(LampArrayCandidate candidate)
    {
        var device = candidate.Device;
        var lampArray = candidate.LampArray;

        _lampArray = lampArray;
        _lampArray.AvailabilityChanged += OnLampArrayAvailabilityChanged;
        _lastBrightness = lampArray.BrightnessLevel;
        _zones = BuildSpatialZones(lampArray);
        _gpuZoneIndex = -1;
        if (_gpuLightingController.TryDiscover())
        {
            _gpuZoneIndex = _zones.Count;
            _zones.Add(new LightingZoneInfo(
                _gpuZoneIndex,
                LocalizationService.Get("ZoneGraphicsCard"),
                LightingZoneKind.GraphicsCard,
                0,
                []));
        }
        foreach (var zone in _zones)
        {
            _zoneBrightness[zone.Index] = 1;
            _zoneEnabled[zone.Index] = true;
        }

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

    private static List<LightingZoneInfo> BuildSpatialZones(LampArray lampArray)
    {
        var positions = new List<Vector3>(lampArray.LampCount);
        for (var i = 0; i < lampArray.LampCount; i++)
            positions.Add(lampArray.GetLampInfo(i).Position);

        var zones = new List<LightingZoneInfo>();
        var groups = BuildContiguousSpatialGroups(positions);
        for (var zoneIndex = 0; zoneIndex < groups.Count; zoneIndex++)
        {
            var lamps = groups[zoneIndex];
            zones.Add(new LightingZoneInfo(
                zoneIndex,
                GetDefaultTowerZoneName(zoneIndex),
                LightingZoneKind.Accent,
                lamps.Count,
                lamps));
        }

        return zones;
    }

    internal static string GetDefaultTowerZoneName(int zoneIndex) =>
        zoneIndex >= 0 && zoneIndex < TowerZoneNameResourceKeys.Length
            ? LocalizationService.Get(TowerZoneNameResourceKeys[zoneIndex])
            : $"{LocalizationService.Get("ZoneLightGroup")} {zoneIndex + 1}";

    internal static IReadOnlyList<IReadOnlyList<int>> BuildContiguousSpatialGroups(
        IReadOnlyList<Vector3> positions)
    {
        if (positions.Count == 0)
            return [];

        var groups = new List<IReadOnlyList<int>>();
        var current = new List<int> { 0 };
        for (var index = 1; index < positions.Count; index++)
        {
            // Lenovo enumerates the LEDs for each physical lighting component
            // contiguously. A large position jump marks the next component.
            if (Vector3.Distance(positions[index - 1], positions[index]) >=
                PhysicalGroupGapMeters)
            {
                groups.Add(current);
                current = [];
            }

            current.Add(index);
        }

        groups.Add(current);
        return groups;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        var lampArray = GetLampArray();
        _lastEnabled = enabled;
        Log.Info($"LampArray.SetEnabledAsync({enabled}), brightness={_lastBrightness:F2}");
        if (enabled)
        {
            lampArray.BrightnessLevel = _lastBrightness;
            ApplyAllZoneColors();
        }
        else
        {
            _lastBrightness = lampArray.BrightnessLevel > 0 ? lampArray.BrightnessLevel : _lastBrightness;
            var black = WinColor.FromArgb(255, 0, 0, 0);
            // SetColor is controller-wide, but Lenovo's firmware can retain a
            // previously addressed component while switching profiles. Write
            // the complete physical lamp range as well so lower case strips
            // and any other separately addressed component are explicitly off.
            lampArray.SetColor(black);
            lampArray.SetSingleColorForIndices(
                black,
                Enumerable.Range(0, lampArray.LampCount).ToArray());
            // Some Lenovo firmware effects continue rendering after a black
            // color write. Zero brightness is the controller-wide hard-off.
            lampArray.BrightnessLevel = 0;
        }
        return QueueGpuStateAsync();
    }

    public Task SetBrightnessAsync(double brightness)
    {
        var lampArray = GetLampArray();
        _lastBrightness = Math.Clamp(brightness, 0, 1);
        Log.Info($"LampArray.SetBrightnessAsync({brightness:F2}) -> {_lastBrightness:F2}");
        lampArray.BrightnessLevel = _lastEnabled ? _lastBrightness : 0;
        return QueueGpuStateAsync();
    }

    public Task SetColorAsync(byte red, byte green, byte blue)
    {
        GetLampArray();
        _lastColor = WinColor.FromArgb(255, red, green, blue);
        foreach (var zone in _zones)
            _zoneColors[zone.Index] = (red, green, blue);
        _gpuColor = (red, green, blue);
        Log.Info($"LampArray.SetColorAsync(r={red}, g={green}, b={blue}) -> all enabled zones");
        ApplyAllZoneColors();
        return QueueGpuStateAsync();
    }

    public Task SetZoneColorAsync(int zoneIndex, byte red, byte green, byte blue)
    {
        var lampArray = GetLampArray();
        if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex));

        var zone = _zones[zoneIndex];
        _zoneColors[zoneIndex] = (red, green, blue);
        if (zoneIndex == _gpuZoneIndex)
        {
            _gpuColor = (red, green, blue);
            Log.Info(
                $"LampArray.SetZoneColorAsync(zone={zoneIndex} '{zone.Name}', " +
                $"r={red}, g={green}, b={blue}) -> Lenovo RTX GPU");
            return QueueGpuStateAsync();
        }

        var color = GetEffectiveTowerColor(
            zoneIndex,
            WinColor.FromArgb(255, red, green, blue));
        Log.Info($"LampArray.SetZoneColorAsync(zone={zoneIndex} '{zone.Name}', r={red}, g={green}, b={blue}) -> {zone.LampCount} lamps");
        lampArray.SetSingleColorForIndices(color, [.. zone.LampIndices]);
        return Task.CompletedTask;
    }

    public Task SetZoneBrightnessAsync(int zoneIndex, double brightness)
    {
        var lampArray = GetLampArray();
        if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex));

        var level = Math.Clamp(brightness, 0, 1);
        _zoneBrightness[zoneIndex] = level;
        var zone = _zones[zoneIndex];
        Log.Info(
            $"LampArray.SetZoneBrightnessAsync(zone={zoneIndex} '{zone.Name}', " +
            $"brightness={level:F2})");

        if (zoneIndex == _gpuZoneIndex)
        {
            return QueueGpuStateAsync();
        }

        var savedColor = _zoneColors.TryGetValue(zoneIndex, out var color)
            ? WinColor.FromArgb(255, color.R, color.G, color.B)
            : _lastColor;
        lampArray.SetSingleColorForIndices(
            GetEffectiveTowerColor(zoneIndex, savedColor),
            [.. zone.LampIndices]);
        return Task.CompletedTask;
    }

    public Task SetZoneEnabledAsync(int zoneIndex, bool enabled)
    {
        var lampArray = GetLampArray();
        if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex));

        var zone = _zones[zoneIndex];
        _zoneEnabled[zoneIndex] = enabled;
        Log.Info($"LampArray.SetZoneEnabledAsync(zone={zoneIndex} '{zone.Name}', enabled={enabled})");

        if (zoneIndex == _gpuZoneIndex)
        {
            return QueueGpuStateAsync();
        }

        var savedColor = _zoneColors.TryGetValue(zoneIndex, out var color)
            ? WinColor.FromArgb(255, color.R, color.G, color.B)
            : _lastColor;
        var effectiveColor = GetEffectiveTowerColor(zoneIndex, savedColor);
        lampArray.SetSingleColorForIndices(effectiveColor, [.. zone.LampIndices]);
        return Task.CompletedTask;
    }

    private void ApplyAllZoneColors()
    {
        var lampArray = GetLampArray();
        foreach (var zone in _zones)
        {
            if (zone.Index == _gpuZoneIndex)
                continue;

            var color = _zoneColors.TryGetValue(zone.Index, out var c)
                ? WinColor.FromArgb(255, c.R, c.G, c.B)
                : _lastColor;
            var effectiveColor = GetEffectiveTowerColor(zone.Index, color);
            lampArray.SetSingleColorForIndices(effectiveColor, [.. zone.LampIndices]);
        }
    }

    private WinColor GetEffectiveTowerColor(int zoneIndex, WinColor color) =>
        _lastEnabled && IsZoneEnabled(zoneIndex)
            ? ScaleColor(color, GetZoneBrightness(zoneIndex))
            : WinColor.FromArgb(255, 0, 0, 0);

    internal static WinColor ScaleColor(WinColor color, double brightness)
    {
        var level = Math.Clamp(brightness, 0, 1);
        return WinColor.FromArgb(
            color.A,
            ScaleChannel(color.R, level),
            ScaleChannel(color.G, level),
            ScaleChannel(color.B, level));
    }

    private static byte ScaleChannel(byte channel, double brightness) =>
        (byte)Math.Clamp(
            (int)Math.Round(channel * brightness, MidpointRounding.AwayFromZero),
            0,
            255);

    private async Task QueueGpuStateAsync()
    {
        if (_gpuZoneIndex < 0)
            return;

        var version = Interlocked.Increment(ref _gpuApplyVersion);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _gpuApplyCancellation, cancellation);
        previous?.Cancel();

        try
        {
            try
            {
                await Task.Delay(GpuApplyDebounceMilliseconds, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (version != Volatile.Read(ref _gpuApplyVersion))
                return;

            if (!_gpuLightingController.TryApplyStaticColor(
                    _gpuColor.R,
                    _gpuColor.G,
                    _gpuColor.B,
                    _lastBrightness * GetZoneBrightness(_gpuZoneIndex),
                    _lastEnabled && IsZoneEnabled(_gpuZoneIndex)))
            {
                throw new InvalidOperationException(
                    "The Lenovo RTX GPU lighting controller did not accept the color change");
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _gpuApplyCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    public Task ReapplyGpuStateAsync()
    {
        Log.Info("Re-applying the current Lenovo RTX GPU lighting state");
        return QueueGpuStateAsync();
    }

    private double GetZoneBrightness(int zoneIndex) =>
        _zoneBrightness.TryGetValue(zoneIndex, out var brightness) ? brightness : 1;

    private bool IsZoneEnabled(int zoneIndex) =>
        !_zoneEnabled.TryGetValue(zoneIndex, out var enabled) || enabled;

    public Task PersistStateAsync()
    {
        if (_lampArray == null)
            return Task.CompletedTask;

        var towerColors = _zones
            .Where(zone => zone.Index != _gpuZoneIndex)
            .Select(zone => !IsZoneEnabled(zone.Index)
                ? (R: (byte)0, G: (byte)0, B: (byte)0)
                : _zoneColors.TryGetValue(zone.Index, out var color)
                    ? color
                    : (_lastColor.R, _lastColor.G, _lastColor.B))
            .Distinct()
            .ToArray();
        var towerBrightness = _zones
            .Where(zone => zone.Index != _gpuZoneIndex && IsZoneEnabled(zone.Index))
            .Select(zone => GetZoneBrightness(zone.Index))
            .Distinct()
            .ToArray();

        if (towerColors.Length > 1 || towerBrightness.Length > 1)
        {
            Log.Info(
                "Skipping firmware lighting persistence because the controller's static profile " +
                "cannot represent independent LampArray zone colors or brightness");
            return Task.CompletedTask;
        }

        var color = towerColors.FirstOrDefault((_lastColor.R, _lastColor.G, _lastColor.B));
        var brightness = _lastBrightness * towerBrightness.FirstOrDefault(1);
        if (_towerLightingPersistence.TryScheduleStaticColorAfterProcessExit(
                color.R,
                color.G,
                color.B,
                brightness,
                _lastEnabled))
        {
            return Task.CompletedTask;
        }

        Log.Warn("Falling back to immediate tower lighting persistence");
        return Task.Run(() =>
        {
            if (!_towerLightingPersistence.TrySaveStaticColor(
                    color.R,
                    color.G,
                    color.B,
                    brightness,
                    _lastEnabled))
            {
                Log.Warn("The current tower lighting state could not be saved to firmware");
            }
        });
    }

    private LampArray GetLampArray() => _lampArray ??
        throw new InvalidOperationException("The Lenovo lighting controller is not available");

    public void Dispose()
    {
        DetachLampArray();
        _gpuLightingController.Dispose();
    }

    private void DetachLampArray()
    {
        if (_lampArray != null)
            _lampArray.AvailabilityChanged -= OnLampArrayAvailabilityChanged;
        _lampArray = null;
        _zones = [];
        _zoneColors.Clear();
        _zoneBrightness.Clear();
        _zoneEnabled.Clear();
        _gpuZoneIndex = -1;
    }

    private sealed record LampArrayCandidate(DeviceInformation Device, LampArray LampArray);
}
