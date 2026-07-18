using System.Buffers.Binary;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using Xunit;

namespace LenovoDesktopFanControl.Tests;

public class FanFirmwareCompatibilityTests
{
    [Fact]
    public void BuildFanTablePayload_SerializesLenovo64ByteStructure()
    {
        byte[] curve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        var result = FanFirmwareCompatibility.BuildFanTablePayload(curve);

        Assert.Equal(64, result.Length);
        Assert.Equal(4, result[0]);
        Assert.Equal(1, result[1]);
        Assert.Equal([10, 0, 0, 0], result[2..6]);
        Assert.Equal(
            [1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 9, 0, 10, 0],
            result[6..26]);
        Assert.All(result[26..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void BuildDesktopFanTablePayload_SerializesLenovoDesktopStructure()
    {
        ushort[] speeds = [10, 20, 30, 40, 50, 60, 70, 80];
        ushort[] temperatures = [20, 30, 40, 50, 60, 70, 80, 90];

        var result = FanFirmwareCompatibility.BuildDesktopFanTablePayload(
            3,
            2,
            speeds,
            temperatures);

        Assert.Equal(64, result.Length);
        Assert.Equal(3, result[0]);
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(1, 4)));
        Assert.Equal(speeds, ReadUShorts(result, 5, 8));
        Assert.Equal(2, result[21]);
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(22, 4)));
        Assert.Equal(temperatures, ReadUShorts(result, 26, 8));
        Assert.All(result[42..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void BuildDesktopFanSpeeds_ResamplesCurveAndMapsItToRawFirmwareRange()
    {
        byte[] curve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        var result = FanFirmwareCompatibility.BuildDesktopFanSpeeds(curve, 10, 110);

        Assert.Equal(new ushort[] { 20, 30, 50, 60, 70, 80, 100, 110 }, result);
    }

    [Fact]
    public void GetDesktopFanTelemetryIds_ExpandsControlZonesUsingSupportedFanMask()
    {
        var telemetryIds = new[] { 1, 3, 5, 4 }
            .SelectMany(fanId =>
                FanFirmwareCompatibility.GetDesktopFanTelemetryIds(fanId, 3313))
            .ToArray();

        Assert.Equal([1, 16, 32, 64, 128, 1024, 2048], telemetryIds);
    }

    [Theory]
    [InlineData(SmartFanMode.Quiet, true, 0)]
    [InlineData(SmartFanMode.Balanced, true, 1)]
    [InlineData(SmartFanMode.Performance, true, 2)]
    [InlineData(SmartFanMode.Quiet, false, 1)]
    [InlineData(SmartFanMode.Balanced, false, 2)]
    [InlineData(SmartFanMode.Performance, false, 3)]
    [InlineData(SmartFanMode.Custom, true, 255)]
    public void SmartFanMode_UsesProtocolSpecificFirmwareValues(
        SmartFanMode mode,
        bool desktopProtocol,
        int firmwareValue)
    {
        Assert.Equal(
            firmwareValue,
            FanFirmwareCompatibility.EncodeSmartFanMode(mode, desktopProtocol));
        Assert.Equal(
            mode,
            FanFirmwareCompatibility.DecodeSmartFanMode(firmwareValue, desktopProtocol));
    }

    [Theory]
    [InlineData(3, true, SmartFanMode.Performance)]
    [InlineData(0, false, SmartFanMode.Quiet)]
    [InlineData(224, true, SmartFanMode.Custom)]
    [InlineData(224, false, SmartFanMode.Custom)]
    public void DecodeSmartFanMode_AcceptsKnownFirmwareAliases(
        int firmwareValue,
        bool desktopProtocol,
        SmartFanMode expected)
    {
        Assert.Equal(
            expected,
            FanFirmwareCompatibility.DecodeSmartFanMode(firmwareValue, desktopProtocol));
    }

    [Fact]
    public void BuildFanTablePayload_RejectsWrongPointCount()
    {
        Assert.Throws<ArgumentException>(() =>
            FanFirmwareCompatibility.BuildFanTablePayload([1, 2, 3, 4, 5, 6, 7, 8]));
    }

    [Fact]
    public void BuildFanTablePayload_RejectsValuesAboveFirmwareScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FanFirmwareCompatibility.BuildFanTablePayload([1, 2, 3, 4, 5, 6, 7, 8, 9, 11]));
    }

