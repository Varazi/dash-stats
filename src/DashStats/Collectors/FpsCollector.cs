using System.Diagnostics;
using System.Globalization;

namespace DashStats.Collectors;

/// <summary>
/// Frame rate for the foreground app, read from Intel PresentMon's CSV stream.
/// PresentMon listens to Windows' own present events (ETW), so it works for any GPU and any game.
/// </summary>
sealed class FpsCollector : IDisposable
{
    const string SessionName = "DashStatsPM";
    const long WindowMs = 10_000;

    readonly record struct Frame(long T, double Ms, bool Dropped);

    sealed class Chain
    {
        public required string App;
        public required int Pid;
        public readonly Queue<Frame> Frames = new();
        public long LastSeen;
    }

    readonly object _lock = new();
    readonly Dictionary<string, Chain> _chains = new();
    int _cApp = -1, _cPid = -1, _cChain = -1, _cBetween = -1, _cDisplayed = -1, _cDropped = -1;

    string _exe = "";
    int _lines;
    long _nextStart;
    string? _error;
    volatile bool _disposed;

    public Process? Process { get; private set; }

    /// <summary>Fastest display refresh rate, as the "full" mark for FPS gauges.</summary>
    static readonly Lazy<double> RefreshHz = new(() =>
    {
        try
        {
            using var q = new System.Management.ManagementObjectSearcher("SELECT CurrentRefreshRate FROM Win32_VideoController");
            double best = 0;
            foreach (var o in q.Get())
                if (o["CurrentRefreshRate"] is uint hz) best = Math.Max(best, hz);
            return best >= 30 ? best : 60;
        }
        catch { return 60; }
    });

    public void Collect(List<RowData> rows, Dictionary<string, Metric> m, int foregroundPid)
    {
        EnsureRunning();
        const string sec = "FPS";
        var s = Snapshot(foregroundPid);

        if (s is null)
        {
            string why = _error ?? "no frames (desktop)";
            m["fps"] = new(Fmt.None, "fps", _error ?? "no game running", 0, null, Level.Dim);
            rows.Add(new(sec, "fps.app", "App", why, Level.Dim));
            rows.Add(new(sec, "fps.fps", "FPS", Fmt.None, Level.Dim));
            rows.Add(new(sec, "fps.frametime", "Frame time", Fmt.None, Level.Dim));
            rows.Add(new(sec, "fps.low", "1% low", Fmt.None, Level.Dim));
            rows.Add(new(sec, "fps.stutter", "Stutters", Fmt.None, Level.Dim));
            rows.Add(new(Sections.Lite, "lite.fps", "FPS", Fmt.None, Level.Dim, View: View.Lite));
            return;
        }

        var v = s.Value;
        // Desktop apps idle at low frame rates on purpose; only judge the app you're actually in.
        Level Judge(Level l) => v.Foreground ? l : Level.Dim;
        var fpsLevel = Judge(Fmt.Lo(v.Fps, 55, 29));
        rows.Add(new(sec, "fps.app", "App", v.App + (v.Foreground ? "" : " (background)"), v.Foreground ? Level.Normal : Level.Dim));
        rows.Add(new(sec, "fps.fps", "FPS", $"{v.Fps:0}", fpsLevel, v.Fps, 0));
        rows.Add(new(sec, "fps.frametime", "Frame time", $"{Fmt.Ms(v.AvgMs)} avg · {Fmt.Ms(v.MaxMs)} worst", Judge(Level.Normal), v.AvgMs, 0));
        rows.Add(new(sec, "fps.low", "1% low", $"{v.Low1:0} fps", Judge(Fmt.Lo(v.Low1, 45, 25))));
        rows.Add(new(sec, "fps.stutter", "Stutters", $"{v.Stutters} · dropped {v.Dropped}  /10s",
            Judge(Fmt.Worst(Fmt.Hi(v.Stutters, 3, 10), Fmt.Hi(v.Dropped, 5, 30)))));

        rows.Add(new(Sections.Lite, "lite.fps", "FPS", $"{v.Fps,-5:0}1% {v.Low1:0}", fpsLevel, v.Fps, 0, View.Lite));

        m["fps"] = new($"{v.Fps:0}", "fps", v.Foreground ? $"1% low {v.Low1:0}" : $"{v.App} (background)",
            Math.Min(100, v.Fps / RefreshHz.Value * 100), v.Fps, fpsLevel);
    }

    readonly record struct Stats(string App, bool Foreground, double Fps, double AvgMs, double MaxMs, double Low1, int Stutters, int Dropped);

