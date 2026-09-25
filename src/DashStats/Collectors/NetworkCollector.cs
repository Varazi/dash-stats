using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DashStats.Collectors;

/// <summary>Throughput, latency/jitter/loss to the internet and the router, retransmits, and a hiccup log.</summary>
sealed class NetworkCollector : IDisposable
{
    readonly Pinger _inet, _router;
    NetworkInterface? _nic;
    IPAddress? _gateway;
    long _nextPick;

    long _rx = -1, _tx = -1, _t;
    double _down, _up;
    readonly Queue<(long T, long Sent, long Resent)> _tcp = new();
    readonly Queue<(long T, long Errors, long Discards)> _err = new();

    string? _wifi;
    Level _wifiLevel;
    long _nextWifi;
    bool _wifiBroken;

    public NetworkCollector(string host)
    {
        _inet = new Pinger(() => host);
        _router = new Pinger(() => _gateway?.ToString());
    }

    public void Dispose() { _inet.Dispose(); _router.Dispose(); }

    public void Collect(List<RowData> rows, Dictionary<string, Metric> m)
    {
        const string sec = "NETWORK";
        long now = Environment.TickCount64;
        if (now >= _nextPick || _nic is null || _nic.OperationalStatus != OperationalStatus.Up)
        {
            PickAdapter();
            _nextPick = now + 10_000;
        }

        if (_nic is not null) Throughput(now);
        var retrans = Retransmits(now);

        var inet = _inet.Stats();
        var router = _router.Stats();
        var (hiccups, lastHiccup) = _inet.Hiccups();
        var (routerHiccups, _) = _router.Hiccups();

        if (_nic is null)
        {
            rows.Add(new(sec, "net.adapter", "Adapter", "offline", Level.Bad));
        }
        else
        {
            rows.Add(new(sec, "net.adapter", "Adapter", $"{_nic.Name} · {Fmt.Bits(_nic.Speed)} link"));
            if (_nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                if (now >= _nextWifi && !_wifiBroken) { ReadWifi(); _nextWifi = now + 10_000; }
                if (_wifi is not null) rows.Add(new(sec, "net.wifi", "Wi-Fi", _wifi, _wifiLevel));
            }
        }

        rows.Add(new(sec, "net.down", "Down", Fmt.Bits(_down), Level.Normal, _down, 0));
        rows.Add(new(sec, "net.up", "Up", Fmt.Bits(_up), Level.Normal, _up, 0));

        // Wi-Fi routinely jitters 10–25 ms; only flag it when it starts to hurt calls and games.
        var pingLevel = Fmt.Worst(Fmt.Hi(inet.Avg, 80, 150), Fmt.Hi(inet.Jitter, 30, 60));
        rows.Add(new(sec, "net.ping", "Ping", inet.Last is null && inet.Samples > 0 ? "timeout"
            : $"{Fmt.Ms(inet.Last)} · avg {Fmt.Ms(inet.Avg)} · jitter {Fmt.Ms(inet.Jitter)}",
            inet.Last is null ? Level.Bad : pingLevel, inet.Last, 0));
        if (_gateway is not null)
            rows.Add(new(sec, "net.router", "Router", router.Last is null && router.Samples > 0 ? "timeout" : Fmt.Ms(router.Last),
                Fmt.Hi(router.Avg, 20, 60)));

        var lossLevel = Fmt.Worst(Fmt.Hi(inet.Loss, 0.1, 3), Fmt.Hi(router.Loss, 0.1, 3));
        rows.Add(new(sec, "net.loss", "Loss (60s)", _gateway is null ? $"{inet.Loss:0.#}%"
            : $"{inet.Loss:0.#}% internet · {router.Loss:0.#}% router", lossLevel));

        rows.Add(new(sec, "net.retrans", "TCP retrans", retrans is null ? Fmt.None : $"{retrans:0.0#}%", Fmt.Hi(retrans, 1, 3)));

        var errs = ErrorsPerMin();
        rows.Add(new(sec, "net.errors", "Pkt errors", $"{errs.Errors} · discards {errs.Discards}  /min", Fmt.Hi(errs.Errors, 1, 20)));

        string hic = hiccups == 0 ? "none in the last hour"
            : $"{hiccups} in 1h · last {Fmt.Ago(lastHiccup!.Value.At)} ({lastHiccup.Value.Kind})";
        if (routerHiccups > 0) hic += $" · router {routerHiccups}";
        rows.Add(new(sec, "net.hiccups", "Hiccups", hic, Fmt.Hi(hiccups, 1, 5)));

        var (diag, diagLevel) = Diagnose(inet, router, retrans);
        rows.Add(new(sec, "net.health", "Health", diag, diagLevel));

        rows.Add(new(Sections.Lite, "lite.net", "NET", $"↓{Fmt.Bits(_down)}  ↑{Fmt.Bits(_up)}", Level.Normal, _down, 0, View.Lite));
        rows.Add(new(Sections.Lite, "lite.ping", "PING", inet.Last is null ? "timeout" : $"{Fmt.Ms(inet.Last),-7}{inet.Loss:0.#}% loss",
            Fmt.Worst(pingLevel, lossLevel, inet.Last is null ? Level.Bad : Level.Normal), inet.Last, 0, View.Lite));

        // Headline metrics for presets.
        _downPeak = Math.Max(_downPeak, _down);
        _upPeak = Math.Max(_upPeak, _up);
        var (dv, du) = Fmt.BitsParts(_down);
        var (uv, uu) = Fmt.BitsParts(_up);
        m["down"] = new(dv, du, $"↑ {Fmt.Bits(_up)}", _down / _downPeak * 100, _down);
        m["up"] = new(uv, uu, $"↓ {Fmt.Bits(_down)}", _up / _upPeak * 100, _up);
        m["ping"] = inet.Last is null && inet.Samples > 0
            ? new("—", "ms", "timeout", 100, null, Level.Bad)
            : new(Fmt.Num(inet.Last), "ms", $"jitter {Fmt.Num(inet.Jitter)} · {inet.Loss:0.#}% loss",
                inet.Last is { } p ? Math.Min(100, p / 2) : null, inet.Last, Fmt.Worst(pingLevel, lossLevel));
        string verdict = diagLevel switch { Level.Bad => "Drops", Level.Warn => "Unstable", Level.Dim => "…", _ => "OK" };
        string detail = diagLevel is Level.Good ? (hiccups == 0 ? "no drops in 1h" : $"{hiccups} hiccups in 1h") : diag;
        m["net"] = new(verdict, "", detail, diagLevel switch { Level.Bad => 25, Level.Warn => 60, _ => 100 }, null, diagLevel);

        if (now >= _nextConns) { CountConnections(); _nextConns = now + 5000; }
        if (_established >= 0)
            m["conns"] = new(_established.ToString(), "", $"{_listening} listening ports", null, _established);
    }

