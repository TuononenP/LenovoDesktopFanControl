using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public class FanViewModelTests
{
    [Fact]
    public async Task ZoneConstructor_GroupsTelemetryChannelsAndUsesAverageRpm()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var zone = new FanViewModel(service, parent,
            [
                new FanInfo { FanId = 3, TelemetryId = 0x10, Name = "Fan 3.1", CurrentRpm = 500, MaxRpm = 2500 },
                new FanInfo { FanId = 3, TelemetryId = 0x20, Name = "Fan 3.2", CurrentRpm = 600, MaxRpm = 2500 }
            ]);

            Assert.Equal(3, zone.FanId);
            Assert.Equal(2, zone.Channels.Count);
            Assert.True(zone.HasMultipleChannels);
            Assert.Equal(550, zone.CurrentRpm);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ConstructorAndTelemetryProperties_ExposeValuesAndRaiseDependentNotifications()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo
            {
                FanId = 2,
                SensorId = 5,
                Name = "CPU fan",
                MaxRpm = 2000,
                CurrentRpm = 1000,
                Temperature = 40
            });
            var changed = new List<string?>();
            fan.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            Assert.Equal(50, fan.SpeedPercentage);
            fan.CurrentRpm = 1500;
            fan.Temperature = 45;
            fan.TargetSpeedPercentage = 60;

            Assert.Equal(75, fan.SpeedPercentage);
            Assert.Contains(nameof(FanViewModel.CurrentRpm), changed);
            Assert.Contains(nameof(FanViewModel.SpeedPercentage), changed);
            Assert.Contains(nameof(FanViewModel.Temperature), changed);
            Assert.Contains(nameof(FanViewModel.TargetSpeedPercentage), changed);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task SpeedPercentage_ClampsTelemetryAboveReportedFirmwareMaximum()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(
                service,
                parent,
                new FanInfo { CurrentRpm = 2020, MaxRpm = 2000 });

            Assert.Equal(100, fan.SpeedPercentage);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task SpeedPercentage_IsZeroForUnavailableRpmOrInvalidMaximum()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var unavailable = new FanViewModel(
                service, parent, new FanInfo { CurrentRpm = null, MaxRpm = 2000 });
            var noMaximum = new FanViewModel(
                service, parent, new FanInfo { CurrentRpm = 1000, MaxRpm = 0 });

            Assert.Equal(0, unavailable.SpeedPercentage);
            Assert.Equal(0, noMaximum.SpeedPercentage);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplySpeedAsync_EnablesCustomModeAndSendsGeneratedCurve()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(
            service, parent, new FanInfo { FanId = 1, Name = "CPU fan" })
        {
            TargetSpeedPercentage = 60
        };
        try
        {
            await fan.ApplySpeedAsync();

            Assert.Equal([SmartFanMode.Custom], service.SetModeCalls);
            var call = Assert.Single(service.SetFanTableCalls);
            Assert.Equal(1, call.FanId);
            Assert.Equal(FanTable.FromPercentage(60).Speeds, call.Curve);
            Assert.Equal(SmartFanMode.Custom, parent.SelectedFanMode);
            Assert.Equal(SmartFanMode.Custom, settings.Settings.Mode);
            Assert.Equal(FanTable.FromPercentage(60).Speeds, settings.Settings.FanCurves[1]);
            Assert.Equal(ApplicationStatusKind.Connected, parent.StatusKind);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplySpeedAsync_IgnoresConcurrentRequestAndReportsServiceFailure()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        var fan = new FanViewModel(service, parent, new FanInfo());
        try
        {
            fan.IsBusy = true;
            await fan.ApplySpeedAsync();
            Assert.Empty(service.SetModeCalls);
            Assert.Empty(service.SetFanTableCalls);

            fan.IsBusy = false;
            service.SetFanTableException = new InvalidOperationException("table denied");
            await fan.ApplySpeedAsync();

            Assert.Equal(ApplicationStatusKind.Error, parent.StatusKind);
            Assert.Contains("table denied", parent.StatusMessage);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyCurveAsync_RejectsInvalidLengthWithoutChangingState()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(service, parent, new FanInfo { FanId = 4 });
        try
        {
            await fan.ApplyCurveAsync([1, 2, 3]);

            Assert.Empty(service.SetModeCalls);
            Assert.Empty(service.SetFanTableCalls);
            Assert.Empty(settings.Settings.FanCurves);
            Assert.Null(settings.Settings.GlobalFanCurve);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyCurveAsync_SendsAndPersistsValidCurve()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(
            service, parent, new FanInfo { FanId = 4, Name = "Chassis fan" });
        try
        {
            await fan.ApplyCurveAsync(curve);

            Assert.Equal(curve, Assert.Single(service.SetFanTableCalls).Curve);
            Assert.Equal(curve, settings.Settings.FanCurves[4]);
            Assert.NotSame(curve, settings.Settings.FanCurves[4]);
            Assert.True(settings.SaveCount > 0);
            Assert.Equal(ApplicationStatusKind.Connected, parent.StatusKind);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyCurveAsync_DoesNotPersistCurveWhenFirmwareWriteFails()
    {
        var service = new FakeFanControlService
        {
            SetFanTableException = new InvalidOperationException("write failed")
        };
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(service, parent, new FanInfo { FanId = 4 });
        try
        {
            await fan.ApplyCurveAsync([1, 1, 2, 2, 3, 3, 4, 5, 6, 7]);

            Assert.Empty(settings.Settings.FanCurves);
            Assert.Null(settings.Settings.GlobalFanCurve);
            Assert.Equal(ApplicationStatusKind.Error, parent.StatusKind);
            Assert.Contains("write failed", parent.StatusMessage);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RefreshLocalizedName_UpdatesResourceBackedNamesOnly()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var localized = new FanViewModel(service, parent, new FanInfo
            {
                Name = "old",
                NameResourceKey = "FanNameCpu"
            });
            var literal = new FanViewModel(service, parent, new FanInfo { Name = "Literal" });

            localized.RefreshLocalizedName();
            literal.RefreshLocalizedName();

            Assert.NotEqual("old", localized.FanName);
            Assert.Equal("Literal", literal.FanName);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Constructor_FallsBackToFanIdWhenTelemetryIdIsNegative()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo
            {
                FanId = 42,
                TelemetryId = -1,
                SensorId = 5,
                Name = "Fan"
            });

            Assert.Equal(42, fan.FanId);
            Assert.Equal(42, fan.TelemetryId);
            Assert.Equal(5, fan.SensorId);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Constructor_PreservesExplicitTelemetryIdWhenSet()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo
            {
                FanId = 42,
                TelemetryId = 16,
                SensorId = 5,
                Name = "Fan"
            });

            Assert.Equal(16, fan.TelemetryId);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RefreshLocalizedName_DoesNotChangeNameWhenResourceKeyIsEmpty()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo { Name = "Literal" });

            fan.RefreshLocalizedName();

            Assert.Equal("Literal", fan.FanName);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RefreshLocalizedName_FormatsArgumentInResourceKeyLookup()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo
            {
                FanId = 4,
                Name = "old",
                NameResourceKey = "FanNameN",
                NameResourceArgument = 4
            });

            fan.RefreshLocalizedName();

            Assert.Contains("4", fan.FanName);
            Assert.NotEqual("old", fan.FanName);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplySpeedAsync_GuardsAgainstConcurrentRequest()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(service, parent, new FanInfo { FanId = 1 })
        {
            TargetSpeedPercentage = 60,
            IsBusy = true
        };
        try
        {
            await fan.ApplySpeedAsync();

            Assert.Empty(service.SetModeCalls);
            Assert.Empty(service.SetFanTableCalls);
            Assert.Null(settings.Settings.GlobalFanCurve);
        }
        finally
        {
            fan.IsBusy = false;
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplySpeedAsync_ReportsModeSwitchFailure()
    {
        var service = new FakeFanControlService
        {
            SetModeException = new InvalidOperationException("mode blocked")
        };
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(service, parent, new FanInfo { FanId = 1 })
        {
            TargetSpeedPercentage = 50
        };
        try
        {
            await fan.ApplySpeedAsync();

            Assert.Empty(service.SetFanTableCalls);
            Assert.Null(settings.Settings.GlobalFanCurve);
            Assert.Equal(ApplicationStatusKind.Error, parent.StatusKind);
            Assert.Contains("mode blocked", parent.StatusMessage);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyCurveAsync_RejectsInvalidLengthEvenWhenBusy()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(service, parent, new FanInfo { FanId = 1 })
        {
            IsBusy = true
        };
        try
        {
            await fan.ApplyCurveAsync([1, 2, 3]);

            Assert.Empty(service.SetFanTableCalls);
            Assert.Null(settings.Settings.GlobalFanCurve);
            Assert.True(fan.IsBusy);
        }
        finally
        {
            fan.IsBusy = false;
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ApplyCurveAsync_AcceptsValidTenPointCurveAfterModeSwitch()
    {
        byte[] curve = [2, 2, 3, 3, 4, 4, 5, 6, 7, 8];
        var service = new FakeFanControlService
        {
            SmartFanMode = SmartFanMode.Balanced
        };
        var parent = CreateParent(service, out var settings);
        var fan = new FanViewModel(
            service, parent, new FanInfo { FanId = 5, Name = "Chassis" });
        try
        {
            await fan.ApplyCurveAsync(curve);

            Assert.Equal(SmartFanMode.Custom, parent.SelectedFanMode);
            Assert.Equal(SmartFanMode.Custom, settings.Settings.Mode);
            Assert.Equal(curve, settings.Settings.FanCurves[5]);
            Assert.Equal(ApplicationStatusKind.Connected, parent.StatusKind);
            Assert.False(fan.IsBusy);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task FanName_RaisesPropertyChangedWhenRefreshed()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo
            {
                Name = "old",
                NameResourceKey = "FanNameCpu"
            });
            var changed = new List<string?>();
            fan.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            fan.RefreshLocalizedName();

            Assert.Contains(nameof(FanViewModel.FanName), changed);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task TargetSpeedPercentage_DefaultsToFifty()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo { FanId = 1 });

            Assert.Equal(50, fan.TargetSpeedPercentage);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Commands_AreInitialized()
    {
        var service = new FakeFanControlService();
        var parent = CreateParent(service, out _);
        try
        {
            var fan = new FanViewModel(service, parent, new FanInfo { FanId = 1 });

            Assert.NotNull(fan.ApplySpeedCommand);
            Assert.NotNull(fan.EditCurveCommand);
            Assert.NotNull(fan.ApplyCurveCommand);
        }
        finally
        {
            await parent.ShutdownAsync();
        }
    }

    private static MainViewModel CreateParent(
        FakeFanControlService service,
        out InMemorySettingsService settings)
    {
        settings = new InMemorySettingsService();
        return new MainViewModel(service, settings, new FakeAutoStartService());
    }
}
