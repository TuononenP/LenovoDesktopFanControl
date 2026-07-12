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
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LenovoDesktopFanControl");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public FanSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new FanSettings();

            var json = File.ReadAllText(SettingsFile);
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
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
        }
    }
}