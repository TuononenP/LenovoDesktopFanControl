using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public class LightingViewModelTests
{
    private static LightingDeviceInfo SingleZoneDevice(int lampCount = 42) =>
        new("Tower", "id", lampCount, 0x17EF, 0xC955,
        [new LightingZoneInfo(0, "Legion Banner", LightingZoneKind.Branding, lampCount, new[] { 0 })]);

    private static LightingDeviceInfo MultiZoneDevice() =>
        new("Tower", "id", 10, 0x17EF, 0xC955,
        [
            new LightingZoneInfo(0, "Legion Banner", LightingZoneKind.Branding, 4, new[] { 0, 1, 2, 3 }),
            new LightingZoneInfo(1, "Accent Lights", LightingZoneKind.Accent, 6, new[] { 4, 5, 6, 7, 8, 9 })
        ]);

    [Fact]
    public async Task InitializeAsync_ExposesDetectedControllerDetails()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(42) };
        using var viewModel = new LightingViewModel(service);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsAvailable);
        Assert.Contains("42 addressable lights", viewModel.DeviceSummary);
        Assert.Contains("17EF", viewModel.DeviceSummary);
        Assert.Contains("C955", viewModel.DeviceSummary);
    }

    [Fact]
    public async Task InitializeAsync_PopulatesZonesFromDevice()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasZones);
        Assert.Equal(2, viewModel.Zones.Count);
        Assert.Equal("Legion Banner", viewModel.Zones[0].Name);
        Assert.Equal("Accent Lights", viewModel.Zones[1].Name);
    }

    [Fact]
    public async Task HasZones_IsTrueForSingleZoneDevice()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(5) };
        using var viewModel = new LightingViewModel(service);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasZones);
        Assert.Single(viewModel.Zones);
    }

    [Fact]
    public async Task ApplyAsync_SendsBrightnessColorAndPowerStateForSingleZone()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 65;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Cyan");
        viewModel.IsEnabled = true;

        await viewModel.ApplyAsync();

        Assert.Equal(0.65, Assert.Single(service.BrightnessCalls), 3);
        Assert.Empty(service.ColorCalls);
        Assert.Equal((0, 0, 211, 254), Assert.Single(service.ZoneColorCalls));
        Assert.True(Assert.Single(service.EnabledCalls));
    }

    [Fact]
    public async Task ApplyAsync_SendsPerZoneColorsWhenMultipleZonesExist()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 80;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Red");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(c => c.Name == "Cyan");
        viewModel.IsEnabled = true;

        await viewModel.ApplyAsync();

        Assert.Equal(0.8, Assert.Single(service.BrightnessCalls), 3);
        Assert.Empty(service.ColorCalls);
        Assert.Equal(2, service.ZoneColorCalls.Count);
        Assert.Equal((0, 255, 55, 75), service.ZoneColorCalls[0]);
        Assert.Equal((1, 0, 211, 254), service.ZoneColorCalls[1]);
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

    [Fact]
    public async Task ReapplyAsync_DoesNothingWhenNotAvailable()
    {
        var service = new FakeLightingControlService();
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ReapplyAsync();

        Assert.Empty(service.BrightnessCalls);
        Assert.Empty(service.ColorCalls);
        Assert.Empty(service.ZoneColorCalls);
        Assert.Empty(service.EnabledCalls);
    }

    [Fact]
    public async Task ReapplyAsync_RestoresSingleZoneColorAndBrightness()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 45;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Green");
        viewModel.IsEnabled = true;

        await viewModel.ReapplyAsync();

        Assert.Equal(0.45, Assert.Single(service.BrightnessCalls), 3);
        Assert.Empty(service.ColorCalls);
        Assert.Equal((0, 55, 220, 125), Assert.Single(service.ZoneColorCalls));
        Assert.True(Assert.Single(service.EnabledCalls));
    }

    [Fact]
    public async Task ReapplyAsync_RestoresPerZoneColorsAndBrightness()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 70;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Purple");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(c => c.Name == "Orange");
        viewModel.IsEnabled = true;

        await viewModel.ReapplyAsync();

        Assert.Equal(0.7, Assert.Single(service.BrightnessCalls), 3);
        Assert.Empty(service.ColorCalls);
        Assert.Equal(2, service.ZoneColorCalls.Count);
        Assert.Equal((0, 145, 85, 255), service.ZoneColorCalls[0]);
        Assert.Equal((1, 255, 135, 40), service.ZoneColorCalls[1]);
        Assert.True(Assert.Single(service.EnabledCalls));
    }

    [Fact]
    public async Task ReapplyAsync_DoesNotRaiseAppliedEvent()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Zones[0].SelectedColor = viewModel.Colors[0];
        viewModel.IsEnabled = true;
        var appliedRaised = false;
        viewModel.Applied += (_, _) => appliedRaised = true;

        await viewModel.ReapplyAsync();

        Assert.False(appliedRaised);
    }

    [Fact]
    public async Task ReapplyAsync_CanBeCalledMultipleTimesWithoutStatusChanges()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 50;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Red");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(c => c.Name == "Cyan");
        viewModel.IsEnabled = true;
        var statusBefore = viewModel.Status;

        await viewModel.ReapplyAsync();
        await viewModel.ReapplyAsync();

        Assert.Equal(2, service.BrightnessCalls.Count);
        Assert.Equal(4, service.ZoneColorCalls.Count);
        Assert.Equal(2, service.EnabledCalls.Count);
        Assert.Equal(statusBefore, viewModel.Status);
    }

    [Fact]
    public async Task AvailabilityChanged_WhileBusy_DefersReapplyUntilBusyEnds()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 60;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Green");
        viewModel.IsEnabled = true;

        service.BrightnessGate = new TaskCompletionSource<object?>();
        var applyTask = viewModel.ApplyAsync();
        Assert.True(viewModel.IsBusy);

        service.RaiseAvailabilityChanged();
        await Task.Yield();

        Assert.Equal(1, service.EnabledCalls.Count);
        service.BrightnessGate.SetResult(null);
        await applyTask;

        Assert.False(viewModel.IsBusy);
        Assert.Equal(2, service.EnabledCalls.Count);
        Assert.Equal(2, service.BrightnessCalls.Count);
        Assert.Equal((0, 55, 220, 125), service.ZoneColorCalls[^1]);
    }

    [Fact]
    public async Task AvailabilityChanged_WhenNotAvailable_DoesNotDefer()
    {
        var service = new FakeLightingControlService();
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();

        service.RaiseAvailabilityChanged();

        Assert.Empty(service.BrightnessCalls);
        Assert.Empty(service.ZoneColorCalls);
    }

    [Fact]
    public async Task ReapplyAsync_WhileBusy_DefersAndDrainsAfterApplyCompletes()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 70;
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Purple");
        viewModel.IsEnabled = true;

        service.BrightnessGate = new TaskCompletionSource<object?>();
        var applyTask = viewModel.ApplyAsync();
        Assert.True(viewModel.IsBusy);

        await viewModel.ReapplyAsync();
        await Task.Yield();

        service.BrightnessGate.SetResult(null);
        await applyTask;

        Assert.False(viewModel.IsBusy);
        Assert.Equal(2, service.BrightnessCalls.Count);
        Assert.Equal(2, service.ZoneColorCalls.Count);
        Assert.Equal(2, service.EnabledCalls.Count);
    }
}