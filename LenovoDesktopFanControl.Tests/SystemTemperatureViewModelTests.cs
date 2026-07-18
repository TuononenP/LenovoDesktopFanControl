using LenovoDesktopFanControl.Models;
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
}
