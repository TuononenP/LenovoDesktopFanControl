using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public class LightingViewModelTests
{
    [Fact]
    public async Task InitializeAsync_ExposesDetectedControllerDetails()
    {
        var service = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo("Tower", "id", 42, 0x17EF, 0xC955)
        };
        using var viewModel = new LightingViewModel(service);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsAvailable);
        Assert.Contains("42 addressable lights", viewModel.DeviceSummary);
        Assert.Contains("17EF", viewModel.DeviceSummary);
        Assert.Contains("C955", viewModel.DeviceSummary);
    }

    [Fact]
    public async Task ApplyAsync_SendsBrightnessColorAndPowerState()
    {
        var service = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo("Tower", "id", 10, 0x17EF, 0xC955)
        };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 65;
        viewModel.SelectedColor = viewModel.Colors.Single(color => color.Name == "Cyan");
        viewModel.IsEnabled = true;

        await viewModel.ApplyAsync();

        Assert.Equal(0.65, Assert.Single(service.BrightnessCalls), 3);
        Assert.Equal((0, 211, 254), Assert.Single(service.ColorCalls));
        Assert.True(Assert.Single(service.EnabledCalls));
    }

    [Fact]
    public async Task InitializeAsync_LeavesControlsUnavailableWhenNoDeviceExists()
    {
        using var viewModel = new LightingViewModel(new FakeLightingControlService());

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsAvailable);
        Assert.Contains("not detected", viewModel.Status);
    }
}
