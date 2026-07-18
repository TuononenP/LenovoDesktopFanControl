using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public sealed class SystemTemperatureViewModelTests
{
    [Fact]
    public void Update_RecordsValidTemperatureInHistory()
    {
        var viewModel = new SystemTemperatureViewModel("CPU");

        viewModel.Update(new SystemTemperatureReading("CPU", 42, "CPU Package"));
        viewModel.Update(new SystemTemperatureReading("CPU", null, "Unavailable"));

        var sample = Assert.Single(viewModel.TemperatureHistory.Samples);
        Assert.Equal(42, sample.Celsius);
        Assert.Null(viewModel.Temperature);
        Assert.Equal("Unavailable", viewModel.Detail);
    }

    [Fact]
    public void OpenTemperatureChartCommand_UsesProvidedCallback()
    {
        SystemTemperatureViewModel? opened = null;
        var viewModel = new SystemTemperatureViewModel("SSD", temperature => opened = temperature);

        viewModel.OpenTemperatureChartCommand.Execute(null);

        Assert.Same(viewModel, opened);
    }

    [Fact]
    public void RefreshLocalizedName_UsesLocalizedDisplayNameWhileKeepingStableSourceId()
    {
        try
        {
            LocalizationService.SetLanguage("fi-FI");
            var viewModel = new SystemTemperatureViewModel("Motherboard");

            Assert.Equal("Motherboard", viewModel.SourceId);
            Assert.Equal("Emolevy", viewModel.Name);
        }
        finally
        {
            LocalizationService.SetLanguage("en");
        }
    }

    [Fact]
    public void Update_UpdatesDisplayPropertiesAndRaisesNotifications()
    {
        var viewModel = new SystemTemperatureViewModel("GPU");
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.Update(new SystemTemperatureReading("GPU", 65, "NVIDIA GPU temperature"));

        Assert.Equal(65, viewModel.Temperature);
        Assert.Equal("65 °C", viewModel.TemperatureText);
        Assert.Equal("NVIDIA GPU temperature", viewModel.Detail);
        Assert.Contains(nameof(SystemTemperatureViewModel.Temperature), changed);
        Assert.Contains(nameof(SystemTemperatureViewModel.TemperatureText), changed);
        Assert.Contains(nameof(SystemTemperatureViewModel.Detail), changed);
    }

    [Fact]
    public void RestoreTemperatureHistory_CopiesInputAndRemovesExpiredSamples()
    {
        var now = DateTime.UtcNow;
        var source = new TemperatureHistory
        {
            Samples =
            [
                new(now.AddHours(-13), 20),
                new(now.AddMinutes(-10), 50)
            ]
        };
        var viewModel = new SystemTemperatureViewModel("CPU");

        viewModel.RestoreTemperatureHistory(source);
        source.Samples.Clear();

        var sample = Assert.Single(viewModel.TemperatureHistory.Samples);
        Assert.Equal(50, sample.Celsius);
    }
}
