using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class PipeLightingControlServiceTests
{
    [Fact]
    public async Task DiscoverAsync_RestartsHostAfterInitialConnectionFailure()
    {
        var pipeName = $"LenovoDesktopFanControl.Tests.{Guid.NewGuid():N}";
        var lighting = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo("Tower", "device", 1, 0x17EF, 0xC955, [])
        };
        using var controller = new LightingHostController(
            lighting,
            new InMemorySettingsService(),
            TimeSpan.Zero);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var server = new LightingHostServer(controller, pipeName);
        Task? serverTask = null;
        var restartCount = 0;

        using var service = new PipeLightingControlService(
            () =>
            {
                restartCount++;
                serverTask = server.RunAsync(cancellation.Token);
                return true;
            },
            pipeName,
            connectAttempts: 5,
            connectRetryDelay: TimeSpan.FromMilliseconds(20),
            connectTimeout: TimeSpan.FromMilliseconds(20),
            responseTimeout: TimeSpan.FromSeconds(2));

        try
        {
            var device = await service.DiscoverAsync();

            Assert.NotNull(device);
            Assert.Equal("Tower", device.Name);
            Assert.Equal(1, restartCount);
            Assert.True(service.IsControlAvailable);
        }
        finally
        {
            cancellation.Cancel();
            if (serverTask != null)
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }
}