    double _downPeak = 1e6, _upPeak = 1e6;
    long _nextConns;
    int _established = -1, _listening;

    void CountConnections()
    {
        try
        {
            var g = IPGlobalProperties.GetIPGlobalProperties();
            _established = g.GetActiveTcpConnections().Count(c => c.State == TcpState.Established);
            _listening = g.GetActiveTcpListeners().Select(e => e.Port).Distinct().Count();
        }
        catch (Exception e) { Log.Write(e); }
    }

    (string, Level) Diagnose(Pinger.Summary inet, Pinger.Summary router, double? retrans)
    {
        if (_nic is null) return ("no network adapter up", Level.Bad);
        if (inet.Samples < 5) return ("measuring…", Level.Dim);
        if (router.Loss > 0 && inet.Loss > 0) return ("drops on Wi-Fi / local network", Level.Bad);
        if (inet.Loss >= 2) return ("drops past your router (ISP)", Level.Bad);
        if (router.Avg > 30) return ("slow local link (Wi-Fi?)", Level.Warn);
        if (inet.Avg > 120) return ("high latency", Level.Warn);
        if (inet.Jitter > 30) return ("unstable latency", Level.Warn);
        if (retrans > 3) return ("many TCP retransmits", Level.Warn);
        if (inet.Loss > 0) return ("minor loss", Level.Warn);
        return ("healthy", Level.Good);
    }

