using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EasyRazer.Core;

/// <summary>
/// Checks GitHub for a newer release and installs it silently.
///
/// - Only runs for the installed app (the portable exe can't replace itself).
/// - Checks a minute after startup and then every 12 hours.
/// - Waits until you're on the desktop (Default profile) so it never restarts mid-game.
/// - Verifies the download against the SHA-256 GitHub publishes for the file.
/// - Runs the installer silently; it restarts Easy Razer in the tray afterwards.
/// </summary>
public sealed class Updater : IDisposable
{
    public const string Repo = "BobotheHutt/Easy-Razer";
    private const string SetupAsset = "EasyRazer-Setup.exe";

    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    // Must come after Current: static fields are set up top to bottom, and the client uses the version.
    private static readonly HttpClient Http = CreateClient();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60_000 };
    private readonly Func<bool> _safeToRestart;
    private bool _busy;


    /// <summary>True when running from the installer's folder (so updating by installer is possible).</summary>
    public static bool IsInstalled
    {
        get
        {
            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Easy Razer");
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            return string.Equals(Path.GetFullPath(exeDir).TrimEnd('\\'), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    public string Status { get; private set; } = IsInstalled ? "Not checked yet" : "Updates need the installed version (EasyRazer-Setup.exe)";
    public Version? Available { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Status"/> changes.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised just before the app exits to install an update.</summary>
    public event Action<Version>? Installing;

    public Func<bool> AutoInstallEnabled { get; set; } = () => true;

    public Updater(Func<bool> safeToRestart)
    {
        _safeToRestart = safeToRestart;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = 12 * 60 * 60 * 1000;
            await CheckAsync(manual: false);
        };
        if (IsInstalled) _timer.Start();
    }

    /// <summary>Check now. When <paramref name="manual"/> is true, installs even if auto-install is off.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        if (!IsInstalled)
        {
            SetStatus("Updates need the installed version (EasyRazer-Setup.exe)");
            return;
        }
        _busy = true;
        try
        {
            SetStatus("Checking for updates…");
            var release = await GetLatestAsync();
            if (release == null)
            {
                SetStatus("Couldn't check for updates (no internet?)");
                return;
            }
            if (release.Version <= Current)
            {
                Available = null;
                SetStatus($"Up to date (v{Current})");
                return;
            }

            Available = release.Version;
            if (!manual && !AutoInstallEnabled())
            {
                SetStatus($"Version {release.Version} is available");
                return;
            }

            // Don't pull the rug out mid-game; try again later.
            if (!manual && !_safeToRestart())
            {
                SetStatus($"Version {release.Version} will install when you're back on the desktop");
                _timer.Interval = 10 * 60 * 1000;
                return;
            }

            SetStatus($"Downloading version {release.Version}…");
            var file = await DownloadAsync(release);
            if (file == null)
            {
                SetStatus("Update download failed, will try again later");
                return;
            }

            SetStatus($"Installing version {release.Version}…");
            Log.Info($"Updating {Current} -> {release.Version}");
            Installing?.Invoke(release.Version);
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-") { UseShellExecute = true });
            Application.Exit();
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            SetStatus("Update check failed, see log");
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed record Release(Version Version, string Url, string? Sha256);

    private static async Task<Release?> GetLatestAsync()
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn($"Update check: GitHub answered {(int)resp.StatusCode}");
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != SetupAsset) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                string? sha = null;
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:"))
                    sha = d["sha256:".Length..];
                return url == null ? null : new Release(version, url, sha);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> DownloadAsync(Release release)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "EasyRazerUpdate");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, SetupAsset);
            var bytes = await Http.GetByteArrayAsync(release.Url);

            if (release.Sha256 != null)
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error($"Update download didn't match its checksum ({actual} vs {release.Sha256}); not installing");
                    return null;
                }
            }
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn($"Update download failed: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EasyRazer", Current.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    private void SetStatus(string s)
    {
        Status = s;
        StatusChanged?.Invoke();
    }

    public void Dispose() => _timer.Dispose();
}
