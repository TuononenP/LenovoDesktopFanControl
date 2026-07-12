using System.Management;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public sealed class WmiLightingService : ILightingControlService
{
    private const string Scope = "root\\WMI";
    private const string GameZoneQuery = "SELECT * FROM LENOVO_GAMEZONE_DATA";
    private const string LightingInfoListQuery = "SELECT * FROM LENOVO_LIGHTING_INFO_LIST";

    private List<WmiLightingZone> _zones = [];
    private bool _isEnabled = true;
    private double _brightness = 1.0;

    public async Task<LightingDeviceInfo?> DiscoverAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var infoRecords = ReadLightingInfoList();
                if (infoRecords.Count == 0)
                {
                    Log.Warn("LENOVO_LIGHTING_INFO_LIST returned no active zones");
                    return null;
                }

                _zones = BuildZones(infoRecords);
                if (_zones.Count == 0)
                {
                    Log.Warn("No addressable RGB lighting zones found");
                    return null;
                }

                foreach (var zone in _zones)
                {
                    zone.CurrentData = BuildLightingPayload(zone, zone.Red, zone.Green, zone.Blue);
                    Log.Info(
                        $"  WMI lighting zone {zone.Index}: id={zone.ZoneId}, panel={zone.PanelId}, " +
                        $"type={zone.Type}, IsSupportRGB={zone.IsSupportRgb}, " +
                        $"brightness={zone.Brightness}, speed={zone.Speed}, " +
                        $"mode=0x{zone.Mode:X8}, payload=[{ByteToHex(zone.CurrentData)}]");
                }

                var totalLamps = _zones.Count;
                return new LightingDeviceInfo(
                    "Lenovo Legion Tower Lighting",
                    "wmi",
                    totalLamps,
                    0x17EF,
                    0xC955,
                    _zones.Select(ToZoneInfo).ToList());
            }
            catch (Exception ex)
            {
                Log.Error("WMI lighting discovery failed", ex);
                return null;
            }
        }).ConfigureAwait(false);
    }

    private static List<LightingInfoRecord> ReadLightingInfoList()
    {
        var records = new List<LightingInfoRecord>();
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, LightingInfoListQuery);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var active = GetBool(mo, "Active") ?? true;
                    if (!active) continue;

                    var zoneId = GetUInt(mo, "zoneid");
                    var panelId = GetUInt(mo, "panelid");
                    var type = GetUInt(mo, "type");
                    var mode = GetUInt(mo, "mode");
                    var brightness = GetUInt(mo, "brightness");
                    var speed = GetUInt(mo, "speed");
                    var isSupportRgb = GetUInt(mo, "IsSupportRGB");
                    var applyCmd = GetUInt(mo, "applycmd");
                    var saveCmd = GetUInt(mo, "Savecmd");
                    var readCmd = GetUInt(mo, "Readcmd");

                    if (zoneId == 0 && panelId == 0 && type == 0)
                        continue;

                    records.Add(new LightingInfoRecord(
                        zoneId, panelId, type, mode, brightness, speed,
                        isSupportRgb, applyCmd, saveCmd, readCmd));
                    Log.Info(
                        $"LENOVO_LIGHTING_INFO_LIST: zone={zoneId}, panel={panelId}, " +
                        $"type={type}, mode=0x{mode:X8}, brightness={brightness}, " +
                        $"speed={speed}, IsSupportRGB={isSupportRgb}, " +
                        $"applycmd={applyCmd}, Savecmd={saveCmd}, Readcmd={readCmd}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to read LENOVO_LIGHTING_INFO_LIST: {ex.Message}");
        }
        return records;
    }

    private static List<WmiLightingZone> BuildZones(List<LightingInfoRecord> records)
    {
        var zones = new List<WmiLightingZone>();
        var seen = new HashSet<(uint zoneId, uint panelId)>();
        var index = 0;

        foreach (var record in records)
        {
            if (record.IsSupportRgb == 0)
            {
                Log.Info($"  Skipping zone {record.ZoneId}/panel {record.PanelId}: IsSupportRGB=0");
                continue;
            }

            var key = (record.ZoneId, record.PanelId);
            if (!seen.Add(key))
                continue;

            var (r, g, b) = ExtractRgbFromMode(record.Mode);

            zones.Add(new WmiLightingZone
            {
                Index = index++,
                ZoneId = (int)record.ZoneId,
                PanelId = (int)record.PanelId,
                Type = (int)record.Type,
                Mode = (int)record.Mode,
                Brightness = (int)record.Brightness,
                Speed = (int)record.Speed,
                IsSupportRgb = (int)record.IsSupportRgb,
                ApplyCmd = (int)record.ApplyCmd,
                SaveCmd = (int)record.SaveCmd,
                ReadCmd = (int)record.ReadCmd,
                Red = r,
                Green = g,
                Blue = b
            });
        }

        return zones;
    }

    private static (byte R, byte G, byte B) ExtractRgbFromMode(uint mode)
    {
        var bytes = BitConverter.GetBytes(mode);
        return (bytes[0], bytes[1], bytes[2]);
    }

    private static byte[] BuildLightingPayload(WmiLightingZone zone, byte red, byte green, byte blue)
    {
        var modeBytes = BitConverter.GetBytes((uint)zone.Mode);
        modeBytes[0] = red;
        modeBytes[1] = green;
        modeBytes[2] = blue;
        return modeBytes;
    }

    private static LightingZoneInfo ToZoneInfo(WmiLightingZone zone)
    {
        var name = GetDesktopZoneName(zone.PanelId, zone.ZoneId, zone.Type);
        var kind = MapZoneKind(zone.PanelId, zone.Type);
        return new LightingZoneInfo(
            zone.Index,
            name,
            kind,
            1,
            new[] { zone.Index });
    }

    private static string GetDesktopZoneName(int panelId, int zoneId, int type)
    {
        if (type == 3)
            return LocalizationService.Get("ZoneLightingSync");

        return panelId switch
        {
            2 => LocalizationService.Get("ZoneFrontPanel"),
            4 => LocalizationService.Get("ZoneRearPanel"),
            5 => LocalizationService.Get("ZoneSidePanel"),
            7 => LocalizationService.Get("ZoneFanLight"),
            8 => LocalizationService.Get("ZoneFanLight"),
            13 => LocalizationService.Get("ZoneBranding"),
            15 => LocalizationService.Get("ZonePowerButton"),
            16 => LocalizationService.Get("ZoneLogoLight"),
            _ => $"{LocalizationService.Get("ZoneGeneric")} {zoneId}"
        };
    }

    private static LightingZoneKind MapZoneKind(int panelId, int type)
    {
        if (type == 3) return LightingZoneKind.Accent;
        return panelId switch
        {
            13 => LightingZoneKind.Branding,
            15 => LightingZoneKind.Status,
            _ => LightingZoneKind.Accent
        };
    }

    public Task SetEnabledAsync(bool enabled)
    {
        _isEnabled = enabled;
        if (!enabled)
        {
            foreach (var zone in _zones)
            {
                var offData = BuildLightingPayload(zone, 0, 0, 0);
                SetLightingData(zone, offData);
            }
        }
        else
        {
            ApplyAllZones();
        }
        return Task.CompletedTask;
    }

    public Task SetBrightnessAsync(double brightness)
    {
        _brightness = Math.Clamp(brightness, 0, 1);
        foreach (var zone in _zones)
        {
            zone.Brightness = (int)Math.Round(_brightness * 10);
        }
        return Task.CompletedTask;
    }

    public Task SetColorAsync(byte red, byte green, byte blue)
    {
        foreach (var zone in _zones)
            SetZoneColorInternal(zone, red, green, blue);
        return Task.CompletedTask;
    }

    public Task SetZoneColorAsync(int zoneIndex, byte red, byte green, byte blue)
    {
        if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex));
        SetZoneColorInternal(_zones[zoneIndex], red, green, blue);
        return Task.CompletedTask;
    }

    private void SetZoneColorInternal(WmiLightingZone zone, byte red, byte green, byte blue)
    {
        var data = BuildLightingPayload(zone, red, green, blue);
        SetLightingData(zone, data);
        zone.Red = red;
        zone.Green = green;
        zone.Blue = blue;
        zone.CurrentData = data;
    }

    private void ApplyAllZones()
    {
        foreach (var zone in _zones)
        {
            var data = BuildLightingPayload(zone, zone.Red, zone.Green, zone.Blue);
            SetLightingData(zone, data);
        }
    }

    private static void SetLightingData(WmiLightingZone zone, byte[] data)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, GameZoneQuery);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                using (var inParams = mo.GetMethodParameters("SetLightingData"))
                {
                    inParams["Data"] = data;
                    inParams["idx"] = zone.ZoneId;
                    inParams["panelid"] = zone.PanelId;
                    mo.InvokeMethod("SetLightingData", inParams, null);
                    Log.Info(
                        $"SetLightingData(zone={zone.ZoneId}, panel={zone.PanelId}) " +
                        $"succeeded: [{ByteToHex(data)}]");
                    return;
                }
            }
            Log.Warn($"SetLightingData(zone={zone.ZoneId}, panel={zone.PanelId}): no WMI objects found");
        }
        catch (Exception ex)
        {
            Log.Error($"SetLightingData(zone={zone.ZoneId}, panel={zone.PanelId}) failed", ex);
        }
    }

    private static uint GetUInt(ManagementBaseObject mo, string name)
    {
        try
        {
            var value = mo[name]?.ToString() ?? "0";
            return uint.TryParse(value, out var result) ? result : Convert.ToUInt32(mo[name]);
        }
        catch { return 0; }
    }

    private static bool? GetBool(ManagementBaseObject mo, string name)
    {
        try
        {
            var value = mo[name];
            if (value is bool b) return b;
            return Convert.ToInt32(value) != 0;
        }
        catch { return null; }
    }

    private static string ByteToHex(byte[] data)
    {
        if (data == null) return "";
        var parts = new string[data.Length];
        for (var i = 0; i < data.Length; i++)
            parts[i] = data[i].ToString("X2");
        return string.Join(" ", parts);
    }

    public void Dispose()
    {
    }

    private sealed class LightingInfoRecord
    {
        public uint ZoneId { get; }
        public uint PanelId { get; }
        public uint Type { get; }
        public uint Mode { get; }
        public uint Brightness { get; }
        public uint Speed { get; }
        public uint IsSupportRgb { get; }
        public uint ApplyCmd { get; }
        public uint SaveCmd { get; }
        public uint ReadCmd { get; }

        public LightingInfoRecord(uint zoneId, uint panelId, uint type, uint mode,
            uint brightness, uint speed, uint isSupportRgb,
            uint applyCmd, uint saveCmd, uint readCmd)
        {
            ZoneId = zoneId;
            PanelId = panelId;
            Type = type;
            Mode = mode;
            Brightness = brightness;
            Speed = speed;
            IsSupportRgb = isSupportRgb;
            ApplyCmd = applyCmd;
            SaveCmd = saveCmd;
            ReadCmd = readCmd;
        }
    }

    private sealed class WmiLightingZone
    {
        public int Index { get; set; }
        public int ZoneId { get; set; }
        public int PanelId { get; set; }
        public int Type { get; set; }
        public int Mode { get; set; }
        public int Brightness { get; set; }
        public int Speed { get; set; }
        public int IsSupportRgb { get; set; }
        public int ApplyCmd { get; set; }
        public int SaveCmd { get; set; }
        public int ReadCmd { get; set; }
        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
        public byte[]? CurrentData { get; set; }
    }
}