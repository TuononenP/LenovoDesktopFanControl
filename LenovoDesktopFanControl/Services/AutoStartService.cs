using Microsoft.Win32;

namespace LenovoDesktopFanControl.Services;

public interface IAutoStartService
{
    bool IsEnabled();
    void Enable(string executablePath);
    void Disable();
}

public class AutoStartService : IAutoStartService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LenovoDesktopFanControl";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public void Enable(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(AppName, $"\"{executablePath}\"");
        }
        catch { }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, false);
        }
        catch { }
    }
}