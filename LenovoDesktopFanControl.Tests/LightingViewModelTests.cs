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

    private static LightingDeviceInfo GpuZoneDevice() =>
        new("Tower", "id", 10, 0x17EF, 0xC955,
        [
            new LightingZoneInfo(
                0,
                "GPU",
                LightingZoneKind.GraphicsCard,
                0,
                [])
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
        Assert.Empty(viewModel.Status);
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
    public async Task ZoneName_CanBeCustomizedAndClearedToRestoreDefault()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var settingsChangedCount = 0;
        viewModel.SettingsChanged += (_, _) => settingsChangedCount++;

        var zone = viewModel.Zones[0];
        zone.Name = "  Desk glow  ";

        Assert.Equal("Desk glow", zone.Name);
        Assert.Equal("Legion Banner", zone.DefaultName);
        Assert.Equal(1, settingsChangedCount);

        zone.Name = "";

        Assert.Equal("Legion Banner", zone.Name);
        Assert.Equal(2, settingsChangedCount);
    }

    [Fact]
    public async Task EditNameCommand_ActivatesInlineZoneNameEditor()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var zone = viewModel.Zones[0];

        zone.EditNameCommand.Execute(null);

        Assert.True(zone.IsEditingName);
    }

    [Fact]
    public async Task RestoreZoneNames_DoesNotRaiseSettingsChanged()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var settingsChangedCount = 0;
        viewModel.SettingsChanged += (_, _) => settingsChangedCount++;

        viewModel.RestoreZoneNames(new Dictionary<int, string>
        {
            [0] = "Desk glow",
            [1] = "Wall wash"
        });

        Assert.Equal(["Desk glow", "Wall wash"], viewModel.Zones.Select(zone => zone.Name));
        Assert.Equal(0, settingsChangedCount);
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
    public async Task Brightness_DebouncesControllerUpdateAndRaisesApplied()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var appliedCount = 0;
        viewModel.Applied += (_, _) => appliedCount++;

        viewModel.Brightness = 25;
        viewModel.Brightness = 35;

        Assert.Empty(service.BrightnessCalls);
        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.Equal(0.35, Assert.Single(service.BrightnessCalls), 3);
        Assert.Equal(1, appliedCount);
        Assert.Equal("Brightness set to 35%", viewModel.Status);
    }

    [Fact]
    public async Task ApplyAsync_SendsBrightnessColorAndPowerStateForSingleZone()
    {
        var service = new FakeLightingControlService { Device = SingleZoneDevice(10) };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Brightness = 65;
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Cyan");
        service.ZoneColorCalls.Clear();
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
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Red");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(c => c.Name == "Cyan");
        service.ZoneColorCalls.Clear();
        viewModel.IsEnabled = true;

        await viewModel.ApplyAsync();

        Assert.Equal(0.8, Assert.Single(service.BrightnessCalls), 3);
        Assert.Equal([(0, 1d), (1, 1d)], service.ZoneBrightnessCalls);
        Assert.Empty(service.ColorCalls);
        Assert.Equal(2, service.ZoneColorCalls.Count);
        Assert.Equal((0, 255, 55, 75), service.ZoneColorCalls[0]);
        Assert.Equal((1, 0, 211, 254), service.ZoneColorCalls[1]);
        Assert.True(Assert.Single(service.EnabledCalls));
    }

    [Fact]
    public async Task ToggleZoneCommand_DisablesOnlySelectedZone()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var appliedCount = 0;
        viewModel.Applied += (_, _) => appliedCount++;

        var zone = viewModel.Zones[1];
        zone.IsEnabled = false;
        viewModel.ToggleZoneCommand.Execute(zone);

        Assert.True(viewModel.Zones[0].IsEnabled);
        Assert.False(zone.IsEnabled);
        Assert.Equal((1, false), Assert.Single(service.ZoneEnabledCalls));
        Assert.Empty(service.EnabledCalls);
        Assert.Equal(1, appliedCount);
        Assert.Equal("Accent Lights disabled", viewModel.Status);
    }

    [Fact]
    public async Task ZoneBrightness_DebouncesUpdateForOnlyTheSelectedZone()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var appliedCount = 0;
        viewModel.Applied += (_, _) => appliedCount++;

        viewModel.Zones[1].Brightness = 25;
        viewModel.Zones[1].Brightness = 35;

        Assert.Equal(100, viewModel.Zones[0].Brightness);
        Assert.Equal(35, viewModel.Zones[1].Brightness);
        Assert.Empty(service.ZoneBrightnessCalls);
        await Task.Delay(250, TestContext.Current.CancellationToken);
        var call = Assert.Single(service.ZoneBrightnessCalls);
        Assert.Equal(1, call.ZoneIndex);
        Assert.Equal(0.35, call.Brightness, 3);
        Assert.Equal(1, appliedCount);
        Assert.Equal("Accent Lights brightness set to 35%", viewModel.Status);
    }

    [Fact]
    public async Task ZoneColor_ImmediatelyUpdatesOnlySelectedZone()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var appliedCount = 0;
        viewModel.Applied += (_, _) => appliedCount++;

        viewModel.Zones[1].SelectedColor =
            viewModel.Colors.Single(color => color.Name == "Cyan");

        Assert.Equal((1, 0, 211, 254), Assert.Single(service.ZoneColorCalls));
        Assert.Equal(1, appliedCount);
        Assert.Equal("Accent Lights color set to Cyan", viewModel.Status);
        Assert.Null(viewModel.GlobalColor);
    }

    [Fact]
    public async Task PickGlobalColorCommand_AddsCustomColorAndAppliesItToEveryZone()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        var picker = new FakeLightingColorPicker(new LightingColorOption("", 17, 34, 51));
        using var viewModel = new LightingViewModel(service, picker);
        await viewModel.InitializeAsync();
        service.ZoneColorCalls.Clear();

        viewModel.PickGlobalColorCommand.Execute(null);

        var color = Assert.Single(viewModel.Colors, color =>
            color.Red == 17 && color.Green == 34 && color.Blue == 51);
        Assert.Same(color, viewModel.GlobalColor);
        Assert.All(viewModel.Zones, zone => Assert.Same(color, zone.SelectedColor));
        Assert.Equal([(0, 17, 34, 51), (1, 17, 34, 51)], service.ZoneColorCalls);
        Assert.Equal("Legion Blue", Assert.Single(picker.InitialColors).Name);
    }

    [Fact]
    public async Task PickZoneColorCommand_OnlyChangesTheRequestedZone()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        var picker = new FakeLightingColorPicker(new LightingColorOption("", 90, 80, 70));
        using var viewModel = new LightingViewModel(service, picker);
        await viewModel.InitializeAsync();
        service.ZoneColorCalls.Clear();

        viewModel.PickZoneColorCommand.Execute(viewModel.Zones[1]);

        var selected = viewModel.Zones[1].SelectedColor;
        Assert.NotNull(selected);
        Assert.Equal((byte)90, selected.Red);
        Assert.Equal((byte)80, selected.Green);
        Assert.Equal((byte)70, selected.Blue);
        Assert.Same(viewModel.Colors[0], viewModel.Zones[0].SelectedColor);
        Assert.Equal((1, 90, 80, 70), Assert.Single(service.ZoneColorCalls));
    }

    [Fact]
    public async Task PickZoneColorCommand_PreviewsChangesAndRestoresTheOriginalColorOnCancel()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        var picker = new FakeLightingColorPicker((LightingColorOption?)null);
        picker.PreviewColors.Add(new LightingColorOption("", 90, 80, 70));
        using var viewModel = new LightingViewModel(service, picker);
        await viewModel.InitializeAsync();
        var originalColor = viewModel.Zones[1].SelectedColor;
        service.ZoneColorCalls.Clear();

        viewModel.PickZoneColorCommand.Execute(viewModel.Zones[1]);

        Assert.Same(originalColor, viewModel.Zones[1].SelectedColor);
        Assert.Equal([(1, 90, 80, 70), (1, 91, 157, 255)], service.ZoneColorCalls);
    }

    [Fact]
    public async Task PickZoneColorCommand_CommitsWithoutWaitingForThePreviewToFinish()
    {
        var service = new FakeLightingControlService
        {
            Device = MultiZoneDevice(),
            ZoneColorGate = new TaskCompletionSource<object?>()
        };
        var picker = new FakeLightingColorPicker(new LightingColorOption("", 17, 34, 51));
        picker.PreviewColors.Add(new LightingColorOption("", 90, 80, 70));
        using var viewModel = new LightingViewModel(service, picker);
        await viewModel.InitializeAsync();

        viewModel.PickZoneColorCommand.Execute(viewModel.Zones[1]);

        var selected = viewModel.Zones[1].SelectedColor;
        Assert.NotNull(selected);
        Assert.Equal((byte)17, selected.Red);
        Assert.Equal((byte)34, selected.Green);
        Assert.Equal((byte)51, selected.Blue);
        service.ZoneColorGate.SetResult(null);
    }

    [Fact]
    public async Task PickGlobalColorCommand_RestoresEveryOriginalZoneColorOnCancel()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        var picker = new FakeLightingColorPicker((LightingColorOption?)null);
        picker.PreviewColors.Add(new LightingColorOption("", 90, 80, 70));
        using var viewModel = new LightingViewModel(service, picker);
        await viewModel.InitializeAsync();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(color => color.Name == "Red");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(color => color.Name == "Cyan");
        service.ZoneColorCalls.Clear();

        viewModel.PickGlobalColorCommand.Execute(null);

        Assert.Equal("Red", viewModel.Zones[0].SelectedColor?.Name);
        Assert.Equal("Cyan", viewModel.Zones[1].SelectedColor?.Name);
        Assert.Equal(
            [(0, 90, 80, 70), (1, 90, 80, 70), (0, 255, 55, 75), (1, 0, 211, 254)],
            service.ZoneColorCalls);
    }

    [Fact]
    public void FindOrAddColor_ReusesMatchingColorAndNamesNewCustomColor()
    {
        using var viewModel = new LightingViewModel(service: null);

        var preset = viewModel.FindOrAddColor(0, 211, 254);
        var custom = viewModel.FindOrAddColor(17, 34, 51);
        var duplicate = viewModel.FindOrAddColor(17, 34, 51);

        Assert.Equal("Cyan", preset.Name);
        Assert.Equal("Custom (#112233)", custom.Name);
        Assert.Same(custom, duplicate);
    }

    [Fact]
    public async Task MatchingZoneColors_RestoreGlobalColorSelection()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var cyan = viewModel.Colors.Single(color => color.Name == "Cyan");

        viewModel.Zones[0].SelectedColor = cyan;
        Assert.Null(viewModel.GlobalColor);

        viewModel.Zones[1].SelectedColor = cyan;
        Assert.Same(cyan, viewModel.GlobalColor);
    }

    [Fact]
    public async Task GpuColorChange_DebouncesAndDisablesSelectionUntilWriteCompletes()
    {
        var service = new FakeLightingControlService
        {
            Device = GpuZoneDevice(),
            ZoneColorGate = new TaskCompletionSource<object?>()
        };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        var gpu = Assert.Single(viewModel.Zones);

        gpu.SelectedColor = viewModel.Colors.Single(color => color.Name == "Cyan");

        Assert.True(gpu.IsColorApplying);
        Assert.False(gpu.CanChangeColor);
        Assert.Empty(service.ZoneColorCalls);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Single(service.ZoneColorCalls);
        Assert.True(gpu.IsColorApplying);

        service.ZoneColorGate.SetResult(null);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.False(gpu.IsColorApplying);
        Assert.True(gpu.CanChangeColor);
    }

    [Fact]
    public async Task ApplyAsync_RestoresEnabledStateForEveryZone()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Zones[1].IsEnabled = false;

        await viewModel.ApplyAsync();

        Assert.Equal([(0, true), (1, false)], service.ZoneEnabledCalls);
        Assert.Equal(2, service.ZoneColorCalls.Count);
        Assert.True(Assert.Single(service.EnabledCalls));
    }

    [Fact]
    public async Task GlobalColorChange_PreservesIndividualZonePowerState()
    {
        var service = new FakeLightingControlService { Device = MultiZoneDevice() };
        using var viewModel = new LightingViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.Zones[1].IsEnabled = false;

        viewModel.GlobalColor = viewModel.Colors.Single(color => color.Name == "Red");

        Assert.All(viewModel.Zones, zone => Assert.Equal("Red", zone.SelectedColor?.Name));
        Assert.False(viewModel.Zones[1].IsEnabled);
        Assert.Empty(service.ColorCalls);
        Assert.Equal(2, service.ZoneColorCalls.Count);
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
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Green");
        service.ZoneColorCalls.Clear();
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
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Purple");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(c => c.Name == "Orange");
        service.ZoneColorCalls.Clear();
        viewModel.IsEnabled = true;

        await viewModel.ReapplyAsync();

        Assert.Equal(0.7, Assert.Single(service.BrightnessCalls), 3);
        Assert.Equal([(0, 1d), (1, 1d)], service.ZoneBrightnessCalls);
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
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Red");
        viewModel.Zones[1].SelectedColor = viewModel.Colors.Single(c => c.Name == "Cyan");
        service.ZoneColorCalls.Clear();
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
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Green");
        viewModel.IsEnabled = true;

        service.BrightnessGate = new TaskCompletionSource<object?>();
        var applyTask = viewModel.ApplyAsync();
        Assert.True(viewModel.IsBusy);

        service.RaiseAvailabilityChanged();
        await Task.Yield();

        Assert.Single(service.EnabledCalls);
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
        service.BrightnessCalls.Clear();
        viewModel.Zones[0].SelectedColor = viewModel.Colors.Single(c => c.Name == "Purple");
        service.ZoneColorCalls.Clear();
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
