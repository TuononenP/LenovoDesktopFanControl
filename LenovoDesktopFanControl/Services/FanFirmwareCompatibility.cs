using System.Buffers.Binary;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

internal sealed record FanTableRecord(
    int FanId,
    int SensorId,
    int MaxRpm,
    int Temperature = -1,
    int MinRpm = 0,
    ushort[]? FanSpeeds = null,
    ushort[]? SensorTemperatures = null,
    int UiSpeedRatio = 1,
    bool Active = true);

internal static class FanFirmwareCompatibility
{
    private const int FirmwareFanTablePayloadLength = 64;
    private const int FirmwareFanTablePointCount = 10;
    private const int DesktopFanTablePointCount = 8;

    public static byte[] BuildFanTablePayload(byte[] fanTable)
    {
        if (fanTable.Length != FirmwareFanTablePointCount)
            throw new ArgumentException(
                $"Fan table must contain exactly {FirmwareFanTablePointCount} points",
                nameof(fanTable));
        if (fanTable.Any(value => value > 10))
            throw new ArgumentOutOfRangeException(
                nameof(fanTable),
                "Fan table values must be in the firmware range 0-10");

        // Notebook firmware uses Lenovo's 64-byte FST structure:
        // FSTM (custom mode 4), FSID (enabled), FSTL (10), then ten
        // FSS values (uint16). Remaining bytes are reserved.
        var payload = new byte[FirmwareFanTablePayloadLength];
        payload[0] = 4;
        payload[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(2, sizeof(uint)),
            FirmwareFanTablePointCount);

        const int speedValuesOffset = 6;
        for (var i = 0; i < fanTable.Length; i++)
        {
            var offset = speedValuesOffset + (i * sizeof(ushort));
            payload[offset] = fanTable[i];
            payload[offset + 1] = 0;
        }

        return payload;
    }

    public static byte[] BuildDesktopFanTablePayload(
        int fanId,
        int sensorId,
        IReadOnlyList<ushort> fanSpeeds,
        IReadOnlyList<ushort> sensorTemperatures)
    {
        if (fanId is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(fanId));
        if (sensorId is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sensorId));
        if (fanSpeeds.Count != DesktopFanTablePointCount)
            throw new ArgumentException(
                $"Desktop fan table must contain exactly {DesktopFanTablePointCount} speeds",
                nameof(fanSpeeds));
        if (sensorTemperatures.Count != DesktopFanTablePointCount)
            throw new ArgumentException(
                $"Desktop fan table must contain exactly {DesktopFanTablePointCount} temperatures",
                nameof(sensorTemperatures));

