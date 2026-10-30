using System.IO;
using Path = System.IO.Path;

namespace ShotX.Services;

public static class PathService
{
    public static string DataRoot
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("SHOTX_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShotX");
        }
    }

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string LogFile => Path.Combine(DataRoot, "shotx.log");

    public static void EnsureDataRoot() => Directory.CreateDirectory(DataRoot);
}
