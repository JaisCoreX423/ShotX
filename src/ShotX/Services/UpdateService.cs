using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace ShotX.Services;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Installing,
    Error
}

/// <summary>
/// Keeps ShotX current from the GitHub repo's latest release: checks periodically, downloads the
/// ShotXSetup.exe asset, verifies it against the SHA-256 digest GitHub publishes for it, and runs
/// it silently. Automatic installs only happen while the app is idle (no capture/editor in front).
/// All members are used on the UI thread. Mirrors ReminderX's UpdateService.
/// </summary>
public class UpdateService
{
    public static UpdateService Instance { get; } = new();

    private const string InstallerAssetName = "ShotXSetup.exe";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan IdleRetryInterval = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private DispatcherTimer? _timer;
    private ReleaseInfo? _latest;
    private string? _downloadedInstaller;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string StatusText { get; private set; } = "";
    public string? LatestVersionText => _latest?.Version.ToString(3);

    public event Action? StateChanged;

    /// <summary>True while installing now wouldn't interrupt the user (no window on top of a capture).</summary>
    public Func<bool>? IsIdle { get; set; }

    /// <summary>Closes the app so the installer can replace its files.</summary>
    public Action? RequestShutdown { get; set; }

    private static bool IsDryRun => Environment.GetEnvironmentVariable("SHOTX_UPDATE_DRYRUN") == "1";

    private UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ShotX/{AppInfo.VersionText}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        StatusText = $"You're on version {AppInfo.VersionText}.";
    }

    public void Start()
    {
        var firstDelay = int.TryParse(Environment.GetEnvironmentVariable("SHOTX_TEST_UPDATE_DELAY_SEC"), out var s)
            ? TimeSpan.FromSeconds(s)
            : FirstCheckDelay;

        _timer = new DispatcherTimer { Interval = firstDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await AutoCycleAsync();
        };
        _timer.Start();
    }

    private async Task AutoCycleAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return;

        if (State != UpdateState.ReadyToInstall)
        {
            await CheckAsync();
            if (State != UpdateState.Available || !SettingsService.Current.AutoUpdate) return;
            await DownloadAsync();
            if (State != UpdateState.ReadyToInstall) return;
        }

        if (!SettingsService.Current.AutoUpdate) return;

        if (IsIdle?.Invoke() ?? true)
        {
            Install();
        }
        else
        {
            Logger.Log($"Update {LatestVersionText} ready; waiting until the app is idle");
            if (_timer != null) _timer.Interval = IdleRetryInterval;
        }
    }

    /// <summary>Settings > "Buscar ahora".</summary>
    public Task CheckNowAsync() => CheckAsync();

    /// <summary>Settings > "Instalar ahora": downloads if needed, then installs right away.</summary>
    public async Task InstallNowAsync()
    {
        if (State == UpdateState.Available) await DownloadAsync();
        if (State == UpdateState.ReadyToInstall) Install();
    }

    private async Task CheckAsync()
    {
        SetState(UpdateState.Checking, "Buscando actualizaciones...");
        try
        {
            var api = Environment.GetEnvironmentVariable("SHOTX_UPDATE_API")
                      ?? $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest";

            using var response = await _http.GetAsync(api);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                SetState(UpdateState.UpToDate, $"Sin versiones publicadas todavía. Tienes la versión {AppInfo.VersionText}.");
                return;
            }
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var release = ReleaseInfo.Parse(doc.RootElement, InstallerAssetName);
            var current = Normalize(AppInfo.Version);

            if (release == null || release.Version <= current)
            {
                _latest = release;
                SetState(UpdateState.UpToDate, $"Tienes la última versión ({AppInfo.VersionText}).");
                return;
            }

            _latest = release;
            if (release.DownloadUrl == null)
            {
                SetState(UpdateState.Error,
                    $"La versión {LatestVersionText} está publicada pero aún no tiene instalador adjunto.");
                return;
            }

            SetState(UpdateState.Available, $"La versión {LatestVersionText} está disponible.");
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Error, $"No se pudo buscar actualizaciones: {ex.Message}");
        }
    }

    private async Task DownloadAsync()
    {
        if (_latest?.DownloadUrl == null) return;

        SetState(UpdateState.Downloading, $"Descargando la versión {LatestVersionText}...");
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ShotX-update");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"ShotXSetup-{LatestVersionText}.exe");

            using (var response = await _http.GetAsync(_latest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path);
                await response.Content.CopyToAsync(file);
            }

            var size = new FileInfo(path).Length;
            if (_latest.Size > 0 && size != _latest.Size)
            {
                File.Delete(path);
                SetState(UpdateState.Error, $"La descarga quedó incompleta ({size:N0} de {_latest.Size:N0} bytes).");
                return;
            }

            if (_latest.Sha256 != null)
            {
                string actual;
                await using (var stream = File.OpenRead(path))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

                if (actual != _latest.Sha256)
                {
                    File.Delete(path);
                    SetState(UpdateState.Error, "El instalador descargado no coincidió con el checksum de GitHub; se descartó.");
                    return;
                }
                Logger.Log($"Update {LatestVersionText} downloaded and SHA-256 verified ({size:N0} bytes)");
            }
            else
            {
                Logger.Log($"Update {LatestVersionText} downloaded ({size:N0} bytes); GitHub published no checksum for it");
            }

            _downloadedInstaller = path;
            SetState(UpdateState.ReadyToInstall, $"La versión {LatestVersionText} está lista para instalar.");
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Error, $"No se pudo descargar la actualización: {ex.Message}");
        }
    }

    private void Install()
    {
        if (_downloadedInstaller == null || !File.Exists(_downloadedInstaller)) return;

        if (IsDryRun)
        {
            Logger.Log($"Update dry run: would install {LatestVersionText} from {_downloadedInstaller}");
            SetState(UpdateState.ReadyToInstall, $"Versión {LatestVersionText} verificada (dry run, no instalada).");
            return;
        }

        SetState(UpdateState.Installing, $"Instalando la versión {LatestVersionText}...");
        Logger.Log($"Installing update {LatestVersionText} from {_downloadedInstaller}");
        try
        {
            var psi = new ProcessStartInfo(_downloadedInstaller, "--silent")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // The installer asks for admin when double-clicked; an update into the user's own folders
            // doesn't need it, so run it as the current user — no UAC prompt while the app is idle.
            psi.Environment["__COMPAT_LAYER"] = "RunAsInvoker";
            Process.Start(psi);
            RequestShutdown?.Invoke();
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Error, $"No se pudo iniciar el instalador: {ex.Message}");
        }
    }

    private void SetState(UpdateState state, string text)
    {
        State = state;
        StatusText = text;
        if (state is not (UpdateState.Checking or UpdateState.Downloading)) Logger.Log($"Updater: {text}");
        StateChanged?.Invoke();
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private sealed record ReleaseInfo(Version Version, string? DownloadUrl, long Size, string? Sha256)
    {
        public static ReleaseInfo? Parse(JsonElement root, string assetName)
        {
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

            string? url = null, sha = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    url = asset.GetProperty("browser_download_url").GetString();
                    size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest["sha256:".Length..].ToLowerInvariant();
                    break;
                }
            }

            return new ReleaseInfo(Normalize(version), url, size, sha);
        }
    }
}