    Stats? Snapshot(int foregroundPid)
    {
        long now = Environment.TickCount64;
        Chain? pick = null;
        bool fg = false;
        int best = 0;

        lock (_lock)
        {
            foreach (var key in _chains.Where(kv => now - kv.Value.LastSeen > 15_000).Select(kv => kv.Key).ToList())
                _chains.Remove(key);

            // Prefer the foreground app's busiest swap chain; otherwise whatever is presenting the most.
            foreach (var c in _chains.Values)
            {
                if (now - c.LastSeen > 2000) continue;
                int recent = c.Frames.Count(f => f.T >= now - 1000);
                bool isFg = c.Pid == foregroundPid;
                if ((isFg && !fg) || (isFg == fg && recent > best)) { pick = c; fg = isFg; best = recent; }
            }
            if (pick is null) return null;

            var frames = pick.Frames.ToArray();
            var last = frames.Where(f => f.T >= now - 1000).ToArray();
            if (last.Length == 0) last = frames[^Math.Min(frames.Length, 3)..];
            double fps = last.Length * 1000.0 / last.Sum(f => f.Ms);

            var sorted = frames.Select(f => f.Ms).Order().ToArray();
            double median = sorted[sorted.Length / 2];
            double p99 = sorted[(int)((sorted.Length - 1) * 0.99)];
            int stutters = frames.Count(f => f.Ms > Math.Max(median * 2, median + 8));
            int dropped = frames.Count(f => f.Dropped);

            return new Stats(pick.App, fg, fps, last.Average(f => f.Ms), last.Max(f => f.Ms), 1000 / p99, stutters, dropped);
        }
    }

    void EnsureRunning()
    {
        if (_disposed || (Process is { HasExited: false })) return;
        long now = Environment.TickCount64;
        if (now < _nextStart) return;
        _nextStart = now + 30_000;

        try
        {
            if (Process is { HasExited: true } dead)
            {
                _error = "PresentMon stopped (retrying)";
                Log.Write($"PresentMon exited with {dead.ExitCode}");
                dead.Dispose();
                Process = null;
            }
            _exe = Setup.Extract("PresentMon.exe"); // re-checked on every (re)start

            var psi = new ProcessStartInfo(_exe,
                $"--output_stdout --no_console_stats --stop_existing_session --session_name {SessionName} " +
                "--no_track_gpu --no_track_input --exclude dwm.exe --exclude DashStats.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data is { } line) OnLine(line); };
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Write("PresentMon: " + e.Data); };
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Log.Write($"PresentMon exited ({SafeExitCode(p)})");
            p.Start();
            Log.Write($"PresentMon started: {_exe}");
            Native.TieToUs(p);
            // We run at below-normal priority; PresentMon must keep up with the event stream.
            p.PriorityClass = ProcessPriorityClass.Normal;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            Process = p;
            _error = null;
        }
        catch (Exception e)
        {
            _error = "PresentMon unavailable";
            Log.Write(e);
        }
    }

    static string SafeExitCode(Process p)
    {
        try { return p.ExitCode.ToString(); } catch { return "?"; }
    }

    void OnLine(string line)
    {
        if (_lines++ == 1) Log.Write("PresentMon first frame: " + line);
        if (line.StartsWith("Application,", StringComparison.Ordinal))
        {
            Log.Write("PresentMon columns: " + line);
            var cols = line.Split(',');
            int Col(string name) => Array.IndexOf(cols, name);
            _cApp = Col("Application");
            _cPid = Col("ProcessID");
            _cChain = Col("SwapChainAddress");
            _cBetween = Col("MsBetweenPresents");
            _cDisplayed = Col("MsUntilDisplayed");
            _cDropped = Col("Dropped");
            return;
        }
        if (_cBetween < 0 || _cPid < 0) return;

        var p = line.Split(',');
        if (p.Length <= Math.Max(_cBetween, _cPid)) return;
        if (!int.TryParse(p[_cPid], out int pid)) return;
        if (!double.TryParse(p[_cBetween], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || ms <= 0 || ms > 5000) return;

        bool dropped = _cDropped >= 0 ? p[_cDropped] == "1"
                     : _cDisplayed >= 0 && _cDisplayed < p.Length && (p[_cDisplayed] == "NA" || p[_cDisplayed] == "");
        string chainId = _cChain >= 0 && _cChain < p.Length ? p[_cChain] : "";
        long now = Environment.TickCount64;

        lock (_lock)
        {
            string key = pid + "/" + chainId;
            if (!_chains.TryGetValue(key, out var c))
                _chains[key] = c = new Chain { App = _cApp >= 0 ? p[_cApp] : "?", Pid = pid };
            c.Frames.Enqueue(new Frame(now, ms, dropped));
            c.LastSeen = now;
            while (c.Frames.Count > 5000 || c.Frames.Peek().T < now - WindowMs) c.Frames.Dequeue();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            if (Process is { HasExited: false } p) p.Kill();
            // Killing PresentMon leaves its ETW session behind; close it explicitly.
            if (_exe != "")
                System.Diagnostics.Process.Start(new ProcessStartInfo(_exe, $"--terminate_existing_session --session_name {SessionName}")
                    { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);
        }
        catch (Exception e) { Log.Write(e); }
    }
}
