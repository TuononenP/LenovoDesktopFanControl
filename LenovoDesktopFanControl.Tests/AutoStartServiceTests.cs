using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Tests;

public sealed class AutoStartServiceTests
{
    [Fact]
    public void BuildTaskArguments_StartsLightingBackgroundHost()
    {
        const string executablePath =
            @"C:\Program Files\Lenovo Desktop Fan Control\LenovoDesktopFanControl.exe";

        var arguments = ApplicationLaunch.BuildTaskArguments(
            executablePath,
            LightingBackgroundHost.BackgroundArgument);

        Assert.Equal("--lighting-background", arguments);
        Assert.Equal("PT0S", AutoStartService.TaskExecutionTimeLimit);
    }

    [Fact]
    public void BuildTaskSecurityDescriptor_AllowsUserRemovalWithoutTaskModification()
    {
        const string userSid = "S-1-5-21-1-2-3-1001";

        var descriptor = AutoStartService.BuildTaskSecurityDescriptor(userSid);

        Assert.StartsWith("O:BAG:SY", descriptor);
        Assert.Contains($"(A;;SDGR;;;{userSid})", descriptor);
        Assert.DoesNotContain($"(A;;FA;;;{userSid})", descriptor);
        Assert.DoesNotContain($"(A;;GW;;;{userSid})", descriptor);
    }
}
