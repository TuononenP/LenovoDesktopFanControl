using System.Runtime.InteropServices;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class LenovoRtxGpuLightingControllerTests
{
    [Theory]
    [InlineData(0x2C0210DEu, 0xC77017AAu, true)]
    [InlineData(0x2C0210DEu, 0x000010DEu, false)]
    [InlineData(0x2C0210DEu, 0xC7701043u, false)]
    [InlineData(0x2C0010DEu, 0xC77017AAu, false)]
    [InlineData(0x2C021002u, 0xC77017AAu, false)]
    public void IsSupportedPciDevice_OnlyMatchesLenovoRtx5080(
        uint deviceId,
        uint subsystemId,
        bool expected)
    {
        Assert.Equal(
            expected,
            LenovoRtxGpuLightingController.IsSupportedPciDevice(deviceId, subsystemId));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.49, 3)]
    [InlineData(0.5, 4)]
    [InlineData(0.92, 6)]
    [InlineData(1, 7)]
    [InlineData(2, 7)]
    public void MapBrightness_UsesLenovosEightFirmwareLevels(double input, byte expected)
    {
        Assert.Equal(expected, LenovoRtxGpuLightingController.MapBrightness(input));
    }

    [Fact]
    public void NvI2cInfoV3_HasTheLayoutExpectedByNvapi()
    {
        Assert.Equal(64, Marshal.SizeOf<LenovoRtxGpuLightingController.NvI2cInfoV3>());
        Assert.Equal(
            16,
            Marshal.OffsetOf<LenovoRtxGpuLightingController.NvI2cInfoV3>(
                nameof(LenovoRtxGpuLightingController.NvI2cInfoV3.I2cRegisterAddress)).ToInt32());
        Assert.Equal(
            32,
            Marshal.OffsetOf<LenovoRtxGpuLightingController.NvI2cInfoV3>(
                nameof(LenovoRtxGpuLightingController.NvI2cInfoV3.Data)).ToInt32());
        Assert.Equal(
            56,
            Marshal.OffsetOf<LenovoRtxGpuLightingController.NvI2cInfoV3>(
                nameof(LenovoRtxGpuLightingController.NvI2cInfoV3.IsPortIdSet)).ToInt32());
    }

    [Fact]
    public void CreateI2cInfo_RoutesRequestsToTheInternalRgbPort()
    {
        var info = LenovoRtxGpuLightingController.CreateI2cInfo(0x1111, 0x2222);

        Assert.Equal((byte)0, info.IsDdcPort);
        Assert.Equal((byte)0xB6, info.I2cDeviceAddress);
        Assert.Equal((byte)1, info.PortId);
        Assert.Equal(1u, info.IsPortIdSet);
        Assert.Equal((nint)0x1111, info.I2cRegisterAddress);
        Assert.Equal((nint)0x2222, info.Data);
    }

    [Fact]
    public void BuildStaticCommands_ProgramsPurpleInBgrRegisterOrder()
    {
        var commands = LenovoRtxGpuLightingController.BuildStaticCommands(
            red: 145,
            green: 85,
            blue: 255,
            brightness: 6,
            enabled: true);

        Assert.Contains(commands, command => command.Register == 0x17 && command.Value == 6);
        Assert.Contains(commands, command => command.Register == 0x18 && command.Value == 255);
        Assert.Contains(commands, command => command.Register == 0x19 && command.Value == 85);
        Assert.Contains(commands, command => command.Register == 0x1A && command.Value == 145);
        Assert.Contains(commands, command => command.Register == 0x14 && command.Value == 1);
        Assert.Equal(4, commands.Count(command => command.Register == 0x50 && command.Value == 1));
        Assert.Equal(37, commands.Count);
    }

    [Fact]
    public void BuildStaticCommands_WhenDisabled_ClosesEveryGpuOutput()
    {
        var commands = LenovoRtxGpuLightingController.BuildStaticCommands(
            red: 145,
            green: 85,
            blue: 255,
            brightness: 6,
            enabled: false);

        Assert.DoesNotContain(commands, command => command.Register == 0x14);
        Assert.Equal(4, commands.Count(command => command.Register == 0x50 && command.Value == 0));
        Assert.Equal(33, commands.Count);
    }
}
