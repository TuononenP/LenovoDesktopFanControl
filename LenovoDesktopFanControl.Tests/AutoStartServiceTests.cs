using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class AutoStartServiceTests
{
    [Fact]
    public void BuildCreateArguments_UsesElevatedLogonTaskAndStartsMinimized()
    {
        const string executablePath =
            @"C:\Program Files\Lenovo Desktop Fan Control\LenovoDesktopFanControl.exe";

        var arguments = AutoStartService.BuildCreateArguments(executablePath);

        Assert.Equal("/Create", arguments[0]);
        Assert.Contains("/SC", arguments);
        Assert.Contains("ONLOGON", arguments);
        Assert.Contains("/RL", arguments);
        Assert.Contains("HIGHEST", arguments);
        Assert.Contains(
            $"\"{executablePath}\" --minimized",
            arguments);
    }
}
