using System.Diagnostics;

namespace ShotX.Services;

/// <summary>
/// Runs ShotX resident in the tray via a per-user logon Scheduled Task (schtasks /IT, no stored
/// password) rather than a true Windows Service — a WPF app can't show UI from Session 0.
/// Mirrors the approach used by ReminderX.
/// </summary>
public static class StartupService
{
    private const string TaskName = "ShotX Background";

    public static bool IsRegistered()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupService.IsRegistered failed: {ex.Message}");
            return false;
        }
    }

    public static void Register()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            var args = $"/Create /F /SC ONLOGON /IT /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --background\"";
            var (exitCode, stderr) = Run(args);
            if (exitCode != 0) Logger.Log($"StartupService.Register: schtasks exited {exitCode}: {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupService.Register failed: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try
        {
            var (exitCode, stderr) = Run($"/Delete /F /TN \"{TaskName}\"");
            if (exitCode != 0) Logger.Log($"StartupService.Unregister: schtasks exited {exitCode}: {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupService.Unregister failed: {ex.Message}");
        }
    }

    private static (int ExitCode, string StdErr) Run(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        var stderr = proc?.StandardError.ReadToEnd() ?? string.Empty;
        proc?.WaitForExit(5000);
        return (proc?.ExitCode ?? -1, stderr);
    }
}
