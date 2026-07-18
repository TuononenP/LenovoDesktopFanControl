using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public sealed class SystemTemperatureFallbackTests
{
    [Fact]
    public async Task RefreshAsync_UsesSharedFirmwareSensorWhenMotherboardSensorIsUnavailable()
    {
        var fanService = new FakeFanControlService
        {
            DiscoveredFans =
            [
                new FanInfo { FanId = 3, TelemetryId = 0x10, SensorId = 2, Temperature = 33 },
                new FanInfo { FanId = 4, TelemetryId = 0x400, SensorId = 2, Temperature = 33 },
                new FanInfo { FanId = 5, TelemetryId = 0x80, SensorId = 2, Temperature = 33 }
            ]
        };
        fanService.FanSpeeds[0x10] = 500;
        fanService.FanSpeeds[0x400] = 500;
        fanService.FanSpeeds[0x80] = 500;
        fanService.Temperatures[2] = 34;
        var systemTemperatures = new FakeSystemTemperatureService(
        [
            new SystemTemperatureReading("GPU", 40, "GPU"),
            new SystemTemperatureReading("CPU", 45, "CPU"),
            new SystemTemperatureReading("SSD", 35, "SSD"),
            new SystemTemperatureReading("Motherboard", null, "No sensor")
        ]);
        var viewModel = new MainViewModel(
            fanService,
            new InMemorySettingsService(),
            new FakeAutoStartService(),
            systemTemperatureService: systemTemperatures);

        try
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshAsync();

            var motherboard = viewModel.SystemTemperatures.Single(item => item.Name == "Motherboard");
            Assert.Equal(34, motherboard.Temperature);
            Assert.Equal("Lenovo firmware shared system sensor (ID 2)", motherboard.Detail);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RefreshAsync_PrefersNamedMotherboardSensorOverFirmwareFallback()
    {
        var fanService = new FakeFanControlService
        {
            DiscoveredFans =
            [
                new FanInfo { FanId = 3, TelemetryId = 0x10, SensorId = 2, Temperature = 33 },
                new FanInfo { FanId = 4, TelemetryId = 0x400, SensorId = 2, Temperature = 33 }
            ]
        };
        fanService.FanSpeeds[0x10] = 500;
        fanService.FanSpeeds[0x400] = 500;
        fanService.Temperatures[2] = 34;
        var systemTemperatures = new FakeSystemTemperatureService(
        [
            new SystemTemperatureReading("GPU", null, ""),
            new SystemTemperatureReading("CPU", null, ""),
            new SystemTemperatureReading("SSD", null, ""),
            new SystemTemperatureReading("Motherboard", 47, "Mainboard / System")
        ]);
        var viewModel = new MainViewModel(
            fanService,
            new InMemorySettingsService(),
            new FakeAutoStartService(),
            systemTemperatureService: systemTemperatures);

        try
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshAsync();

            var motherboard = viewModel.SystemTemperatures.Single(item => item.Name == "Motherboard");
            Assert.Equal(47, motherboard.Temperature);
            Assert.Equal("Mainboard / System", motherboard.Detail);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }
}
