using System.IO;

namespace LenovoDesktopFanControl.Services;

public static class Log
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LenovoDesktopFanControl");

    private static readonly string LogFile = Path.Combine(LogDir, "log.txt");

    private static readonly object Lock = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex != null ? $"{message}: {ex}" : message);

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(LogFile, line);
            }
        }
        catch { }
    }
}