        // Lenovo's desktop SEThermalMode plugin serializes this packed record:
        // FanID (byte), speed count (uint32), eight speeds (uint16),
        // SensorID (byte), temperature count (uint32), eight temperatures
        // (uint16), followed by zero-filled reserved bytes.
        var payload = new byte[FirmwareFanTablePayloadLength];
        payload[0] = (byte)fanId;
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(1, sizeof(uint)),
            DesktopFanTablePointCount);
        for (var i = 0; i < fanSpeeds.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload.AsSpan(5 + i * sizeof(ushort), sizeof(ushort)),
                fanSpeeds[i]);
        }

        const int sensorIdOffset = 5 + DesktopFanTablePointCount * sizeof(ushort);
        payload[sensorIdOffset] = (byte)sensorId;
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(sensorIdOffset + 1, sizeof(uint)),
            DesktopFanTablePointCount);
        var temperaturesOffset = sensorIdOffset + 1 + sizeof(uint);
        for (var i = 0; i < sensorTemperatures.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload.AsSpan(temperaturesOffset + i * sizeof(ushort), sizeof(ushort)),
                sensorTemperatures[i]);
        }

        return payload;
    }

    public static ushort[] BuildDesktopFanSpeeds(
        byte[] fanTable,
        int minimumSpeed,
        int maximumSpeed)
    {
        if (fanTable.Length != FirmwareFanTablePointCount)
            throw new ArgumentException(
                $"Fan table must contain exactly {FirmwareFanTablePointCount} points",
                nameof(fanTable));
        if (fanTable.Any(value => value > 10))
            throw new ArgumentOutOfRangeException(
                nameof(fanTable),
                "Fan table values must be in the firmware range 0-10");

        minimumSpeed = Math.Clamp(minimumSpeed, 0, ushort.MaxValue);
        maximumSpeed = Math.Clamp(maximumSpeed, minimumSpeed, ushort.MaxValue);
        var result = new ushort[DesktopFanTablePointCount];
        for (var i = 0; i < result.Length; i++)
        {
            var sourceIndex = (int)Math.Round(
                i * (fanTable.Length - 1d) / (result.Length - 1d),
                MidpointRounding.AwayFromZero);
            var level = fanTable[sourceIndex];
            result[i] = (ushort)Math.Round(
                minimumSpeed + (maximumSpeed - minimumSpeed) * (level / 10d),
                MidpointRounding.AwayFromZero);
        }

        return result;
    }

    public static IReadOnlyList<int> GetDesktopFanTelemetryIds(int fanId, uint supportedFanMask)
    {
        int[] candidates = fanId switch
        {
            1 => [0x0001],
            3 => [0x0010, 0x0020, 0x0040],
            5 => [0x0080, 0x0100, 0x0200],
            4 => [0x0400, 0x0800, 0x1000],
            _ => [fanId]
        };

        if (supportedFanMask == 0)
            return candidates;

        var supported = candidates
            .Where(candidate => ((uint)candidate & supportedFanMask) != 0)
            .ToArray();
        return supported.Length > 0 ? supported : [fanId];
    }

    public static SmartFanMode DecodeSmartFanMode(int firmwareValue, bool zeroBasedDesktopProtocol)
    {
        if (firmwareValue is 224 or 255)
            return SmartFanMode.Custom;

        if (zeroBasedDesktopProtocol)
        {
            return firmwareValue switch
            {
                0 => SmartFanMode.Quiet,
                1 => SmartFanMode.Balanced,
                2 or 3 => SmartFanMode.Performance,
                _ => SmartFanMode.Balanced
            };
        }

        return firmwareValue switch
        {
            0 or 1 => SmartFanMode.Quiet,
            2 => SmartFanMode.Balanced,
            3 => SmartFanMode.Performance,
            _ => SmartFanMode.Balanced
        };
    }

    public static int EncodeSmartFanMode(SmartFanMode mode, bool zeroBasedDesktopProtocol)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SmartFan mode");
        if (mode == SmartFanMode.Custom)
            return 255;
        return zeroBasedDesktopProtocol ? (int)mode - 1 : (int)mode;
    }

    public static IReadOnlyList<FanInfo> DiscoverFans(
        uint legacyFanList,
        IReadOnlyList<FanTableRecord> tableRecords,
        bool waterCoolingSupported = false)
    {
        return tableRecords
            .GroupBy(record => record.FanId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var sensor = group
                    .OrderByDescending(record => IsValidTemperature(record.Temperature))
                    .ThenBy(record => record.SensorId)
                    .First();
                var nameResourceKey = InferFanNameResourceKey(group, waterCoolingSupported);
                return new FanInfo
                {
                    FanId = group.Key,
                    SensorId = sensor.SensorId,
                    Temperature = IsValidTemperature(sensor.Temperature) ? sensor.Temperature : null,
                    MaxRpm = NormalizeMaxRpm(group.Max(record => record.MaxRpm)),
                    MinRpm = NormalizeMinimumRpm(group.Max(record => record.MinRpm)),
                    HasFirmwareRpmRange = group.Any(record => record.MaxRpm > 0),
                    NameResourceKey = nameResourceKey,
                    IsAvailable = true
                };
            })
            .ToList();
    }

    private static string? InferFanNameResourceKey(
        IEnumerable<FanTableRecord> records,
        bool waterCoolingSupported)
    {
        foreach (var record in records)
        {
            // These pairs are consistent across Lenovo's known GameZone table
            // generations. Other pairs remain generic unless the tested
            // water-cooling layout is detected below.
            if (waterCoolingSupported && (record.FanId, record.SensorId) is (1, 1))
                return "FanNamePump";
            if (waterCoolingSupported)
            {
                // On the tested Legion T7 water-cooling layout, the firmware
                // exposes the three front/radiator fans, two top fans, and the
                // rear fan as control zones 3, 4, and 5 respectively.
                return record.FanId switch
                {
                    3 => "FanNameFrontRadiator",
                    4 => "FanNameTopFans",
                    5 => "FanNameRearFan",
                    _ => InferKnownFanName(record)
                };
            }
            return InferKnownFanName(record);
        }

        return null;
    }

    private static string? InferKnownFanName(FanTableRecord record)
    {
        if ((record.FanId, record.SensorId) is (0, 3) or (1, 1))
            return "FanNameCpu";
        if ((record.FanId, record.SensorId) is (2, 5))
            return "FanNameGpu";

        return null;
    }

    public static bool IsValidTemperature(int temperature) => temperature is > 0 and <= 125;

    public static int? NormalizeRpm(int rpm) => rpm >= 0 ? rpm : null;

    public static int? NormalizeRpm(int rpm, bool positiveSpeedExpected) =>
        rpm < 0 || (rpm == 0 && positiveSpeedExpected) ? null : rpm;

    public static int NormalizeMaxRpm(int maxSpeed)
    {
        if (maxSpeed <= 0)
            return 2500;
        return maxSpeed <= 500 ? maxSpeed * 10 : maxSpeed;
    }

    public static int NormalizeMinimumRpm(int minSpeed)
    {
        if (minSpeed <= 0)
            return 0;
        return minSpeed <= 500 ? minSpeed * 10 : minSpeed;
    }

    public static double FanLevelToPercentage(
        int level,
        int minimumRpm,
        int maximumRpm)
    {
        level = Math.Clamp(level, 0, 10);
        if (maximumRpm <= 0 || minimumRpm < 0 || minimumRpm > maximumRpm)
            return level * 10d;

        var rpm = minimumRpm + (maximumRpm - minimumRpm) * (level / 10d);
        return rpm / maximumRpm * 100d;
    }

    public static byte PercentageToFanLevel(
        double percentage,
        int minimumRpm,
        int maximumRpm)
    {
        if (maximumRpm <= 0 || minimumRpm < 0 || minimumRpm >= maximumRpm)
            return (byte)Math.Clamp(
                (int)Math.Round(percentage / 10d, MidpointRounding.AwayFromZero),
                0,
                10);

        var requestedRpm = Math.Clamp(percentage, 0, 100) / 100d * maximumRpm;
        var level = (requestedRpm - minimumRpm) / (maximumRpm - minimumRpm) * 10d;
        return (byte)Math.Clamp(
            (int)Math.Round(level, MidpointRounding.AwayFromZero),
            0,
            10);
    }

    public static ConflictShutdownResult ReconcileConflictShutdown(
        IReadOnlyCollection<string> requested,
        IReadOnlyCollection<string> stopRequestsAccepted,
        IReadOnlyCollection<string> remaining)
    {
        var failed = requested
            .Intersect(remaining, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stopped = requested
            .Except(failed, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ConflictShutdownResult(stopped, failed);
    }
}
