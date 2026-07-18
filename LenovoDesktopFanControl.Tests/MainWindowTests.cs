namespace LenovoDesktopFanControl.Tests;

public sealed class MainWindowTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void ShouldHideOnClose_RespectsMinimizeToTrayUnlessExitWasRequested(
        bool exitRequested,
        bool minimizeToTray,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldHideOnClose(exitRequested, minimizeToTray));
    }
}