    [Fact]
    public void DiscoverFans_CollapsesSensorRowsAndNormalizesFirmwareMaxSpeed()
    {
        FanTableRecord[] records =
        [
            new(3, 1, 125),
            new(3, 2, 125),
            new(4, 1, 125),
            new(4, 2, 125),
            new(1, 1, 200),
            new(1, 2, 125),
            new(5, 1, 125),
            new(5, 2, 125)
        ];

        var fans = FanFirmwareCompatibility.DiscoverFans(3313, records);

        Assert.Collection(
            fans,
            fan =>
            {
                Assert.Equal((1, 2000), (fan.FanId, fan.MaxRpm));
                Assert.Equal("FanNameCpu", fan.NameResourceKey);
            },
            fan => Assert.Equal((3, 1250), (fan.FanId, fan.MaxRpm)),
            fan => Assert.Equal((4, 1250), (fan.FanId, fan.MaxRpm)),
            fan => Assert.Equal((5, 1250), (fan.FanId, fan.MaxRpm)));
    }

    [Fact]
    public void DiscoverFans_SelectsSensorWithValidWholeCelsiusReading()
    {
        FanTableRecord[] records =
        [
            new(1, 1, 200, -1),
            new(1, 2, 125, 32)
        ];

        var fan = Assert.Single(FanFirmwareCompatibility.DiscoverFans(3313, records));

        Assert.Equal(2, fan.SensorId);
        Assert.Equal(32, fan.Temperature);
    }

    [Fact]
    public void DiscoverFans_IdentifiesWaterCoolingPumpAndNormalizesMinimumSpeed()
    {
        FanTableRecord[] records = [new(1, 1, 200, -1, 80), new(1, 2, 125, 32, 10)];

        var fan = Assert.Single(FanFirmwareCompatibility.DiscoverFans(3313, records, true));

        Assert.Equal("FanNamePump", fan.NameResourceKey);
        Assert.Equal(800, fan.MinRpm);
        Assert.True(fan.HasFirmwareRpmRange);
        Assert.Null(FanFirmwareCompatibility.NormalizeRpm(0, fan.MinRpm > 0));
        Assert.Equal(2008, FanFirmwareCompatibility.NormalizeRpm(2008, fan.MinRpm > 0));
    }

    [Theory]
    [InlineData(0, 800, 2000, 40)]
    [InlineData(1, 800, 2000, 46)]
    [InlineData(5, 800, 2000, 70)]
    [InlineData(10, 800, 2000, 100)]
    [InlineData(1, 100, 1250, 17.2)]
    public void FanLevelToPercentage_UsesReportedFirmwareRpmRange(
        int level,
        int minimumRpm,
        int maximumRpm,
        double expected)
    {
        Assert.Equal(
            expected,
            FanFirmwareCompatibility.FanLevelToPercentage(
                level,
                minimumRpm,
                maximumRpm),
            3);
    }

    [Theory]
    [InlineData(46, 800, 2000, 1)]
    [InlineData(70, 800, 2000, 5)]
    [InlineData(100, 800, 2000, 10)]
    [InlineData(-20, 800, 2000, 0)]
    [InlineData(120, 800, 2000, 10)]
    public void PercentageToFanLevel_ClampsAndMapsReportedFirmwareRange(
        double percentage,
        int minimumRpm,
        int maximumRpm,
        byte expected)
    {
        Assert.Equal(
            expected,
            FanFirmwareCompatibility.PercentageToFanLevel(
                percentage,
                minimumRpm,
                maximumRpm));
    }

    [Fact]
    public void DiscoverFans_NamesTestedWaterCooledT7SystemFanGroups()
    {
        FanTableRecord[] records =
        [
            new(3, 1, 125),
            new(4, 1, 125),
            new(5, 1, 125)
        ];

        var fans = FanFirmwareCompatibility.DiscoverFans(3313, records, waterCoolingSupported: true);

        Assert.Equal(
            ["FanNameFrontRadiator", "FanNameTopFans", "FanNameRearFan"],
            fans.Select(fan => fan.NameResourceKey));
    }

