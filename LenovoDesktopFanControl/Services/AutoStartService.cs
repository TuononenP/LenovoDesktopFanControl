using System.Diagnostics;
using System.IO;
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
    private const string TaskName = "Lenovo Desktop Fan Control";

    public bool IsEnabled()
    {
        try
        {
            if (RunScheduledTasks(["/Query", "/TN", TaskName]).ExitCode == 0)
                return true;

            // Recognize older releases that used the Run key so the checkbox
            // remains accurate until the entry is migrated.
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
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var result = RunScheduledTasks(BuildCreateArguments(executablePath));
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Windows Task Scheduler rejected the auto-start task: {result.Error}");

        DeleteLegacyRunEntry();
    }

    public void Disable()
    {
        _ = RunScheduledTasks(["/Delete", "/TN", TaskName, "/F"]);
        DeleteLegacyRunEntry();
    }

    internal static IReadOnlyList<string> BuildCreateArguments(string executablePath) =>
    [
        "/Create",
        "/TN", TaskName,
        "/TR", $"\"{executablePath}\" --minimized",
        "/SC", "ONLOGON",
        "/RL", "HIGHEST",
        "/F"
    ];

    private static ProcessResult RunScheduledTasks(IReadOnlyList<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
    }

    private static void DeleteLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, false);
    }

    private readonly record struct ProcessResult(int ExitCode, string Error);
}