    void PickAdapter()
    {
        try
        {
            // Ask Windows which interface it would route internet traffic through.
            uint dest = BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes());
            Native.GetBestInterface(dest, out uint idx);
            var nics = NetworkInterface.GetAllNetworkInterfaces();

            NetworkInterface? best = nics.FirstOrDefault(n =>
            {
                try { return n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().GetIPv4Properties()?.Index == idx; }
                catch { return false; }
            });
            best ??= nics.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && Gateway(n) is not null);

            if (best?.Id != _nic?.Id) { _rx = _tx = -1; _down = _up = 0; }
            _nic = best;
            _gateway = best is null ? null : Gateway(best);
        }
        catch (Exception e) { Log.Write(e); }
    }

    static IPAddress? Gateway(NetworkInterface n) =>
        n.GetIPProperties().GatewayAddresses.Select(g => g.Address)
         .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));

    void Throughput(long now)
    {
        var st = _nic!.GetIPStatistics();
        long rx = st.BytesReceived, tx = st.BytesSent;
        if (_rx >= 0 && now > _t)
        {
            double dt = (now - _t) / 1000.0;
            _down = Math.Max(0, (rx - _rx) * 8 / dt);
            _up = Math.Max(0, (tx - _tx) * 8 / dt);
        }
        _rx = rx; _tx = tx; _t = now;

        _err.Enqueue((now, st.IncomingPacketsWithErrors + st.OutgoingPacketsWithErrors,
                           st.IncomingPacketsDiscarded + st.OutgoingPacketsDiscarded));
        while (_err.Count > 2 && _err.Peek().T < now - 60_000) _err.Dequeue();
    }

    (long Errors, long Discards) ErrorsPerMin()
    {
        if (_err.Count < 2) return (0, 0);
        var a = _err.Peek();
        var b = _err.Last();
        return (Math.Max(0, b.Errors - a.Errors), Math.Max(0, b.Discards - a.Discards));
    }

    double? Retransmits(long now)
    {
        try
        {
            var g = IPGlobalProperties.GetIPGlobalProperties();
            var v4 = g.GetTcpIPv4Statistics();
            long sent = v4.SegmentsSent, resent = v4.SegmentsResent;
            try { var v6 = g.GetTcpIPv6Statistics(); sent += v6.SegmentsSent; resent += v6.SegmentsResent; } catch { }

            _tcp.Enqueue((now, sent, resent));
            while (_tcp.Count > 2 && _tcp.Peek().T < now - 30_000) _tcp.Dequeue();
            var a = _tcp.Peek();
            long ds = sent - a.Sent;
            return ds < 50 ? 0 : (resent - a.Resent) * 100.0 / ds;
        }
        catch { return null; }
    }

    void ReadWifi()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);

            // One block per wireless adapter; find ours by name.
            Dictionary<string, string>? mine = null, cur = null;
            foreach (var raw in text.Split('\n'))
            {
                int c = raw.IndexOf(':');
                if (c < 0) continue;
                string k = raw[..c].Trim(), v = raw[(c + 1)..].Trim();
                if (k == "Name") { cur = new(); if (v == _nic?.Name) mine = cur; }
                if (cur is not null) cur[k] = v;
            }
            mine ??= cur;
            if (mine is null || !mine.TryGetValue("Signal", out var signal))
            {
                if (text.Contains("location", StringComparison.OrdinalIgnoreCase)) _wifiBroken = true;
                _wifi = null;
                return;
            }
            double.TryParse(signal.TrimEnd('%'), out double pct);
            var parts = new List<string> { $"signal {signal}" };
            if (mine.TryGetValue("Band", out var band)) parts.Add(band);
            if (mine.TryGetValue("Channel", out var ch)) parts.Add("ch " + ch);
            _wifi = string.Join(" · ", parts);
            _wifiLevel = Fmt.Lo(pct, 55, 35);
        }
        catch (Exception e) { _wifiBroken = true; Log.Write(e); }
    }
}

