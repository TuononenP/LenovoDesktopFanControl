using System.IO.Pipes;
using System.Text.Json;
using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class LightingHostProtocolTests
{
    [Fact]
    public async Task InitializeAsync_AppliesSavedZoneStateThroughTheHostController()
    {
        var lighting = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo(
                "Tower", "device", 3, 0x17EF, 0xC955,
                [new LightingZoneInfo(0, "Front", LightingZoneKind.Accent, 3, [0, 1, 2])])
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            LightingEnabled = false,
            LightingBrightness = 35,
            LightingZoneEnabled = new Dictionary<int, bool> { [0] = false },
            LightingZoneBrightness = new Dictionary<int, int> { [0] = 40 },
            LightingZoneColors = new Dictionary<int, LightingZoneColor>
            {
                [0] = new LightingZoneColor(0, 10, 20, 30)
            }
        });

        using var controller = new LightingHostController(lighting, settings, TimeSpan.Zero);
        await controller.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([0.35], lighting.BrightnessCalls);
        Assert.Equal([(0, false)], lighting.ZoneEnabledCalls);
        Assert.Equal([(0, 0.4)], lighting.ZoneBrightnessCalls);
        Assert.Equal([(0, (byte)10, (byte)20, (byte)30)], lighting.ZoneColorCalls);
        Assert.Equal([false], lighting.EnabledCalls);
    }

    [Fact]
    public async Task DiscoverAsync_AppliesSavedStateWhenTheControllerAppearsAfterHostStartup()
    {
        var lighting = new FakeLightingControlService();
        var settings = new InMemorySettingsService(new FanSettings
        {
            LightingBrightness = 35,
            LightingEnabled = false
        });
        using var controller = new LightingHostController(lighting, settings, TimeSpan.Zero);

        await controller.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Empty(lighting.BrightnessCalls);

        lighting.Device = new LightingDeviceInfo("Tower", "device", 1, 0x17EF, 0xC955, []);
        var response = await controller.HandleAsync(
            new LightingHostRequest(LightingHostProtocol.Discover),
            TestContext.Current.CancellationToken);

        Assert.True(response.Succeeded);
        Assert.Equal("Tower", response.Device!.Name);
        Assert.Equal([0.35], lighting.BrightnessCalls);
        Assert.Equal([false], lighting.EnabledCalls);
    }

    [Fact]
    public async Task Server_ContinuesAfterAClientDoesNotTerminateItsRequest()
    {
        var pipeName = $"LenovoDesktopFanControl.Tests.{Guid.NewGuid():N}";
        var lighting = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo("Tower", "device", 1, 0x17EF, 0xC955, [])
        };
        using var controller = new LightingHostController(lighting, new InMemorySettingsService());
        using var cancellation = new CancellationTokenSource();
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        operationCancellation.CancelAfter(TimeSpan.FromSeconds(2));
        var server = new LightingHostServer(controller, pipeName, TimeSpan.FromMilliseconds(100));
        var serverTask = server.RunAsync(cancellation.Token);

        try
        {
            using var stalledClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stalledClient.ConnectAsync(operationCancellation.Token);
            await using var stalledWriter = new StreamWriter(stalledClient, leaveOpen: true) { AutoFlush = true };
            await stalledWriter.WriteAsync("{\"operation\":\"discover\"");

            using var validClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await validClient.ConnectAsync(operationCancellation.Token);
            await using var validWriter = new StreamWriter(validClient, leaveOpen: true) { AutoFlush = true };
            using var validReader = new StreamReader(validClient, leaveOpen: true);
            await validWriter.WriteLineAsync(JsonSerializer.Serialize(
                new LightingHostRequest(LightingHostProtocol.Discover),
                LightingHostProtocol.JsonOptions));

            var responseLine = await validReader.ReadLineAsync(operationCancellation.Token);
            var response = JsonSerializer.Deserialize<LightingHostResponse>(
                responseLine!, LightingHostProtocol.JsonOptions);

            Assert.NotNull(response);
            Assert.True(response!.Succeeded);
            Assert.Equal("Tower", response.Device!.Name);
        }
        finally
        {
            cancellation.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }
}
