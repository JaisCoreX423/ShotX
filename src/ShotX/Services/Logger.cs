using System.IO;

namespace ShotX.Services;

public static class Logger
{
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            PathService.EnsureDataRoot();
            lock (Lock)
            {
                File.AppendAllText(PathService.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