    [Fact]
    public void DiscoverFans_UsesLowestSensorAndUnavailableTemperatureWhenNoneAreValid()
    {
        FanTableRecord[] records = [new(2, 8, 0, 126), new(2, 3, -1, 0)];

        var fan = Assert.Single(FanFirmwareCompatibility.DiscoverFans(0, records));

        Assert.Equal(3, fan.SensorId);
        Assert.Null(fan.Temperature);
        Assert.Equal(2500, fan.MaxRpm);
        Assert.True(fan.IsAvailable);
    }

    [Fact]
    public void DiscoverFans_ReturnsEmptyCollectionWhenFirmwareHasNoTableRows()
    {
        Assert.Empty(FanFirmwareCompatibility.DiscoverFans(uint.MaxValue, []));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(32, true)]
    [InlineData(125, true)]
    [InlineData(126, false)]
    public void IsValidTemperature_RejectsUnavailableAndImplausibleReadings(
        int temperature,
        bool expected)
    {
        Assert.Equal(expected, FanFirmwareCompatibility.IsValidTemperature(temperature));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 0)]
    [InlineData(2002, 2002)]
    public void NormalizeRpm_DistinguishesUnavailableFromStoppedFan(int rpm, int? expected)
    {
        Assert.Equal(expected, FanFirmwareCompatibility.NormalizeRpm(rpm));
    }

    [Theory]
    [InlineData(-1, 2500)]
    [InlineData(0, 2500)]
    [InlineData(1, 10)]
    [InlineData(500, 5000)]
    [InlineData(501, 501)]
    [InlineData(2500, 2500)]
    public void NormalizeMaxRpm_HandlesDefaultsFirmwareUnitsAndRawRpm(int value, int expected)
    {
        Assert.Equal(expected, FanFirmwareCompatibility.NormalizeMaxRpm(value));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(500, 5000)]
    [InlineData(501, 501)]
    [InlineData(3000, 3000)]
    public void NormalizeMinimumRpm_HandlesDefaultsFirmwareUnitsAndRawRpm(int value, int expected)
    {
        Assert.Equal(expected, FanFirmwareCompatibility.NormalizeMinimumRpm(value));
    }

    [Theory]
    [InlineData(-1, false, null)]
    [InlineData(-1, true, null)]
    [InlineData(0, false, 0)]
    [InlineData(0, true, null)]
    [InlineData(800, false, 800)]
    [InlineData(800, true, 800)]
    public void NormalizeRpm_WithPositiveSpeedExpected_HandlesUnavailableAndStoppedFan(
        int rpm,
        bool positiveSpeedExpected,
        int? expected)
    {
        Assert.Equal(expected, FanFirmwareCompatibility.NormalizeRpm(rpm, positiveSpeedExpected));
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(0, true, 0)]
    [InlineData(1, false, 1)]
    [InlineData(2, false, 2)]
    [InlineData(2, true, 2)]
    [InlineData(3, false, 3)]
    [InlineData(3, true, 2)]
    public void EncodeSmartFanMode_RoundTripsQuietBalancedPerformance(
        int firmwareValue,
        bool desktopProtocol,
        int encodedValue)
    {
        var mode = FanFirmwareCompatibility.DecodeSmartFanMode(firmwareValue, desktopProtocol);
        Assert.Equal(encodedValue, FanFirmwareCompatibility.EncodeSmartFanMode(mode, desktopProtocol));
    }

    [Theory]
    [InlineData(SmartFanMode.Custom, true, 255)]
    [InlineData(SmartFanMode.Custom, false, 255)]
    public void EncodeSmartFanMode_CustomReturnsFirmwareConstant(
        SmartFanMode mode,
        bool desktopProtocol,
        int expected)
    {
        Assert.Equal(
            expected,
            FanFirmwareCompatibility.EncodeSmartFanMode(mode, desktopProtocol));
    }

    [Fact]
    public void EncodeSmartFanMode_ThrowsForUndefinedEnumValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FanFirmwareCompatibility.EncodeSmartFanMode((SmartFanMode)99, false));
    }

    [Theory]
    [InlineData(99, false, SmartFanMode.Balanced)]
    [InlineData(99, true, SmartFanMode.Balanced)]
    [InlineData(-1, false, SmartFanMode.Balanced)]
    [InlineData(-1, true, SmartFanMode.Balanced)]
    public void DecodeSmartFanMode_UnknownFirmwareValueDefaultsToBalanced(
        int firmwareValue,
        bool desktopProtocol,
        SmartFanMode expected)
    {
        Assert.Equal(
            expected,
            FanFirmwareCompatibility.DecodeSmartFanMode(firmwareValue, desktopProtocol));
    }

    [Fact]
    public void BuildFanTablePayload_RoundsAllValuesToSixteenBitLittleEndian()
    {
        byte[] curve = [10, 10, 10, 10, 10, 10, 10, 10, 10, 10];

        var result = FanFirmwareCompatibility.BuildFanTablePayload(curve);

        Assert.Equal(64, result.Length);
        Assert.All(result[6..26], value =>
        {
            var evenIndex = Array.IndexOf(result, value) % 2 == 0;
            Assert.True(value == 10 || value == 0);
        });
    }

    [Fact]
    public void BuildFanTablePayload_AcceptsMinimumSafeCurve()
    {
        var result = FanFirmwareCompatibility.BuildFanTablePayload(FanTable.Minimum().Speeds);

        Assert.Equal(64, result.Length);
        Assert.Equal(4, result[0]);
        Assert.Equal(1, result[1]);
    }

    [Fact]
    public void BuildDesktopFanTablePayload_RejectsOutOfRangeFanId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FanFirmwareCompatibility.BuildDesktopFanTablePayload(
                300,
                2,
                new ushort[8],
                new ushort[8]));
    }

    [Fact]
    public void BuildDesktopFanTablePayload_RejectsOutOfRangeSensorId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FanFirmwareCompatibility.BuildDesktopFanTablePayload(
                3,
                -1,
                new ushort[8],
                new ushort[8]));
    }

    [Fact]
    public void BuildDesktopFanTablePayload_RejectsWrongSpeedCount()
    {
        Assert.Throws<ArgumentException>(() =>
            FanFirmwareCompatibility.BuildDesktopFanTablePayload(
                3,
                2,
                new ushort[7],
                new ushort[8]));
    }

    [Fact]
    public void BuildDesktopFanTablePayload_RejectsWrongTemperatureCount()
    {
        Assert.Throws<ArgumentException>(() =>
            FanFirmwareCompatibility.BuildDesktopFanTablePayload(
                3,
                2,
                new ushort[8],
                new ushort[9]));
    }

    [Fact]
    public void BuildDesktopFanSpeeds_RejectsWrongPointCount()
    {
        Assert.Throws<ArgumentException>(() =>
            FanFirmwareCompatibility.BuildDesktopFanSpeeds([1, 2, 3], 10, 100));
    }

    [Fact]
    public void BuildDesktopFanSpeeds_RejectsValuesAboveFirmwareScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FanFirmwareCompatibility.BuildDesktopFanSpeeds(
                [1, 2, 3, 4, 5, 6, 7, 8, 9, 11],
                10,
                100));
    }

    [Theory]
    [InlineData(10, 110, 20, 110)]
    [InlineData(0, 100, 10, 100)]
    [InlineData(50, 50, 50, 50)]
    public void BuildDesktopFanSpeeds_ClampsMinimumAndMaximum(
        int minimumSpeed,
        int maximumSpeed,
        int expectedFirst,
        int expectedLast)
    {
        var result = FanFirmwareCompatibility.BuildDesktopFanSpeeds(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            minimumSpeed,
            maximumSpeed);

        Assert.Equal((ushort)expectedFirst, result[0]);
        Assert.Equal((ushort)expectedLast, result[^1]);
    }

    [Theory]
    [InlineData(1, 0x0001, new[] { 1 })]
    [InlineData(3, 0, new[] { 16, 32, 64 })]
    [InlineData(5, 0, new[] { 128, 256, 512 })]
    [InlineData(4, 0, new[] { 1024, 2048, 4096 })]
    [InlineData(7, 0, new[] { 7 })]
    [InlineData(7, 999, new[] { 7 })]
    public void GetDesktopFanTelemetryIds_ReturnsCandidatesAndFallsBackToFanId(
        int fanId,
        uint supportedFanMask,
        int[] expected)
    {
        var result = FanFirmwareCompatibility.GetDesktopFanTelemetryIds(fanId, supportedFanMask);

        Assert.Equal(expected, result.ToArray());
    }

    [Fact]
    public void DiscoverFans_OrdersByFanId()
    {
        FanTableRecord[] records =
        [
            new(5, 1, 125),
            new(1, 1, 200),
            new(3, 1, 125)
        ];

        var fans = FanFirmwareCompatibility.DiscoverFans(0, records);

        Assert.Collection(
            fans,
            fan => Assert.Equal(1, fan.FanId),
            fan => Assert.Equal(3, fan.FanId),
            fan => Assert.Equal(5, fan.FanId));
    }

    [Fact]
    public void DiscoverFans_AssignsGpuNameForFanIdTwoSensorIdFive()
    {
        FanTableRecord[] records = [new(2, 5, 200, 40)];

        var fan = Assert.Single(FanFirmwareCompatibility.DiscoverFans(0, records));

        Assert.Equal("FanNameGpu", fan.NameResourceKey);
    }

    [Fact]
    public void DiscoverFans_LeavesGenericNameForUnknownFanSensorPairs()
    {
        FanTableRecord[] records = [new(7, 9, 200, 40)];

        var fan = Assert.Single(FanFirmwareCompatibility.DiscoverFans(0, records));

        Assert.Null(fan.NameResourceKey);
    }

    [Fact]
    public void ReconcileConflictShutdown_ReturnsEmptyResultWhenAllStop()
    {
        var result = FanFirmwareCompatibility.ReconcileConflictShutdown(
            ["VantageService"],
            ["VantageService"],
            []);

        Assert.Equal(["VantageService"], result.Stopped);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void ReconcileConflictShutdown_HandlesUnrelatedRemainingProcesses()
    {
        var result = FanFirmwareCompatibility.ReconcileConflictShutdown(
            ["VantageService"],
            ["VantageService"],
            ["GAService"]);

        Assert.Equal(["VantageService"], result.Stopped);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void ReconcileConflictShutdown_ReturnsAllFailedWhenNothingStopped()
    {
        var result = FanFirmwareCompatibility.ReconcileConflictShutdown(
            ["VantageService", "LenovoVantage"],
            [],
            ["VantageService", "LenovoVantage"]);

        Assert.Empty(result.Stopped);
        Assert.Equal(["VantageService", "LenovoVantage"], result.Failed);
    }

    [Fact]
    public void ReconcileConflictShutdown_ReportsConflictThatRemainsAfterAcceptedStopRequest()
    {
        var result = FanFirmwareCompatibility.ReconcileConflictShutdown(
            ["VantageService"],
            ["VantageService"],
            ["VantageService"]);

        Assert.Empty(result.Stopped);
        Assert.Equal(["VantageService"], result.Failed);
    }

    [Fact]
    public void ReconcileConflictShutdown_IsCaseInsensitiveAndPreservesRequestedNames()
    {
        var result = FanFirmwareCompatibility.ReconcileConflictShutdown(
            ["LenovoVantage", "VantageService"],
            ["lenovovantage"],
            ["VANTAGESERVICE"]);

        Assert.Equal(["LenovoVantage"], result.Stopped);
        Assert.Equal(["VantageService"], result.Failed);
    }

    private static ushort[] ReadUShorts(byte[] source, int offset, int count)
    {
        var result = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt16LittleEndian(
                source.AsSpan(offset + i * sizeof(ushort), sizeof(ushort)));
        }

        return result;
    }
}
