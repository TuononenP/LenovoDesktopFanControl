using System.IO;
using System.Text.Json;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public interface ISettingsService
{
    FanSettings Load();
    void Save(FanSettings settings);
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsDir;
    private readonly string _settingsFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LenovoDesktopFanControl"))
    {
    }

    internal SettingsService(string settingsDir)
    {
        _settingsDir = settingsDir;
        _settingsFile = Path.Combine(settingsDir, "settings.json");
    }

    public FanSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFile))
                return new FanSettings();

            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<FanSettings>(json, JsonOptions) ?? new FanSettings();
        }
        catch
        {
            return new FanSettings();
        }
    }

    public void Save(FanSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFile, json);
        }
        catch
        {
        }
    }
}