/// <summary>Pings a host once a second in the background and keeps a 60-sample window.</summary>
sealed class Pinger : IDisposable
{
    public readonly record struct Summary(double? Last, double? Avg, double? Jitter, double Loss, int Samples);

    readonly Func<string?> _host;
    string? _lastHost;
    int _warmup;
    readonly double?[] _ring = new double?[60];
    int _n, _i;
    readonly List<(DateTime At, string Kind)> _events = [];
    readonly object _lock = new();
    readonly CancellationTokenSource _cts = new();

    public Pinger(Func<string?> host)
    {
        _host = host;
        _ = Task.Run(Loop);
    }

    public void Dispose() => _cts.Cancel();

    async Task Loop()
    {
        using var ping = new Ping();
        var payload = new byte[32];
        while (!_cts.IsCancellationRequested)
        {
            long t0 = Environment.TickCount64;
            if (_host() is { } h)
            {
                double? ms = null;
                try
                {
                    var r = await ping.SendPingAsync(h, 1000, payload);
                    if (r.Status == IPStatus.Success) ms = r.RoundtripTime;
                }
                catch { }
                // The first pings to a new host often time out while ARP/routes warm up; don't count them.
                if (h != _lastHost) { _lastHost = h; _warmup = 2; }
                if (_warmup > 0 && ms is null) _warmup--;
                else { _warmup = 0; Record(ms); }
            }
            int wait = 1000 - (int)(Environment.TickCount64 - t0);
            try { await Task.Delay(Math.Max(50, wait), _cts.Token); } catch { break; }
        }
    }

    void Record(double? ms)
    {
        lock (_lock)
        {
            var ok = Values().Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray();
            double median = ok.Length > 0 ? ok[ok.Length / 2] : 0;

            _ring[_i] = ms;
            _i = (_i + 1) % _ring.Length;
            if (_n < _ring.Length) _n++;

            string? kind = ms is null ? "loss" : ok.Length >= 10 && ms > Math.Max(100, median * 3) ? "spike" : null;
            var now = DateTime.Now;
            if (kind is not null)
            {
                // Consecutive bad samples of the same kind are one hiccup.
                if (_events.Count > 0 && _events[^1].Kind == kind && (now - _events[^1].At).TotalSeconds < 5)
                    _events[^1] = (now, kind);
                else
                    _events.Add((now, kind));
            }
            _events.RemoveAll(e => (now - e.At).TotalHours > 1);
        }
    }

    IEnumerable<double?> Values()
    {
        for (int k = 0; k < _n; k++) yield return _ring[(_i - _n + k + _ring.Length) % _ring.Length];
    }

    public Summary Stats()
    {
        lock (_lock)
        {
            if (_n == 0) return new(null, null, null, 0, 0);
            var all = Values().ToArray();
            var ok = all.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            double? jitter = ok.Length < 2 ? null : ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average();
            return new(all[^1], ok.Length > 0 ? ok.Average() : null, jitter, (all.Length - ok.Length) * 100.0 / all.Length, all.Length);
        }
    }

    public (int Count, (DateTime At, string Kind)? Last) Hiccups()
    {
        lock (_lock) return (_events.Count, _events.Count > 0 ? _events[^1] : null);
    }
}
