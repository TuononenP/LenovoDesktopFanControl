using System.Diagnostics;
using System.Management;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public sealed class WmiFanControlService : IWmiFanControlService
{
    private static readonly (string ProcessName, string ConflictName)[] ConflictingProcesses =
    [
        ("VantageService", "VantageService"),
        ("LenovoVantage", "LenovoVantage"),
        ("VantageAddinDemo", "VantageAddinDemo"),
        ("LegionZone", "LegionZone"),
        ("LegionSpace", "Lenovo Legion Space"),
        ("LenovoSmartService", "Lenovo Legion Space"),
        ("SmartEngineHost", "Lenovo Legion Space"),
        ("GAService", "GAService")
    ];

    private static readonly (string ServiceName, string ConflictName)[] ConflictingServices =
    [
        ("LenovoVantageService", "VantageService"),
        ("GAService", "GAService")
    ];

    public static List<string> GetRunningConflictingProcesses()
    {
        var result = new List<string>();
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    var name = p.ProcessName;
                    var conflictName = FindConflictingProcessName(name);
                    if (conflictName != null && !result.Contains(conflictName))
                        result.Add(conflictName);
                }
                catch { }
            }
        }
        catch { }

        foreach (var conflictName in GetRunningConflictingServices())
        {
            if (!result.Contains(conflictName))
                result.Add(conflictName);
        }

        return result;
    }

    public static async Task<ConflictShutdownResult> ShutdownConflictingProcessesAsync()
    {
        return await Task.Run(() =>
        {
            var requested = GetRunningConflictingProcesses();
            var stopped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TryStopConflictingServices(stopped, failed);

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    string processName;
                    try
                    {
                        processName = process.ProcessName;
                    }
                    catch
                    {
                        continue;
                    }

                    var conflictName = FindConflictingProcessName(processName);
                    if (conflictName == null)
                        continue;

                    try
                    {
                        if (process.CloseMainWindow() && process.WaitForExit(2000))
                        {
                            stopped.Add(conflictName);
                            continue;
                        }

                        process.Kill(entireProcessTree: true);
                        if (process.WaitForExit(3000))
                            stopped.Add(conflictName);
                        else
                            failed.Add(conflictName);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Failed to stop conflicting process {conflictName}: {ex.Message}");
                        failed.Add(conflictName);
                    }
                }
            }

            Thread.Sleep(500);
            var remaining = GetRunningConflictingProcesses();
            return FanFirmwareCompatibility.ReconcileConflictShutdown(
                requested,
                stopped,
                remaining);
        }).ConfigureAwait(false);
    }

    internal static string? FindConflictingProcessName(string processName)
    {
        var definition = ConflictingProcesses.FirstOrDefault(candidate =>
            processName.Contains(candidate.ProcessName, StringComparison.OrdinalIgnoreCase));
        return definition == default ? null : definition.ConflictName;
    }

    private static IReadOnlyList<string> GetRunningConflictingServices()
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, State FROM Win32_Service " +
                "WHERE Name = 'LenovoVantageService' OR Name = 'GAService'");
            foreach (ManagementObject service in searcher.Get())
            {
                using (service)
                {
                    if (string.Equals(
                            service["State"]?.ToString(),
                            "Stopped",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    var serviceName = service["Name"]?.ToString();
                    var definition = ConflictingServices.FirstOrDefault(item =>
                        string.Equals(item.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
                    if (definition != default)
                        result.Add(definition.ConflictName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to query conflicting Lenovo services: {ex.Message}");
        }

        return result;
    }

    private static void TryStopConflictingServices(HashSet<string> stopped, HashSet<string> failed)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_Service " +
                "WHERE Name = 'LenovoVantageService' OR Name = 'GAService'");
            foreach (ManagementObject service in searcher.Get())
            {
                using (service)
                {
                    var serviceName = service["Name"]?.ToString();
                    var definition = ConflictingServices.FirstOrDefault(item =>
                        string.Equals(item.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
                    if (definition == default)
                        continue;

                    var state = service["State"]?.ToString();
                    if (string.Equals(state, "Stopped", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var result = service.InvokeMethod("StopService", null, null);
                    var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? uint.MaxValue);
                    if (returnValue == 0)
                        stopped.Add(definition.ConflictName);
                    else
                    {
                        Log.Warn($"{definition.ServiceName} stop request failed with code {returnValue}");
                        failed.Add(definition.ConflictName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to stop conflicting Lenovo services through WMI: {ex.Message}");
            foreach (var definition in ConflictingServices)
                failed.Add(definition.ConflictName);
        }
    }

    public static void LogAvailableWmiClasses()
    {
        try
        {
            var scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            var query = new SelectQuery("SELECT * FROM meta_class");
            var mos = new ManagementObjectSearcher(scope, query);
            var classes = new List<string>();
            foreach (ManagementClass mc in mos.Get())
            {
                var name = mc["__CLASS"]?.ToString() ?? "";
                if (name.StartsWith("LENOVO", StringComparison.OrdinalIgnoreCase))
                    classes.Add(name);
            }
            classes.Sort();
            foreach (var c in classes)
                Log.Info($"WMI class: {c}");

            LogWmiMethods("LENOVO_GAMEZONE_DATA");
            LogWmiMethods("LENOVO_OTHER_METHOD");
            LogWmiMethods("LENOVO_FAN_METHOD");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to enumerate WMI classes", ex);
        }
    }

    private static void LogWmiMethods(string className)
    {
        try
        {
            var scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            var path = new ManagementPath(className);
            var mc = new ManagementClass(scope, path, null);
            foreach (var method in mc.Methods)
            {
                var inStr = "";
                if (method.InParameters != null)
                {
                    var names = new List<string>();
                    foreach (PropertyData pd in method.InParameters.Properties)
                        names.Add(pd.Name);
                    inStr = string.Join(", ", names);
                }
                var outStr = "";
                if (method.OutParameters != null)
                {
                    var names = new List<string>();
                    foreach (PropertyData pd in method.OutParameters.Properties)
                        names.Add(pd.Name);
                    outStr = string.Join(", ", names);
                }
                Log.Info($"  {className}.{method.Name}(in: [{inStr}]) -> (out: [{outStr}])");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"  {className}: failed to enumerate methods: {ex.Message}");
        }
    }

    private const string Scope = "root\\WMI";
    private const string GameZoneQuery = "SELECT * FROM LENOVO_GAMEZONE_DATA";
    private const string FanMethodQuery = "SELECT * FROM LENOVO_FAN_METHOD";
    private const string FanTableDataQuery = "SELECT * FROM LENOVO_FAN_TABLE_DATA";
    private const string OtherMethodQuery = "SELECT * FROM LENOVO_OTHER_METHOD";
    private const uint FanFullSpeedFeatureId = 0x04020000;
    private bool? _usesDesktopFanProtocol;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (!await IsLenovoManufacturerAsync().ConfigureAwait(false))
            {
                Log.Warn("Manufacturer is not LENOVO");
                return false;
            }

            var result = await Task.Run(() =>
            {
                var mos = new ManagementObjectSearcher(Scope, GameZoneQuery);
                foreach (ManagementObject mo in mos.Get())
                {
                    try
                    {
                        var inParams = mo.GetMethodParameters("IsSupportSmartFan");
                        var outParams = mo.InvokeMethod("IsSupportSmartFan", inParams, null);
                        var data = Convert.ToInt32(outParams?["Data"] ?? 0);
                        Log.Info($"IsSupportSmartFan returned {data}");
                        return data > 0;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"IsSupportSmartFan call failed: {ex.Message}");
                    }
                }
                return false;
            }).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("IsSupportedAsync failed", ex);
            return false;
        }
    }

    private static async Task<bool> IsLenovoManufacturerAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var mos = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in mos.Get())
                {
                    var manufacturer = mo["Manufacturer"]?.ToString() ?? "";
                    Log.Info($"Manufacturer: {manufacturer}");
                    return manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Manufacturer check failed", ex);
            }
            return false;
        }).ConfigureAwait(false);
    }

    private bool UsesDesktopFanProtocol()
    {
        if (_usesDesktopFanProtocol is bool cached)
            return cached;

        bool? detected = null;
        var reason = "computer chassis type";
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, FanTableDataQuery);
            foreach (ManagementObject table in searcher.Get())
            {
                using (table)
                {
                    var propertyNames = table.Properties
                        .Cast<PropertyData>()
                        .Select(property => property.Name)
                        .ToArray();
                    bool HasProperty(string name) =>
                        propertyNames.Contains(name, StringComparer.OrdinalIgnoreCase);

                    if (HasProperty("UISpeedRatio") ||
                        HasProperty("FanSpeedStep") ||
                        HasProperty("DefaultFanMaxSpeed"))
                    {
                        detected = true;
                        reason = "desktop LENOVO_FAN_TABLE_DATA schema";
                    }
                    else if (HasProperty("Fan_Id") || HasProperty("FanTable_Data"))
                    {
                        detected = false;
                        reason = "notebook LENOVO_FAN_TABLE_DATA schema";
                    }
                    else if (HasProperty("FanID") && HasProperty("SensorID"))
                    {
                        detected = true;
                        reason = "desktop fan/sensor identifiers";
                    }
                }

                break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Fan protocol schema detection failed: {ex.Message}");
        }

        if (detected == null)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT PCSystemType FROM Win32_ComputerSystem");
                foreach (ManagementObject computer in searcher.Get())
                {
                    using (computer)
                    {
                        var systemType = Convert.ToInt32(computer["PCSystemType"] ?? 0);
                        detected = systemType is 1 or 3;
                        reason = $"PCSystemType={systemType}";
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Fan protocol chassis detection failed: {ex.Message}");
            }
        }

        _usesDesktopFanProtocol = detected ?? false;
        Log.Info(
            $"Using {(_usesDesktopFanProtocol.Value ? "desktop" : "notebook")} " +
            $"Lenovo fan protocol ({reason})");
        return _usesDesktopFanProtocol.Value;
    }

    public async Task<SmartFanMode> GetSmartFanModeAsync()
    {
        return await Task.Run(() =>
        {
            var desktopProtocol = UsesDesktopFanProtocol();
            var mos = new ManagementObjectSearcher(Scope, GameZoneQuery);
            foreach (ManagementObject mo in mos.Get())
            {
                var inParams = mo.GetMethodParameters("GetSmartFanMode");
                var outParams = mo.InvokeMethod("GetSmartFanMode", inParams, null);
                var data = Convert.ToInt32(outParams?["Data"] ?? 1);
                var mode = FanFirmwareCompatibility.DecodeSmartFanMode(data, desktopProtocol);
                Log.Info(
                    $"GetSmartFanMode returned firmware value {data}; " +
                    $"decoded as {mode} ({(desktopProtocol ? "desktop" : "notebook")} protocol)");
                return mode;
            }
            return SmartFanMode.Balanced;
        }).ConfigureAwait(false);
    }

    public async Task SetSmartFanModeAsync(SmartFanMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SmartFan mode");

        Log.Info($"SetSmartFanModeAsync({mode})");
        await Task.Run(() =>
        {
            try
            {
                var desktopProtocol = UsesDesktopFanProtocol();
                var firmwareMode = FanFirmwareCompatibility.EncodeSmartFanMode(
                    mode,
                    desktopProtocol);
                using var searcher = new ManagementObjectSearcher(Scope, GameZoneQuery);
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        using var inParams = mo.GetMethodParameters("SetSmartFanMode");
                        inParams["Data"] = firmwareMode;
                        mo.InvokeMethod("SetSmartFanMode", inParams, null);
                        Log.Info(
                            $"SetSmartFanMode WMI call succeeded for {mode}: " +
                            $"firmware value {firmwareMode} " +
                            $"({(desktopProtocol ? "desktop" : "notebook")} protocol)");

                        using var verifyParams = mo.GetMethodParameters("GetSmartFanMode");
                        using var verifyResult = mo.InvokeMethod("GetSmartFanMode", verifyParams, null);
                        var actualMode = Convert.ToInt32(verifyResult?["Data"] ?? -1);
                        var actualLogicalMode = FanFirmwareCompatibility.DecodeSmartFanMode(
                            actualMode,
                            desktopProtocol);
                        var knownFirmwareValue = actualMode is 0 or 1 or 2 or 3 or 224 or 255;
                        Log.Info(
                            $"GetSmartFanMode readback: {actualMode} ({actualLogicalMode}); " +
                            $"expected {firmwareMode} ({mode})");
                        if (!knownFirmwareValue || actualLogicalMode != mode)
                            throw new InvalidOperationException(
                                $"SmartFan mode was not applied: requested {firmwareMode} ({mode}), " +
                                $"read back {actualMode} ({actualLogicalMode})");
                        return;
                    }
                }

                throw new InvalidOperationException("No LENOVO_GAMEZONE_DATA objects were found");
            }
            catch (Exception ex)
            {
                Log.Error($"SetSmartFanMode failed for {mode}", ex);
                throw;
            }
        }).ConfigureAwait(false);
    }

    public async Task<List<FanInfo>> DiscoverFansAsync()
    {
        return await Task.Run(() =>
        {
            uint legacyFanList = 0;
            Log.Info("Calling GetFanList...");
            try
            {
                var mos = new ManagementObjectSearcher(Scope, GameZoneQuery);
                foreach (ManagementObject mo in mos.Get())
                {
                    var inParams = mo.GetMethodParameters("GetFanList");
                    var outParams = mo.InvokeMethod("GetFanList", inParams, null);
                    var fanList = outParams?["Data"];
                    Log.Info($"GetFanList returned: {fanList} (type: {fanList?.GetType().Name})");
                    if (fanList != null)
                        legacyFanList = Convert.ToUInt32(fanList);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"GetFanList failed: {ex.Message}");
            }

            LogFanZoneSupportList();
            LogFanTableData();

            var waterCoolingSupported = GetWaterCoolingSupport();
            var records = ReadFanTableRecords();
            var discoveredFans = FanFirmwareCompatibility
                .DiscoverFans(legacyFanList, records, waterCoolingSupported)
                .ToList();
            var desktopProtocol = UsesDesktopFanProtocol();
            var fans = discoveredFans
                .SelectMany(discovered =>
                {
                    IReadOnlyList<int> telemetryIds = desktopProtocol
                        ? FanFirmwareCompatibility.GetDesktopFanTelemetryIds(
                            discovered.FanId,
                            legacyFanList)
                        : [discovered.FanId];
                    return telemetryIds.Select((telemetryId, index) =>
                        PopulateFanTelemetry(
                            discovered,
                            telemetryId,
                            index + 1,
                            telemetryIds.Count));
                })
                .ToList();

            if (fans.Count == 0)
                Log.Warn("No fan mappings were exposed by LENOVO_FAN_TABLE_DATA; the opaque GetFanList value was not expanded into guessed fan IDs");

            Log.Info($"Discovered {fans.Count} fan(s)");
            return fans;
        }).ConfigureAwait(false);
    }

    private static List<FanTableRecord> ReadFanTableRecords()
    {
        var records = new List<FanTableRecord>();
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, FanTableDataQuery);
            foreach (ManagementObject table in searcher.Get())
            {
                using (table)
                {
                    var fanId = GetIntProperty(table, "Fan_Id", "FanId", "Fan_ID");
                    var sensorId = GetIntProperty(table, "Sensor_ID", "SensorId", "Sensor_Id");
                    if (fanId == null || sensorId == null)
                        continue;

                    var maxRpm = GetIntProperty(
                        table,
                        "CurrentFanMaxSpeed",
                        "DefaultFanMaxSpeed",
                        "Fan_Max_Speed",
                        "FanMaxSpeed") ?? 2500;
                    var minRpm = GetIntProperty(
                        table,
                        "CurrentFanMinSpeed",
                        "DefaultFanMinSpeed",
                        "Fan_Min_Speed",
                        "FanMinSpeed") ?? 0;
                    var fanSpeeds = GetUShortArrayProperty(
                        table,
                        "FanSpeed",
                        "FanTable_Data");
                    var sensorTemperatures = GetUShortArrayProperty(
                        table,
                        "SensorTemperature",
                        "SensorTable_Data");
                    var uiSpeedRatio = Math.Max(
                        1,
                        GetIntProperty(table, "UISpeedRatio") ?? 1);
                    var active = GetBoolProperty(table, "Active") ?? true;
                    var temperature = GetGameZoneSensorTemperature(sensorId.Value);
                    records.Add(new FanTableRecord(
                        fanId.Value,
                        sensorId.Value,
                        maxRpm,
                        temperature,
                        minRpm,
                        fanSpeeds,
                        sensorTemperatures,
                        uiSpeedRatio,
                        active));
                    Log.Info(
                        $"Fan table mapping: fan={fanId}, sensor={sensorId}, " +
                        $"active={active}, minSpeed={minRpm}, maxSpeed={maxRpm}, " +
                        $"uiSpeedRatio={uiSpeedRatio}, speedPoints={fanSpeeds?.Length ?? 0}, " +
                        $"temperaturePoints={sensorTemperatures?.Length ?? 0}, temp={temperature}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"LENOVO_FAN_TABLE_DATA discovery failed: {ex.Message}");
        }

        return records;
    }

    private static int? GetIntProperty(ManagementBaseObject source, params string[] names)
    {
        foreach (PropertyData property in source.Properties)
        {
            if (property.Value == null ||
                !names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                return Convert.ToInt32(property.Value);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static ushort[]? GetUShortArrayProperty(
        ManagementBaseObject source,
        params string[] names)
    {
        foreach (PropertyData property in source.Properties)
        {
            if (property.Value is not Array values ||
                !names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                return values.Cast<object>()
                    .Select(Convert.ToUInt16)
                    .ToArray();
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool? GetBoolProperty(ManagementBaseObject source, params string[] names)
    {
        foreach (PropertyData property in source.Properties)
        {
            if (property.Value == null ||
                !names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                return property.Value is bool value
                    ? value
                    : Convert.ToInt32(property.Value) != 0;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private FanInfo PopulateFanTelemetry(
        FanInfo discovered,
        int telemetryId,
        int channelNumber,
        int channelCount)
    {
        try
        {
            var nameResourceKey = discovered.NameResourceKey ?? "FanNameN";
            object? nameResourceArgument = discovered.NameResourceKey == null
                ? channelCount > 1
                    ? $"{discovered.FanId}.{channelNumber}"
                    : discovered.FanId
                : null;
            var name = nameResourceArgument == null
                ? LocalizationService.Get(nameResourceKey)
                : LocalizationService.Get(nameResourceKey, nameResourceArgument);
            var rpm = GetGameZoneFanSpeed(telemetryId);
            var info = new FanInfo
            {
                FanId = discovered.FanId,
                TelemetryId = telemetryId,
                SensorId = discovered.SensorId,
                Name = name,
                NameResourceKey = nameResourceKey,
                NameResourceArgument = nameResourceArgument,
                CurrentRpm = FanFirmwareCompatibility.NormalizeRpm(
                    rpm,
                    discovered.MinRpm > 0),
                Temperature = discovered.Temperature,
                MaxRpm = discovered.MaxRpm,
                MinRpm = discovered.MinRpm,
                HasFirmwareRpmRange = discovered.HasFirmwareRpmRange,
                IsAvailable = true
            };
            Log.Info(
                $"Discovered fan zone {info.FanId}, telemetry channel 0x{telemetryId:X}: " +
                $"{info.Name}, sensor={info.SensorId}, RPM={info.CurrentRpm}, Temp={info.Temperature}");
            return info;
        }
        catch (Exception ex)
        {
            Log.Warn(
                $"Fan zone {discovered.FanId}, telemetry channel 0x{telemetryId:X} " +
                $"failed: {ex.Message}");
            return new FanInfo
            {
                FanId = discovered.FanId,
                TelemetryId = telemetryId,
                SensorId = discovered.SensorId,
                Name = discovered.Name,
                NameResourceKey = discovered.NameResourceKey,
                MaxRpm = discovered.MaxRpm,
                MinRpm = discovered.MinRpm,
                HasFirmwareRpmRange = discovered.HasFirmwareRpmRange,
                IsAvailable = false
            };
        }
    }

    private static bool GetWaterCoolingSupport()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, GameZoneQuery);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                using (var inParams = mo.GetMethodParameters("IsSupportWaterCooling"))
                using (var outParams = mo.InvokeMethod("IsSupportWaterCooling", inParams, null))
                {
                    var supported = Convert.ToInt32(outParams?["Data"] ?? 0) != 0;
                    Log.Info($"IsSupportWaterCooling = {supported}");
                    return supported;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"IsSupportWaterCooling failed: {ex.Message}");
        }

        return false;
    }

    public async Task<int> GetFanSpeedAsync(int fanId)
    {
        return await Task.Run(() => GetGameZoneFanSpeed(fanId)).ConfigureAwait(false);
    }

    public async Task<int> GetSensorTemperatureAsync(int sensorId)
    {
        return await Task.Run(() => GetGameZoneSensorTemperature(sensorId)).ConfigureAwait(false);
    }

    public async Task SetFanTableAsync(int fanId, byte[] fanTable)
    {
        if (fanTable.Length != 10)
            throw new ArgumentException($"Fan table must have 10 elements, got {fanTable.Length}", nameof(fanTable));

        var tempTable = new FanTable { Speeds = fanTable };
        if (!tempTable.IsValid())
            throw new ArgumentException(
                "Fan table must be non-decreasing, use values 0-10, and meet Lenovo's minimum safe curve",
                nameof(fanTable));

        Log.Info($"SetFanTableAsync(fan={fanId}, [{string.Join(", ", fanTable)}])");
        await Task.Run(() =>
        {
            try
            {
                if (UsesDesktopFanProtocol())
                {
                    SetDesktopFanTable(fanId, fanTable);
                    return;
                }

                var payload = FanFirmwareCompatibility.BuildFanTablePayload(fanTable);
                Log.Info(
                    $"Serialized notebook fan table payload: size={payload.Length}, " +
                    $"bytes=[{string.Join(", ", payload)}]");

                using var searcher = new ManagementObjectSearcher(Scope, FanMethodQuery);
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    using (var inParams = mo.GetMethodParameters("Fan_Set_Table"))
                    using (var outParams = InvokeFanSetTable(mo, inParams, payload))
                    {
                        var returnValue = outParams?["ReturnValue"];
                        if (returnValue is bool success && !success)
                            throw new InvalidOperationException("Fan_Set_Table returned false");

                        Log.Info($"Fan_Set_Table succeeded (ReturnValue={returnValue ?? "not reported"})");
                        return;
                    }
                }

                throw new InvalidOperationException("No LENOVO_FAN_METHOD objects were found");
            }
            catch (Exception ex)
            {
                Log.Error("Fan_Set_Table failed", ex);
                throw;
            }
        }).ConfigureAwait(false);
    }

    private static void SetDesktopFanTable(int fanId, byte[] fanTable)
    {
        var completeRecords = ReadFanTableRecords()
            .Where(record =>
                record.FanId == fanId &&
                record.FanSpeeds?.Length == 8 &&
                record.SensorTemperatures?.Length == 8)
            .GroupBy(record => (record.FanId, record.SensorId))
            .Select(group => group.First())
            .ToArray();
        var activeRecords = completeRecords.Where(record => record.Active).ToArray();
        var recordsToWrite = activeRecords.Length > 0 ? activeRecords : completeRecords;
        if (recordsToWrite.Length == 0)
        {
            throw new InvalidOperationException(
                "LENOVO_FAN_TABLE_DATA did not expose any complete desktop fan tables");
        }

        using var searcher = new ManagementObjectSearcher(Scope, FanMethodQuery);
        foreach (ManagementObject method in searcher.Get())
        {
            using (method)
            {
                var appliedCount = 0;
                foreach (var record in recordsToWrite)
                {
                    var existingSpeeds = record.FanSpeeds!;
                    var minimumSpeed = record.MinRpm > 0
                        ? record.MinRpm
                        : existingSpeeds.Min(value => (int)value);
                    var maximumSpeed = record.MaxRpm > minimumSpeed
                        ? record.MaxRpm
                        : existingSpeeds.Max(value => (int)value);
                    var speeds = FanFirmwareCompatibility.BuildDesktopFanSpeeds(
                        fanTable,
                        minimumSpeed,
                        maximumSpeed);
                    var payload = FanFirmwareCompatibility.BuildDesktopFanTablePayload(
                        record.FanId,
                        record.SensorId,
                        speeds,
                        record.SensorTemperatures!);

                    using var inParams = method.GetMethodParameters("Fan_Set_Table");
                    using var outParams = InvokeFanSetTable(method, inParams, payload);
                    var returnValue = outParams?["ReturnValue"];
                    if (returnValue is bool success && !success)
                    {
                        throw new InvalidOperationException(
                            $"Fan_Set_Table returned false for fan {record.FanId}, " +
                            $"sensor {record.SensorId}");
                    }

                    appliedCount++;
                    Log.Info(
                        $"Fan_Set_Table succeeded for desktop fan {record.FanId}, " +
                        $"sensor {record.SensorId}: raw speeds=[{string.Join(", ", speeds)}], " +
                        $"uiSpeedRatio={record.UiSpeedRatio}, ReturnValue={returnValue ?? "not reported"}");
                }

                Log.Info($"Applied desktop fan curve to {appliedCount} fan/sensor table(s)");
                return;
            }
        }

        throw new InvalidOperationException("No LENOVO_FAN_METHOD objects were found");
    }

    private static ManagementBaseObject? InvokeFanSetTable(
        ManagementObject method,
        ManagementBaseObject inParams,
        byte[] payload)
    {
        inParams["FanTable"] = payload;
        return method.InvokeMethod("Fan_Set_Table", inParams, null);
    }

    public async Task<bool> IsFullSpeedSupportedAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var mos = new ManagementObjectSearcher(Scope, OtherMethodQuery);
                foreach (ManagementObject mo in mos.Get())
                {
                    var inParams = mo.GetMethodParameters("IsSupportFullSpeedMode");
                    var outParams = mo.InvokeMethod("IsSupportFullSpeedMode", inParams, null);
                    var supported = Convert.ToInt32(outParams?["Data"] ?? 0) != 0;
                    Log.Info($"IsSupportFullSpeedMode = {supported}");
                    return supported;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"IsFullSpeedSupported failed: {ex.Message}");
            }

            return false;
        }).ConfigureAwait(false);
    }

    public async Task<bool> GetFullSpeedAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var mos = new ManagementObjectSearcher(Scope, OtherMethodQuery);
                foreach (ManagementObject mo in mos.Get())
                {
                    var inParams = mo.GetMethodParameters("IsSupportFullSpeedMode");
                    var outParams = mo.InvokeMethod("IsSupportFullSpeedMode", inParams, null);
                    var supported = Convert.ToInt32(outParams?["Data"] ?? 0);
                    Log.Info($"IsSupportFullSpeedMode = {supported}");
                    if (supported == 0) return false;

                    var inParams2 = mo.GetMethodParameters("GetFeatureValue");
                    inParams2["IDs"] = FanFullSpeedFeatureId;
                    var outParams2 = mo.InvokeMethod("GetFeatureValue", inParams2, null);
                    var val = Convert.ToInt32(outParams2?["value"] ?? 0);
                    Log.Info($"GetFullSpeed via GetFeatureValue(0x{FanFullSpeedFeatureId:X8}) = {val}");
                    return val != 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"GetFullSpeed failed: {ex.Message}");
            }
            return false;
        }).ConfigureAwait(false);
    }

    public async Task SetFullSpeedAsync(bool enabled)
    {
        Log.Info($"SetFullSpeedAsync({enabled})");
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(Scope, OtherMethodQuery);
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        using (var inParams = mo.GetMethodParameters("SetFeatureValue"))
                        {
                            inParams["IDs"] = FanFullSpeedFeatureId;
                            inParams["value"] = enabled ? 1 : 0;
                            mo.InvokeMethod("SetFeatureValue", inParams, null);
                        }

                        using var verifyParams = mo.GetMethodParameters("GetFeatureValue");
                        verifyParams["IDs"] = FanFullSpeedFeatureId;
                        using var verifyResult = mo.InvokeMethod("GetFeatureValue", verifyParams, null);
                        var actual = Convert.ToInt32(verifyResult?["value"] ?? -1);
                        Log.Info(
                            $"SetFullSpeed via SetFeatureValue(0x{FanFullSpeedFeatureId:X8}, {enabled}) " +
                            $"readback={actual}");
                        if ((actual != 0) != enabled)
                            throw new InvalidOperationException(
                                $"Full-speed mode was not applied: requested {enabled}, read back {actual}");
                        return;
                    }
                }

                throw new InvalidOperationException("No LENOVO_OTHER_METHOD objects were found");
            }
            catch (Exception ex)
            {
                Log.Error("SetFullSpeed failed", ex);
                throw;
            }
        }).ConfigureAwait(false);
    }

    private static int GetGameZoneFanSpeed(int fanId)
    {
        try
        {
            var mos = new ManagementObjectSearcher(Scope, GameZoneQuery);
            foreach (ManagementObject mo in mos.Get())
            {
                var inParams = mo.GetMethodParameters("GetFanSpeed");
                inParams["Idx"] = fanId;
                var outParams = mo.InvokeMethod("GetFanSpeed", inParams, null);
                if (outParams == null) return -1;
                var result = outParams["Data"];
                if (result == null) return 0;
                if (result is int i) return i;
                if (result is uint u) return (int)u;
                return Convert.ToInt32(result);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GetFanSpeed({fanId}) failed: {ex.Message}");
        }
        return -1;
    }

    private static int GetGameZoneSensorTemperature(int sensorId)
    {
        try
        {
            var mos = new ManagementObjectSearcher(Scope, GameZoneQuery);
            foreach (ManagementObject mo in mos.Get())
            {
                var inParams = mo.GetMethodParameters("GetSensorTemperature");
                inParams["Idx"] = sensorId;
                var outParams = mo.InvokeMethod("GetSensorTemperature", inParams, null);
                if (outParams == null) return 0;
                var result = outParams["Data"];
                if (result == null) return 0;
                if (result is int i) return i;
                if (result is uint u) return (int)u;
                return Convert.ToInt32(result);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GetSensorTemperature({sensorId}) failed: {ex.Message}");
        }
        return 0;
    }

    private static void LogFanZoneSupportList()
    {
        try
        {
            var mos = new ManagementObjectSearcher(Scope, FanMethodQuery);
            foreach (ManagementObject mo in mos.Get())
            {
                var inParams = mo.GetMethodParameters("GetFanZoneSupportList");
                var outParams = mo.InvokeMethod("GetFanZoneSupportList", inParams, null);
                var data = outParams?["Data"];
                Log.Info($"GetFanZoneSupportList returned: {data} (type: {data?.GetType().Name})");
                if (data is Array arr)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in arr)
                        sb.Append($"{item} ");
                    Log.Info($"  FanZoneSupportList array: [{sb}]");
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GetFanZoneSupportList failed: {ex.Message}");
        }
    }

    private static void LogFanTableData()
    {
        for (var fanId = 0; fanId < 4; fanId++)
        {
            for (var sensorId = 0; sensorId < 6; sensorId++)
            {
                try
                {
                    var mos = new ManagementObjectSearcher(Scope, FanMethodQuery);
                    foreach (ManagementObject mo in mos.Get())
                    {
                        var inParams = mo.GetMethodParameters("Fan_Get_Table");
                        inParams["FanID"] = fanId;
                        inParams["SensorID"] = sensorId;
                        var outParams = mo.InvokeMethod("Fan_Get_Table", inParams, null);
                        var table = outParams?["FanTable"];
                        var size = outParams?["FanTableSize"];
                        if (table is byte[] bytes)
                        {
                            Log.Info($"Fan_Get_Table(FanID={fanId}, SensorID={sensorId}): size={size}, bytes=[{string.Join(", ", bytes)}]");
                        }
                        else if (table != null)
                        {
                            Log.Info($"Fan_Get_Table(FanID={fanId}, SensorID={sensorId}): size={size}, table={table} ({table.GetType().Name})");
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Info($"Fan_Get_Table(FanID={fanId}, SensorID={sensorId}) failed: {ex.Message}");
                }
            }
        }
    }

}

public sealed record ConflictShutdownResult(
    IReadOnlyList<string> Stopped,
    IReadOnlyList<string> Failed);
