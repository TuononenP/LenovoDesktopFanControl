using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class LenovoTowerLightingPersistenceTests
{
    [Theory]
    [InlineData(0x17EF, 0xC955, 0xFF89, 0x00CC, 64, true)]
    [InlineData(0x17EF, 0xC955, 0xFF89, 0x0010, 64, false)]
    [InlineData(0x17EF, 0xC955, 0x0059, 0x0001, 51, false)]
    [InlineData(0x17EF, 0xC955, 0xFF89, 0x00CC, 63, false)]
    [InlineData(0x17EF, 0xC956, 0xFF89, 0x00CC, 64, false)]
    public void IsSupportedHidInterface_OnlyMatchesThePersistentLightingCollection(
        ushort vendorId,
        ushort productId,
        ushort usagePage,
        ushort usage,
        ushort featureReportLength,
        bool expected)
    {
        Assert.Equal(
            expected,
            LenovoTowerLightingPersistence.IsSupportedHidInterface(
                vendorId,
                productId,
                usagePage,
                usage,
                featureReportLength));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(0.49, 2)]
    [InlineData(0.5, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    public void MapBrightness_UsesTheFourFirmwareLevels(double input, byte expected)
    {
        Assert.Equal(expected, LenovoTowerLightingPersistence.MapBrightness(input));
    }

    [Fact]
    public void BuildFeatureReports_ProgramsAndSavesAStaticPurpleProfile()
    {
        var reports = LenovoTowerLightingPersistence.BuildFeatureReports(
            zone: 0x12,
            red: 145,
            green: 85,
            blue: 255,
            brightness: 3);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Equal(64, report.Length));

        var apply = reports[0];
        Assert.Equal((byte)0xCC, apply[0]);
        Assert.Equal((byte)0x12, apply[1]);
        Assert.Equal((byte)0x01, apply[2]);
        Assert.Equal((byte)0x01, apply[3]);
        Assert.Equal((byte)0x03, apply[4]);
        Assert.Equal((byte)145, apply[5]);
        Assert.Equal((byte)85, apply[6]);
        Assert.Equal((byte)255, apply[7]);

        var save = reports[1];
        Assert.Equal((byte)0xCC, save[0]);
        Assert.Equal((byte)0x28, save[1]);
        Assert.Equal((byte)0x06, save[2]);
        Assert.Equal((byte)0x12, save[33]);
        Assert.Equal((byte)0x01, save[34]);
        Assert.Equal((byte)0x01, save[35]);
        Assert.Equal((byte)0x03, save[36]);
        Assert.Equal((byte)145, save[37]);
        Assert.Equal((byte)85, save[38]);
        Assert.Equal((byte)255, save[39]);
    }

    [Fact]
    public void DeferredPersistenceArguments_RoundTripInvariantLightingState()
    {
        var arguments = LenovoTowerLightingPersistence.BuildDeferredPersistenceArguments(
            parentProcessId: 1234,
            red: 145,
            green: 85,
            blue: 255,
            brightness: 0.37,
            enabled: true);

        var parsed = LenovoTowerLightingPersistence.TryParseDeferredPersistenceRequest(
            arguments,
            out var request);

        Assert.True(parsed);
        Assert.Equal(1234, request.ParentProcessId);
        Assert.Equal((byte)145, request.Red);
        Assert.Equal((byte)85, request.Green);
        Assert.Equal((byte)255, request.Blue);
        Assert.Equal(0.37, request.Brightness);
        Assert.True(request.Enabled);
    }

    [Theory]
    [InlineData("--persist-lighting-after-exit", "0", "145", "85", "255", "0.5", "1")]
    [InlineData("--persist-lighting-after-exit", "1234", "256", "85", "255", "0.5", "1")]
    [InlineData("--persist-lighting-after-exit", "1234", "145", "85", "255", "0.5", "2")]
    [InlineData("--unknown-mode", "1234", "145", "85", "255", "0.5", "1")]
    public void DeferredPersistenceArguments_RejectInvalidRequests(params string[] arguments)
    {
        Assert.False(
            LenovoTowerLightingPersistence.TryParseDeferredPersistenceRequest(
                arguments,
                out _));
    }
}
