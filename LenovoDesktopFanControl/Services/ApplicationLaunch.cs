using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace LenovoDesktopFanControl.Services;

/// <summary>
/// Reconstructs the current app's launch form. During development the process
/// is dotnet.exe and needs the managed application DLL as its first argument;
/// a published app can be launched directly.
/// </summary>
internal static class ApplicationLaunch
{
    internal static ProcessStartInfo CreateProcessStartInfo(params string[] arguments)
    {
        var executablePath = GetExecutablePath();
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        AddEntryAssemblyArgumentIfNeeded(startInfo, executablePath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    internal static string BuildTaskArguments(string executablePath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var commandParts = new List<string>();
        var entryAssembly = IsCurrentDotnetHost(executablePath) ? GetEntryAssemblyPath() : null;
        if (entryAssembly != null)
            commandParts.Add(Quote(entryAssembly));
        commandParts.AddRange(arguments.Select(QuoteIfNeeded));
        return string.Join(" ", commandParts);
    }

    private static string GetExecutablePath() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The application executable path is unavailable");

    private static void AddEntryAssemblyArgumentIfNeeded(ProcessStartInfo startInfo, string executablePath)
    {
        if (IsCurrentDotnetHost(executablePath) && GetEntryAssemblyPath() is { } entryAssembly)
            startInfo.ArgumentList.Add(entryAssembly);
    }

    private static bool IsCurrentDotnetHost(string executablePath) =>
        string.Equals(executablePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static string? GetEntryAssemblyPath()
    {
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        return string.IsNullOrWhiteSpace(entryAssemblyPath) ? null : entryAssemblyPath;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? Quote(value) : value;
}
