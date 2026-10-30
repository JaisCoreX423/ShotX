using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShotX.Models;

namespace ShotX.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static AppSettings? _cached;

    public static AppSettings Current => _cached ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathService.SettingsFile))
            {
                var json = File.ReadAllText(PathService.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    _cached = loaded;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"SettingsService.Load failed, using defaults: {ex.Message}");
        }

        _cached = new AppSettings();
        return _cached;
    }

    public static void Save(AppSettings? settings = null)
    {
        settings ??= _cached ?? new AppSettings();
        _cached = settings;

        try
        {
            PathService.EnsureDataRoot();
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempFile = PathService.SettingsFile + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Copy(tempFile, PathService.SettingsFile, overwrite: true);
            File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            Logger.Log($"SettingsService.Save failed: {ex.Message}");
        }
    }
}
