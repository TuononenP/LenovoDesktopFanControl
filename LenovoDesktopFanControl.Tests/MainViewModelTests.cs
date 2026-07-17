using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public class MainViewModelTests
{
    [Fact]
    public async Task InitializeAsync_ReportsUnsupportedHardwareWithoutDiscoveringFans()
    {
        var service = new FakeFanControlService { IsSupported = false };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();

            Assert.False(viewModel.IsSupported);
            Assert.Empty(viewModel.Fans);
            Assert.Equal(ApplicationStatusKind.Unsupported, viewModel.StatusKind);
            Assert.False(viewModel.IsBusy);
            Assert.Equal(1, settings.LoadCount);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_PopulatesStateFansAndStoredCurve()
    {
        byte[] curve = [4, 4, 4, 5, 5, 6, 6, 7, 8, 9];
        var service = new FakeFanControlService
        {
            SmartFanMode = SmartFanMode.Quiet,
            FullSpeed = true,
            IsFullSpeedSupported = false,
            DiscoveredFans =
            [
                new FanInfo
                {
                    FanId = 7,
                    SensorId = 3,
                    Name = "CPU fan",
                    CurrentRpm = 1200,
                    Temperature = 41,
                    MaxRpm = 2400,
                    IsAvailable = true
                }
            ]
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            MinimizeToTray = true,
            FanCurves = { [7] = curve }
        });
        var autoStart = new FakeAutoStartService { Enabled = true };
        var viewModel = new MainViewModel(service, settings, autoStart);
        try
        {
            await viewModel.InitializeAsync();

            var fan = Assert.Single(viewModel.Fans);
            Assert.True(viewModel.IsSupported);
            Assert.True(viewModel.StartWithWindows);
            Assert.True(viewModel.MinimizeToTray);
            Assert.Equal(SmartFanMode.Quiet, viewModel.SelectedFanMode);
            Assert.True(viewModel.IsFullSpeed);
            Assert.False(viewModel.IsFullSpeedSupported);
            Assert.Equal(40, fan.TargetSpeedPercentage);
            Assert.Equal(1200, fan.CurrentRpm);
            Assert.Equal(ApplicationStatusKind.Connected, viewModel.StatusKind);
            Assert.True(viewModel.HasFans);
            Assert.False(viewModel.ShowNoFans);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_ReappliesFirstStoredCurveInCustomMode()
    {
        byte[] curve = [2, 2, 3, 3, 4, 4, 5, 6, 7, 8];
        var service = new FakeFanControlService
        {
            SmartFanMode = SmartFanMode.Custom,
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 2, Name = "Fan" }]
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            FanCurves = { [1] = curve }
        });
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();

            Assert.Single(service.SetFanTableCalls);
            Assert.Equal(curve, service.SetFanTableCalls[0].Curve);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_RestoresSavedCustomModeAndGlobalCurve()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var service = new FakeFanControlService
        {
            SmartFanMode = SmartFanMode.Balanced,
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 2, Name = "Fan" }]
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            Mode = SmartFanMode.Custom,
            GlobalFanCurve = curve
        });
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();

            Assert.Equal([SmartFanMode.Custom], service.SetModeCalls);
            Assert.Equal(curve, Assert.Single(service.SetFanTableCalls).Curve);
            Assert.Equal(SmartFanMode.Custom, viewModel.SelectedFanMode);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_ReportsNoFansAndInitializationExceptions()
    {
        var noFansService = new FakeFanControlService();
        var noFansViewModel = new MainViewModel(
            noFansService, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            await noFansViewModel.InitializeAsync();
            Assert.True(noFansViewModel.ShowNoFans);
            Assert.Equal(ApplicationStatusKind.Warning, noFansViewModel.StatusKind);
        }
        finally
        {
            await noFansViewModel.ShutdownAsync();
        }

        var errorService = new FakeFanControlService
        {
            IsSupportedException = new InvalidOperationException("provider failed")
        };
        var errorViewModel = new MainViewModel(
            errorService, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            await errorViewModel.InitializeAsync();
            Assert.Equal(ApplicationStatusKind.Error, errorViewModel.StatusKind);
            Assert.Contains("provider failed", errorViewModel.StatusMessage);
            Assert.False(errorViewModel.IsBusy);
        }
        finally
        {
            await errorViewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RefreshAsync_UpdatesOnlyValidTelemetryAndPreservesLastKnownValues()
    {
        var service = new FakeFanControlService
        {
            DiscoveredFans =
            [
                new FanInfo
                {
                    FanId = 1,
                    TelemetryId = 16,
                    SensorId = 11,
                    CurrentRpm = 900,
                    Temperature = 35
                },
                new FanInfo { FanId = 2, SensorId = 22, CurrentRpm = 800, Temperature = 36 }
            ]
        };
        service.FanSpeeds[16] = 1300;
        service.FanSpeeds[2] = -1;
        service.Temperatures[11] = 48;
        service.Temperatures[22] = 126;
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshAsync();

            Assert.Equal(1300, viewModel.Fans[0].CurrentRpm);
            Assert.Equal(48, viewModel.Fans[0].Temperature);
            Assert.Equal(800, viewModel.Fans[1].CurrentRpm);
            Assert.Equal(36, viewModel.Fans[1].Temperature);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyModeAsync_PersistsModeAndReappliesCurveForCustomMode()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var service = new FakeFanControlService
        {
            DiscoveredFans = [new FanInfo { FanId = 3, SensorId = 4 }]
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            FanCurves = { [3] = curve }
        });
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            viewModel.SelectedFanMode = SmartFanMode.Custom;

            await viewModel.ApplyModeAsync();

            Assert.Equal([SmartFanMode.Custom], service.SetModeCalls);
            Assert.Equal(curve, Assert.Single(service.SetFanTableCalls).Curve);
            Assert.Equal(SmartFanMode.Custom, settings.Settings.Mode);
            Assert.True(viewModel.IsCustomMode);
            Assert.Equal(ApplicationStatusKind.Connected, viewModel.StatusKind);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyModeAsync_ReportsServiceFailureAndClearsBusyState()
    {
        var service = new FakeFanControlService
        {
            SetModeException = new InvalidOperationException("mode denied")
        };
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService())
        {
            SelectedFanMode = SmartFanMode.Quiet
        };
        try
        {
            await viewModel.ApplyModeAsync();

            Assert.Equal(ApplicationStatusKind.Error, viewModel.StatusKind);
            Assert.Contains("mode denied", viewModel.StatusMessage);
            Assert.False(viewModel.IsBusy);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ToggleFullSpeedAsync_UpdatesStateAndRollsBackOnFailure()
    {
        var service = new FakeFanControlService();
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            await viewModel.ToggleFullSpeedAsync(true);
            Assert.True(viewModel.IsFullSpeed);
            Assert.Equal(ApplicationStatusKind.Warning, viewModel.StatusKind);

            service.SetFullSpeedException = new InvalidOperationException("full speed denied");
            await viewModel.ToggleFullSpeedAsync(false);

            Assert.True(viewModel.IsFullSpeed);
            Assert.Equal(ApplicationStatusKind.Error, viewModel.StatusKind);
            Assert.Contains("full speed denied", viewModel.StatusMessage);
            Assert.False(viewModel.IsBusy);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ResetAsync_RestoresFirmwareUiAndStoredDefaults()
    {
        var service = new FakeFanControlService
        {
            FullSpeed = true,
            SmartFanMode = SmartFanMode.Custom,
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 1 }]
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            Mode = SmartFanMode.Custom,
            FanCurves = { [1] = [3, 3, 3, 3, 3, 3, 3, 3, 3, 3] }
        });
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            service.SetFanTableCalls.Clear();

            await viewModel.ResetAsync();

            Assert.False(service.SetFullSpeedCalls.Last());
            Assert.Equal(SmartFanMode.Balanced, service.SetModeCalls.Last());
            Assert.Empty(service.SetFanTableCalls);
            Assert.Equal(50, viewModel.Fans[0].TargetSpeedPercentage);
            Assert.Empty(settings.Settings.FanCurves);
            Assert.Null(settings.Settings.GlobalFanCurve);
            Assert.Equal(SmartFanMode.Balanced, settings.Settings.Mode);
            Assert.False(viewModel.IsFullSpeed);
            Assert.False(viewModel.IsCustomMode);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ResetAsync_SkipsUnsupportedFullSpeedFeature()
    {
        var service = new FakeFanControlService
        {
            IsFullSpeedSupported = false,
            DiscoveredFans = [new FanInfo { FanId = 1 }]
        };
        var viewModel = new MainViewModel(
            service,
            new InMemorySettingsService(),
            new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            await viewModel.ResetAsync();

            Assert.Empty(service.SetFullSpeedCalls);
            Assert.Equal(SmartFanMode.Balanced, service.SetModeCalls.Last());
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ToggleAutoStart_DisablesAndPersistsSetting()
    {
        var autoStart = new FakeAutoStartService { Enabled = true };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(new FakeFanControlService(), settings, autoStart)
        {
            StartWithWindows = false
        };
        try
        {
            viewModel.ToggleAutoStart();

            Assert.Equal(1, autoStart.DisableCount);
            Assert.False(settings.Settings.StartWithWindows);
            Assert.True(settings.SaveCount > 0);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ShutdownAsync_RestoresBalancedModeOnlyWhenCustomModeIsActive()
    {
        var service = new FakeFanControlService();
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService())
        {
            IsSupported = true,
            SelectedFanMode = SmartFanMode.Custom
        };

        await viewModel.ShutdownAsync();

        Assert.Equal([SmartFanMode.Balanced], service.SetModeCalls);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public async Task ShutdownAsync_PersistsCurrentLightingSettingsWithoutApplying()
    {
        var lightingService = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo(
                "Tower", "id", 1, 0x17EF, 0xC955,
                [new LightingZoneInfo(7, "Front", LightingZoneKind.Accent, 1, [0])])
        };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(
            new FakeFanControlService(), settings, new FakeAutoStartService(), lightingService);
        await viewModel.InitializeAsync();
        viewModel.Lighting.IsEnabled = false;
        viewModel.Lighting.Brightness = 37;
        viewModel.Lighting.Zones[0].SelectedColor =
            viewModel.Lighting.Colors.Single(color => color.Name == "Purple");

        await viewModel.ShutdownAsync();

        Assert.False(settings.Settings.LightingEnabled);
        Assert.Equal(37, settings.Settings.LightingBrightness);
        var savedColor = Assert.Single(settings.Settings.LightingZoneColors).Value;
        Assert.Equal(7, savedColor.ZoneIndex);
        Assert.Equal((byte)145, savedColor.Red);
        Assert.Equal((byte)85, savedColor.Green);
        Assert.Equal((byte)255, savedColor.Blue);
    }

    [Fact]
    public async Task InitializeAsync_RestoresAndAppliesSavedGlobalLightingState()
    {
        var lightingService = new FakeLightingControlService
        {
            Device = new LightingDeviceInfo(
                "Tower", "id", 2, 0x17EF, 0xC955,
                [
                    new LightingZoneInfo(0, "Rear", LightingZoneKind.Accent, 1, [0]),
                    new LightingZoneInfo(1, "Front", LightingZoneKind.Accent, 1, [1])
                ])
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            LightingEnabled = false,
            LightingBrightness = 42,
            LightingZoneColors =
            {
                [0] = new LightingZoneColor(0, 145, 85, 255),
                [1] = new LightingZoneColor(1, 145, 85, 255)
            }
        });
        var viewModel = new MainViewModel(
            new FakeFanControlService(), settings, new FakeAutoStartService(), lightingService);

        try
        {
            await viewModel.InitializeAsync();

            Assert.Equal("Purple", viewModel.Lighting.GlobalColor?.Name);
            Assert.All(viewModel.Lighting.Zones,
                zone => Assert.Equal("Purple", zone.SelectedColor?.Name));
            Assert.Equal(0.42, Assert.Single(lightingService.BrightnessCalls), 3);
            Assert.Empty(lightingService.ColorCalls);
            Assert.Equal(
                [(0, (byte)145, (byte)85, (byte)255), (1, (byte)145, (byte)85, (byte)255)],
                lightingService.ZoneColorCalls);
            Assert.False(Assert.Single(lightingService.EnabledCalls));
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task EffectiveStatusKind_IsBusyWhileOperationIsInProgress()
    {
        var viewModel = new MainViewModel(
            new FakeFanControlService(), new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            viewModel.UpdateStatus("ready", ApplicationStatusKind.Connected);
            viewModel.IsBusy = true;

            Assert.Equal(ApplicationStatusKind.Busy, viewModel.EffectiveStatusKind);
            viewModel.IsBusy = false;
            Assert.Equal(ApplicationStatusKind.Connected, viewModel.EffectiveStatusKind);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ChangeLanguage_PersistsSettingAndRefreshesLocalizableStatus()
    {
        var service = new FakeFanControlService
        {
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 2, Name = "Fan" }]
        };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            Assert.Equal("en", settings.Settings.Language);

            viewModel.SelectedLanguage = "fi-FI";

            Assert.Equal("fi-FI", settings.Settings.Language);
            Assert.True(settings.SaveCount > 0);
            Assert.Contains("1", viewModel.StatusMessage);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task MinimizeToTray_PersistsToSettingsWithoutDuplicates()
    {
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(
            new FakeFanControlService(), settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();

            viewModel.MinimizeToTray = true;
            Assert.True(settings.Settings.MinimizeToTray);

            viewModel.MinimizeToTray = true;
            Assert.True(settings.Settings.MinimizeToTray);

            viewModel.MinimizeToTray = false;
            Assert.False(settings.Settings.MinimizeToTray);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ToggleAutoStart_EnablesAutostartWithProcessPath()
    {
        var autoStart = new FakeAutoStartService { Enabled = false };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(new FakeFanControlService(), settings, autoStart)
        {
            StartWithWindows = true
        };
        try
        {
            viewModel.ToggleAutoStart();

            Assert.Equal(1, autoStart.EnabledPaths.Count);
            Assert.True(autoStart.Enabled);
            Assert.True(settings.Settings.StartWithWindows);
            Assert.True(settings.SaveCount > 0);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ShutdownAsync_DoesNotRestoreBalancedModeWhenNotCustomMode()
    {
        var service = new FakeFanControlService
        {
            SmartFanMode = SmartFanMode.Quiet
        };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService())
        {
            IsSupported = true,
            SelectedFanMode = SmartFanMode.Quiet
        };

        await viewModel.ShutdownAsync();

        Assert.Empty(service.SetModeCalls);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public async Task ShutdownAsync_DoesNotRestoreBalancedModeWhenUnsupported()
    {
        var service = new FakeFanControlService { IsSupported = false };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService())
        {
            IsSupported = false,
            SelectedFanMode = SmartFanMode.Custom
        };

        await viewModel.ShutdownAsync();

        Assert.Empty(service.SetModeCalls);
    }

    [Fact]
    public async Task ShutdownAsync_GuardsAgainstMultipleCalls()
    {
        var service = new FakeFanControlService();
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService())
        {
            IsSupported = true,
            SelectedFanMode = SmartFanMode.Custom
        };

        await viewModel.ShutdownAsync();
        await viewModel.ShutdownAsync();

        Assert.Equal([SmartFanMode.Balanced], service.SetModeCalls);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public async Task UpdateStatus_RaisesPropertyNotifications()
    {
        var viewModel = new MainViewModel(
            new FakeFanControlService(), new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            var changed = new List<string?>();
            viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            viewModel.UpdateStatus("custom", ApplicationStatusKind.Warning);

            Assert.Contains(nameof(MainViewModel.StatusMessage), changed);
            Assert.Contains(nameof(MainViewModel.StatusKind), changed);
            Assert.Contains(nameof(MainViewModel.EffectiveStatusKind), changed);
            Assert.Equal("custom", viewModel.StatusMessage);
            Assert.Equal(ApplicationStatusKind.Warning, viewModel.StatusKind);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task StopTimer_StopsRefreshTimer()
    {
        var service = new FakeFanControlService
        {
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 1 }]
        };
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            viewModel.StopTimer();

            Assert.True(settings.SaveCount > 0);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task SaveFanCurve_StoresZoneCurveAndPersists()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var settings = new InMemorySettingsService();
        var viewModel = new MainViewModel(new FakeFanControlService(), settings, new FakeAutoStartService());
        try
        {
            viewModel.SaveFanCurve(5, curve);

            Assert.Equal(curve, settings.Settings.FanCurves[5]);
            Assert.True(settings.SaveCount > 0);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task GetFanCurve_ReturnsDefaultWhenNoCurveIsStored()
    {
        var viewModel = new MainViewModel(
            new FakeFanControlService(), new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            var curve = viewModel.GetFanCurve(7);

            Assert.Equal(FanTable.Default().Speeds, curve);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task GetFanCurve_ReturnsStoredGlobalCurve()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var settings = new InMemorySettingsService(new FanSettings { GlobalFanCurve = curve });
        var viewModel = new MainViewModel(new FakeFanControlService(), settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();

            var result = viewModel.GetFanCurve(99);

            Assert.Equal(curve, result);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task SelectedFanMode_RaisesIsCustomModeNotification()
    {
        var viewModel = new MainViewModel(
            new FakeFanControlService(), new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            var changed = new List<string?>();
            viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            viewModel.SelectedFanMode = SmartFanMode.Custom;

            Assert.Contains(nameof(MainViewModel.SelectedFanMode), changed);
            Assert.True(viewModel.IsCustomMode);

            changed.Clear();
            viewModel.SelectedFanMode = SmartFanMode.Balanced;
            Assert.False(viewModel.IsCustomMode);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task FullSpeedCommand_TogglesFullSpeedState()
    {
        var service = new FakeFanControlService();
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            viewModel.FullSpeedCommand.Execute(true);

            Assert.True(viewModel.IsFullSpeed);
            Assert.True(service.SetFullSpeedCalls.Count > 0);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task FullSpeedCommand_IgnoresNullParameter()
    {
        var viewModel = new MainViewModel(
            new FakeFanControlService(), new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            viewModel.FullSpeedCommand.Execute(null);

            Assert.False(viewModel.IsFullSpeed);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyModeAsync_GuardsAgainstConcurrentExecution()
    {
        var service = new FakeFanControlService();
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            viewModel.IsBusy = true;
            viewModel.SelectedFanMode = SmartFanMode.Quiet;

            await viewModel.ApplyModeAsync();

            Assert.Empty(service.SetModeCalls);
        }
        finally
        {
            viewModel.IsBusy = false;
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ToggleFullSpeedAsync_GuardsAgainstConcurrentExecution()
    {
        var service = new FakeFanControlService();
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            viewModel.IsBusy = true;
            viewModel.IsFullSpeed = false;

            await viewModel.ToggleFullSpeedAsync(true);

            Assert.False(viewModel.IsFullSpeed);
            Assert.Empty(service.SetFullSpeedCalls);
        }
        finally
        {
            viewModel.IsBusy = false;
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ResetAsync_GuardsAgainstConcurrentExecution()
    {
        var service = new FakeFanControlService();
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            viewModel.IsBusy = true;

            await viewModel.ResetAsync();

            Assert.Empty(service.SetModeCalls);
            Assert.Empty(service.SetFullSpeedCalls);
        }
        finally
        {
            viewModel.IsBusy = false;
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_PreservesPollingIntervalWithinSafeRange()
    {
        var service = new FakeFanControlService
        {
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 1 }]
        };
        var settings = new InMemorySettingsService(new FanSettings { PollingIntervalMs = 5000 });
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_DetectsAndReportsConflictingSoftwareInWarning()
    {
        var conflicts = "VantageService,LenovoVantage";
        Environment.SetEnvironmentVariable(
            VisualTestFanControlService.ConflictEnvironmentVariable, conflicts);
        try
        {
            var service = new FakeFanControlService
            {
                DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 1 }]
            };
            var viewModel = new MainViewModel(
                service, new InMemorySettingsService(), new FakeAutoStartService());
            try
            {
                await viewModel.InitializeAsync();

                Assert.Equal(ApplicationStatusKind.Warning, viewModel.StatusKind);
                Assert.True(viewModel.HasConflictWarning);
                Assert.Contains("VantageService", viewModel.ConflictWarning);
            }
            finally
            {
                await viewModel.ShutdownAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                VisualTestFanControlService.ConflictEnvironmentVariable, null);
        }
    }

    [Fact]
    public async Task ApplyModeAsync_DoesNotReapplyCurveForNonCustomMode()
    {
        var service = new FakeFanControlService
        {
            DiscoveredFans = [new FanInfo { FanId = 1, SensorId = 1 }]
        };
        var settings = new InMemorySettingsService(new FanSettings
        {
            FanCurves = { [1] = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7] }
        });
        var viewModel = new MainViewModel(service, settings, new FakeAutoStartService());
        try
        {
            await viewModel.InitializeAsync();
            service.SetFanTableCalls.Clear();
            viewModel.SelectedFanMode = SmartFanMode.Performance;

            await viewModel.ApplyModeAsync();

            Assert.Equal([SmartFanMode.Performance], service.SetModeCalls);
            Assert.Empty(service.SetFanTableCalls);
            Assert.False(viewModel.IsCustomMode);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ToggleFullSpeedAsync_DisablingReportsConnected()
    {
        var service = new FakeFanControlService { FullSpeed = true };
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            await viewModel.ToggleFullSpeedAsync(false);

            Assert.False(viewModel.IsFullSpeed);
            Assert.Equal(ApplicationStatusKind.Connected, viewModel.StatusKind);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ResetAsync_ReportsErrors()
    {
        var service = new FakeFanControlService
        {
            SetModeException = new InvalidOperationException("reset blocked")
        };
        var viewModel = new MainViewModel(
            service, new InMemorySettingsService(), new FakeAutoStartService())
        {
            SelectedFanMode = SmartFanMode.Quiet
        };
        try
        {
            await viewModel.ResetAsync();

            Assert.Equal(ApplicationStatusKind.Error, viewModel.StatusKind);
            Assert.Contains("reset blocked", viewModel.StatusMessage);
            Assert.False(viewModel.IsBusy);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Fact]
    public async Task HasConflictWarning_ReflectsConflictWarningState()
    {
        var viewModel = new MainViewModel(
            new FakeFanControlService(), new InMemorySettingsService(), new FakeAutoStartService());
        try
        {
            Assert.False(viewModel.HasConflictWarning);

            viewModel.ConflictWarning = "warning text";
            Assert.True(viewModel.HasConflictWarning);

            viewModel.ConflictWarning = "";
            Assert.False(viewModel.HasConflictWarning);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }
}
