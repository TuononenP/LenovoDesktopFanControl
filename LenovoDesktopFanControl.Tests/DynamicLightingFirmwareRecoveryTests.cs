using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class DynamicLightingFirmwareRecoveryTests
{
    [Theory]
    [InlineData(0x17EF, 0xC955, 0, true)]
    [InlineData(0x17EF, 0xC955, 1, false)]
    [InlineData(0x17EF, 0x0001, 0, false)]
    [InlineData(0x0001, 0xC955, 0, false)]
    public void ShouldAttempt_OnlyMatchesTheZeroLampLenovoController(
        int vendorId,
        int productId,
        int lampCount,
        bool expected)
    {
        var actual = DynamicLightingFirmwareRecovery.ShouldAttempt(
            (ushort)vendorId,
            (ushort)productId,
            lampCount);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TryEnableAsync_WhenFirmwareEnablesLighting_ReturnsTrue()
    {
        var firmware = new FakeFirmwareService(DynamicLightingFirmwareRecoveryResult.Enabled);
        var recovery = new DynamicLightingFirmwareRecovery(firmware);

        var enabled = await recovery.TryEnableAsync(0x17EF, 0xC955, 0);

        Assert.True(enabled);
        Assert.Equal(1, firmware.CallCount);
    }

    [Theory]
    [InlineData((int)DynamicLightingFirmwareRecoveryResult.Unavailable)]
    [InlineData((int)DynamicLightingFirmwareRecoveryResult.Unsupported)]
    [InlineData((int)DynamicLightingFirmwareRecoveryResult.AlreadyEnabled)]
    [InlineData((int)DynamicLightingFirmwareRecoveryResult.Failed)]
    public async Task TryEnableAsync_WhenFirmwareDoesNotChangeState_ReturnsFalse(
        int result)
    {
        var firmware = new FakeFirmwareService((DynamicLightingFirmwareRecoveryResult)result);
        var recovery = new DynamicLightingFirmwareRecovery(firmware);

        var enabled = await recovery.TryEnableAsync(0x17EF, 0xC955, 0);

        Assert.False(enabled);
        Assert.Equal(1, firmware.CallCount);
    }

    [Fact]
    public async Task TryEnableAsync_WhenControllerAlreadyHasLamps_DoesNotCallFirmware()
    {
        var firmware = new FakeFirmwareService(DynamicLightingFirmwareRecoveryResult.Enabled);
        var recovery = new DynamicLightingFirmwareRecovery(firmware);

        var enabled = await recovery.TryEnableAsync(0x17EF, 0xC955, 4);

        Assert.False(enabled);
        Assert.Equal(0, firmware.CallCount);
    }

    private sealed class FakeFirmwareService(
        DynamicLightingFirmwareRecoveryResult result) : IDynamicLightingFirmwareService
    {
        public int CallCount { get; private set; }

        public Task<DynamicLightingFirmwareRecoveryResult> EnsureEnabledAsync()
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
