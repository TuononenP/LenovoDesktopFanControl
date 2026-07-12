using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public class WmiFanControlServiceTests
{
    [Theory]
    [InlineData("LenovoVantageService", "VantageService")]
    [InlineData("LenovoVantage-(VantageCoreAddin)", "LenovoVantage")]
    [InlineData("SmartEngineHost64", "Lenovo Legion Space")]
    [InlineData("SmartEngineHostN64", "Lenovo Legion Space")]
    [InlineData("LenovoSmartService", "Lenovo Legion Space")]
    [InlineData("GAService", "GAService")]
    [InlineData("unrelated", null)]
    public void FindConflictingProcessName_MatchesActualLenovoProcessNames(
        string processName,
        string? expected)
    {
        Assert.Equal(expected, WmiFanControlService.FindConflictingProcessName(processName));
    }
}
