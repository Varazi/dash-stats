using System.Diagnostics;

namespace DashStats.Collectors;

/// <summary>Runs every collector once per interval on one background thread and hands the rows to the UI.</summary>
sealed class Collector : IDisposable
{
    readonly Settings _s;
    readonly AutoResetEvent _wake = new(false);
    readonly FpsCollector _fps = new();
    readonly NetworkCollector _net;
    HardwareCollector? _hw;
    Thread? _thread;
    volatile bool _run = true;

    TimeSpan _lastCpu;
    long _lastT;

    public event Action<List<RowData>, Dictionary<string, Metric>>? Updated;

    public Collector(Settings s)
    {
        _s = s;
        _net = new NetworkCollector(s.PingHost);
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "collector" };
        _thread.Start();
    }

    void Loop()
    {
        try { _hw = new HardwareCollector(); }
        catch (Exception e) { Log.Write(e); }

        var sw = new Stopwatch();
        while (_run)
        {
            sw.Restart();
            var rows = new List<RowData>(96);
            var metrics = new Dictionary<string, Metric>();
            Safe(() => _fps.Collect(rows, metrics, Native.ForegroundPid()));
            Safe(() => _hw?.Collect(rows, metrics));
            Safe(() => _net.Collect(rows, metrics));
            Safe(() => Self(rows, metrics));
            if (_run) Updated?.Invoke(rows, metrics);
            Safe(() => Dump(rows, metrics));

            int wait = Math.Max(250, _s.IntervalMs) - (int)sw.ElapsedMilliseconds;
            if (wait > 0) _wake.WaitOne(wait);
        }
    }

    /// <summary>Troubleshooting: create "debug.flag" in the settings folder to get every row written to snapshot.txt.</summary>
    static void Dump(List<RowData> rows, Dictionary<string, Metric> metrics)
    {
        if (!File.Exists(Path.Combine(Setup.DataDir, "debug.flag"))) return;
        File.WriteAllLines(Path.Combine(Setup.DataDir, "snapshot.txt"),
            rows.Select(r => $"{r.Section,-32} {r.Label,-14} {r.Value}  [{r.Level}]")
                .Concat(metrics.Select(kv => $"metric {kv.Key,-10} {kv.Value}")));
    }

    static void Safe(Action a)
    {
        try { a(); } catch (Exception e) { Log.Write(e); }
    }

    /// <summary>DashStats' own cost, including PresentMon — so "lightweight" is measured, not assumed.</summary>
    void Self(List<RowData> rows, Dictionary<string, Metric> m)
    {
        const string sec = "SYSTEM";
        using var me = Process.GetCurrentProcess();
        var cpu = me.TotalProcessorTime;
        long mem = me.WorkingSet64;
        if (_fps.Process is { } pm)
        {
            try { pm.Refresh(); if (!pm.HasExited) { cpu += pm.TotalProcessorTime; mem += pm.WorkingSet64; } } catch { }
        }

        long now = Environment.TickCount64;
        double? pct = _lastT > 0 && now > _lastT
            ? (cpu - _lastCpu).TotalMilliseconds / ((now - _lastT) * Environment.ProcessorCount) * 100
            : null;
        _lastCpu = cpu;
        _lastT = now;

        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        rows.Add(new(sec, "sys.uptime", "Uptime", Fmt.Span(up)));
        m["uptime"] = new(Fmt.SpanShort(up), "", "since last restart", 100, null);
        rows.Add(new(sec, "sys.self", "DashStats", $"{(pct is { } p ? $"{p:0.0}%" : Fmt.None)} CPU · {mem / 1048576} MB",
            Level.Dim));

        if (File.Exists(Path.Combine(Setup.DataDir, "debug.flag")))
            rows.Add(new(sec, "sys.memdebug", "Mem detail",
                $"ws {me.WorkingSet64 >> 20} · private {me.PrivateMemorySize64 >> 20} · gc {GC.GetTotalMemory(false) >> 20} · " +
                $"modules {me.Modules.Count} · threads {me.Threads.Count}", Level.Dim));
    }

    public void Dispose()
    {
        _run = false;
        _wake.Set();
        _thread?.Join(3000);
        _fps.Dispose();
        _net.Dispose();
        try { _hw?.Dispose(); } catch { }
    }
}
