using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace DashStats;

/// <summary>
/// Looks for a newer GitHub release now and then, offers it (UpdateWindow), and swaps the running exe for it.
/// A running exe can't be overwritten but can be renamed, so the old one becomes "DashStats.exe.old" and is
/// deleted on a later run.
/// </summary>
static class Updater
{
    const string LatestApi = "https://api.github.com/repos/Varazi/dash-stats/releases/latest";
    const string AssetName = "DashStats.exe";
    static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2), Every = TimeSpan.FromHours(6);

    public sealed record Release(Version Version, string PageUrl, string DownloadUrl, long Size, string? Sha256);

    public static Version Current { get; } = ThreePart(Assembly.GetEntryAssembly()!.GetName().Version!);
    public static string CurrentText => Current.ToString(3);

    static readonly HttpClient Http = CreateClient();
    static DispatcherTimer? _timer;
    static bool _busy;

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"DashStats/{CurrentText}"); // GitHub's API requires one
        return h;
    }

    static Version ThreePart(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    static string OldExe => Setup.ExePath + ".old";
    // Next to the exe it replaces, so the download stays in the same admin-only folder from check to swap.
    static string Staging => Path.Combine(Path.GetDirectoryName(Setup.ExePath)!, "update");

    /// <summary>A quiet check a couple of minutes after start, then every few hours.</summary>
    public static void Start(Controller c)
    {
        _timer = new DispatcherTimer { Interval = FirstCheck };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Every;
            if (!_busy) CleanUp();
            await Check(c, manual: false);
        };
        _timer.Start();
    }

    /// <summary>Leftovers from the last update; by now the previous process has long exited.</summary>
    static void CleanUp()
    {
        try { if (File.Exists(OldExe)) File.Delete(OldExe); } catch { }
        try { if (Directory.Exists(Staging)) Directory.Delete(Staging, recursive: true); } catch { }
    }

    /// <summary>
    /// Manual (tray → Check for updates…) always asks and reports "up to date" or a failure.
    /// Automatic respects "Don't ask again" and "Remind me later" and stays silent unless there's something new.
    /// </summary>
    public static async Task Check(Controller c, bool manual)
    {
        if (_busy) return;
        if (!manual && (!c.S.CheckForUpdates || c.S.UpdateRemindAfter > DateTime.Now)) return;
        _busy = true;
        try
        {
            Release? r;
            try { r = await Latest(); }
            catch (Exception e)
            {
                Log.Write("Update check failed: " + e.Message);
                if (manual)
                    MessageBox.Show("Couldn't check for updates right now. Check your internet connection and try again later.",
                        "DashStats", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (r is null || r.Version <= Current)
            {
                if (manual)
                    MessageBox.Show($"You have the latest version of DashStats ({CurrentText}).",
                        "DashStats", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new UpdateWindow(c, r).ShowDialog();
        }
        finally { _busy = false; }
    }

    static async Task<Release?> Latest()
    {
        using var resp = await Http.GetAsync(LatestApi);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // "v0.3.0" or "v0.3.0-beta" → 0.3.0
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version)) return null;

        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (a.GetProperty("name").GetString() != AssetName) continue;
            // GitHub publishes "sha256:<hex>" for each asset; older releases may not have it.
            string? sha = a.TryGetProperty("digest", out var d) && d.GetString() is { } s && s.StartsWith("sha256:")
                ? s["sha256:".Length..] : null;
            return new Release(ThreePart(version), root.GetProperty("html_url").GetString() ?? "",
                a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64(), sha);
        }
        return null;
    }

    /// <summary>Downloads the new exe next to our data and checks it's complete and matches GitHub's checksum.</summary>
    public static async Task<string> Download(Release r, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Staging);
        var path = Path.Combine(Staging, AssetName);
        using var resp = await Http.GetAsync(r.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long read = 0;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                sha.AppendData(buf, 0, n);
                read += n;
                progress.Report(r.Size > 0 ? (double)read / r.Size : 0);
            }
        }

        if (read != r.Size) throw new InvalidDataException("the download was incomplete");
        if (r.Sha256 is not null && !Convert.ToHexString(sha.GetHashAndReset()).Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("the download didn't match the release's checksum");
        return path;
    }

    /// <summary>Puts the downloaded exe where this one is, then restarts into it.</summary>
    public static void Install(Controller c, string downloaded)
    {
        var exe = Setup.ExePath;
        try { if (File.Exists(OldExe)) File.Delete(OldExe); } catch { }
        File.Move(exe, OldExe);
        try { File.Move(downloaded, exe); }
        catch { File.Move(OldExe, exe); throw; }

        c.S.UpdateRemindAfter = null;
        c.S.Save();
        Log.Write($"Updated from {CurrentText}; restarting");
        ((App)Application.Current).Restart(exe);
    }
}
