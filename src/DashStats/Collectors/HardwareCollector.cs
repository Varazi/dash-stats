using LibreHardwareMonitor.Hardware;
using Forms = System.Windows.Forms;

namespace DashStats.Collectors;

/// <summary>CPU, GPU, memory, storage, battery and fans via LibreHardwareMonitor.</summary>
sealed class HardwareCollector : IDisposable
{
    readonly Computer _pc;
    readonly bool _driver = Setup.PawnIoInstalled();
    int _tick;

    // Readings shared between sections for the combined metrics (hottest part, total power).
    readonly List<(double Temp, string Where)> _temps = [];
    double? _cpuPower, _gpuPower;
    double _gpuPowerPeak = 1, _powerPeak = 1;

    public HardwareCollector()
    {
        _pc = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsBatteryEnabled = true,
            IsMotherboardEnabled = true,
        };
        _pc.Open();
    }

    public void Dispose() => _pc.Close();

    public void Collect(List<RowData> rows, Dictionary<string, Metric> m)
    {
        _tick++;
        foreach (var hw in _pc.Hardware)
        {
            // Slow-changing and comparatively expensive to poll: refresh less often.
            if (hw.HardwareType == HardwareType.Storage && _tick % 3 != 1) continue;
            if (hw.HardwareType == HardwareType.Motherboard && _tick % 5 != 1) continue;
            try { Update(hw); } catch (Exception e) { Log.Write($"{hw.Name}: {e.Message}"); }
        }

        _temps.Clear();
        _cpuPower = _gpuPower = null;

        Cpu(rows, m);
        Gpus(rows, m);
        Memory(rows, m);
        Storage(rows, m);
        Battery(rows, m);
        Fans(rows);
        Combined(m);
    }

    static void Update(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware) Update(sub);
    }

    IHardware? First(HardwareType t) => _pc.Hardware.FirstOrDefault(h => h.HardwareType == t);

    /// <summary>First sensor of the type whose name matches, in preference order.</summary>
    static float? Val(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw is null) return null;
        foreach (var n in names)
            foreach (var s in hw.Sensors)
                if (s.SensorType == type && s.Value.HasValue && s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                    return s.Value;
        return null;
    }

    static float? Max(IHardware hw, SensorType type) =>
        hw.Sensors.Where(s => s.SensorType == type && s.Value.HasValue).Max(s => s.Value);

    string NoDriver(float? v) => v is null && !_driver ? "needs PawnIO" : Fmt.None;

    void Cpu(List<RowData> rows, Dictionary<string, Metric> m)
    {
        var cpu = First(HardwareType.Cpu);
        if (cpu is null) return;
        string sec = "CPU · " + Fmt.Short(cpu.Name);

        var load = Val(cpu, SensorType.Load, "CPU Total");
        var temp = Val(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Tctl", "Package", "Core Max")
                   ?? Max(cpu, SensorType.Temperature);
        var clocks = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core #", StringComparison.Ordinal) && s.Value > 1)
            .Select(s => (double)s.Value!.Value).ToArray();
        var power = Val(cpu, SensorType.Power, "Package", "CPU Package", "Core (SVI2 TFN)");
        var busiest = Val(cpu, SensorType.Load, "CPU Core Max") ??
                      cpu.Sensors.Where(s => s.SensorType == SensorType.Load && s.Name != "CPU Total" && s.Value.HasValue).Max(s => s.Value);

        var tempText = temp is null ? NoDriver(temp) : Fmt.Temp(temp);
        var tempLevel = Fmt.Hi(temp, 85, 95);
        var loadLevel = Fmt.Hi(load, 85, 97);
        double? maxClock = clocks.Length > 0 ? clocks.Max() : null;

        rows.Add(new(sec, "cpu.load", "Load", Fmt.Pct(load), loadLevel, load, 100));
        rows.Add(new(sec, "cpu.temp", "Temp", tempText, tempLevel, temp, 0));
        rows.Add(new(sec, "cpu.clock", "Clock", clocks.Length == 0 ? Fmt.None
            : $"{Fmt.Clock(clocks.Average())} avg · {Fmt.Clock(maxClock)} max"));
        rows.Add(new(sec, "cpu.power", "Power", power is null ? NoDriver(power) : Fmt.Watts(power)));
        rows.Add(new(sec, "cpu.thread", "Busiest core", Fmt.Pct(busiest), Fmt.Hi(busiest, 95, 100)));

        rows.Add(new(Sections.Lite, "lite.cpu", "CPU", $"{Fmt.Pct(load),-5}{(temp is null ? "" : Fmt.Temp(temp))}",
            Fmt.Worst(loadLevel, tempLevel), load, 100, View.Lite));

        m["cpu"] = new(Fmt.Num(load), "%", $"{tempText} · {Fmt.Clock(maxClock)}", load, load, Fmt.Worst(loadLevel, tempLevel));
        m["core"] = new(Fmt.Num(busiest), "%", "busiest core", busiest, busiest, Fmt.Hi(busiest, 95, 100));
        if (temp is not null) _temps.Add((temp.Value, "CPU"));
        _cpuPower = power;
    }

    static int GpuRank(IHardware g) => g.HardwareType switch
    {
        HardwareType.GpuNvidia => 0,
        HardwareType.GpuAmd => g.Name.Contains(" RX ", StringComparison.Ordinal) ? 1 : 2,
        _ => 3,
    };

    void Gpus(List<RowData> rows, Dictionary<string, Metric> m)
    {
        var gpus = _pc.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .OrderBy(GpuRank).ToList();

        for (int i = 0; i < gpus.Count; i++)
        {
            var g = gpus[i];
            string sec = "GPU · " + Fmt.Short(g.Name), k = $"gpu{i}.";

            var load = Val(g, SensorType.Load, "GPU Core", "D3D 3D") ?? Max(g, SensorType.Load);
            var temp = Val(g, SensorType.Temperature, "GPU Core", "GPU Hot Spot") ?? Max(g, SensorType.Temperature);
            var hot = Val(g, SensorType.Temperature, "GPU Hot Spot");
            var clk = Val(g, SensorType.Clock, "GPU Core");
            var mclk = Val(g, SensorType.Clock, "GPU Memory");
            var vUsed = Val(g, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
            var vTotal = Val(g, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            var power = Val(g, SensorType.Power, "GPU Package", "GPU Power", "GPU Core") ?? Max(g, SensorType.Power);
            // A sleeping laptop dGPU can report garbage (thousands of watts).
            if (power is < 0 or > 1000) power = null;
            var fan = Val(g, SensorType.Fan, "GPU Fan", "GPU Fan 1");
            var encoder = Val(g, SensorType.Load, "GPU Video Engine", "D3D Video Encode");

            var loadLevel = Fmt.Hi(load, 95, 100);
            var tempLevel = Fmt.Hi(temp, 80, 88);
            double? vramPct = vTotal is > 0 ? vUsed / vTotal * 100 : null;

            rows.Add(new(sec, k + "load", "Load", Fmt.Pct(load), loadLevel, load, 100));
            rows.Add(new(sec, k + "temp", "Temp", hot is null ? Fmt.Temp(temp) : $"{Fmt.Temp(temp)} · hotspot {Fmt.Temp(hot)}",
                tempLevel, temp, 0));
            rows.Add(new(sec, k + "clock", "Clock", mclk is null ? Fmt.Clock(clk) : $"{Fmt.Clock(clk)} · mem {Fmt.Clock(mclk)}"));
            if (vUsed is not null)
                rows.Add(new(sec, k + "vram", "VRAM", vTotal is > 0
                    ? $"{vUsed / 1024:0.0} / {vTotal / 1024:0.0} GB" : $"{vUsed / 1024:0.0} GB",
                    Fmt.Hi(vramPct, 90, 97)));
            if (power is not null) rows.Add(new(sec, k + "power", "Power", Fmt.Watts(power)));
            if (fan is not null) rows.Add(new(sec, k + "fan", "Fan", Fmt.Rpm(fan)));

            if (temp is not null) _temps.Add((temp.Value, gpus.Count > 1 ? (i == 0 ? "GPU" : "iGPU") : "GPU"));
            if (power is not null) _gpuPower = (_gpuPower ?? 0) + power;

            if (i != 0) continue;

            // The first (most capable) GPU is the one presets talk about.
            rows.Add(new(Sections.Lite, "lite.gpu", "GPU", $"{Fmt.Pct(load),-5}{(temp is null ? "" : Fmt.Temp(temp))}",
                Fmt.Worst(loadLevel, tempLevel), load, 100, View.Lite));

            if (power is { } p) _gpuPowerPeak = Math.Max(_gpuPowerPeak, p);
            m["gpu"] = new(Fmt.Num(load), "%", $"{Fmt.Temp(temp)} · {Fmt.Watts(power)}", load, load, Fmt.Worst(loadLevel, tempLevel));
            m["gputemp"] = new(Fmt.Num(temp), "°C", hot is null ? Fmt.Short(g.Name) : $"hotspot {Fmt.Temp(hot)}", temp, temp, tempLevel);
            m["gpupow"] = new(Fmt.Num(power), "W", fan is null ? $"clock {Fmt.Clock(clk)}" : $"fan {Fmt.Rpm(fan)}",
                power / _gpuPowerPeak * 100, power);
            if (vUsed is not null)
                m["vram"] = new($"{vUsed / 1024:0.0}", "GB", vTotal is > 0 ? $"of {vTotal / 1024:0} GB" : "in use",
                    vramPct, vUsed / 1024, Fmt.Hi(vramPct, 90, 97));
            m["encoder"] = new(Fmt.Num(encoder), "%", "GPU video engine", encoder, encoder);
        }
    }

    void Memory(List<RowData> rows, Dictionary<string, Metric> m)
    {
        var mem = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual"));
        var vm = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name.Contains("Virtual"));
        if (mem is null) return;
        const string sec = "MEMORY";

        var used = Val(mem, SensorType.Data, "Memory Used");
        var avail = Val(mem, SensorType.Data, "Memory Available");
        var load = Val(mem, SensorType.Load, "Memory");
        var commit = vm is not null ? Val(vm, SensorType.Load, "Virtual Memory", "Memory") : Val(mem, SensorType.Load, "Virtual Memory");
        var level = Fmt.Hi(load, 85, 95);

        rows.Add(new(sec, "mem.ram", "RAM", used is null ? Fmt.Pct(load)
            : $"{used:0.0} / {used + avail:0} GB · {Fmt.Pct(load)}", level, load, 100));
        if (commit is not null) rows.Add(new(sec, "mem.commit", "Committed", Fmt.Pct(commit), Fmt.Hi(commit, 85, 95)));

        rows.Add(new(Sections.Lite, "lite.ram", "RAM", used is null ? Fmt.Pct(load) : $"{Fmt.Pct(load),-5}{used:0.0} GB",
            level, load, 100, View.Lite));

        m["ram"] = new(Fmt.Num(load), "%", used is null ? "" : $"{used:0.0} / {used + avail:0} GB", load, load, level);
    }

    void Storage(List<RowData> rows, Dictionary<string, Metric> m)
    {
        var disks = _pc.Hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
        double read = 0, write = 0, busiest = 0;
        for (int i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            string k = $"disk{i}.";
            var temp = Val(d, SensorType.Temperature, "Temperature", "Composite Temperature") ?? Max(d, SensorType.Temperature);
            var used = Val(d, SensorType.Load, "Used Space");
            var busy = Val(d, SensorType.Load, "Total Activity");
            var rd = Val(d, SensorType.Throughput, "Read Rate");
            var wr = Val(d, SensorType.Throughput, "Write Rate");
            read += rd ?? 0;
            write += wr ?? 0;
            busiest = Math.Max(busiest, busy ?? 0);
            if (temp is not null) _temps.Add((temp.Value, "SSD"));

            var parts = new List<string>();
            if (temp is not null) parts.Add(Fmt.Temp(temp));
            if (used is not null) parts.Add($"{used:0}% full");
            rows.Add(new("STORAGE", k + "info", Fmt.Short(d.Name), parts.Count == 0 ? Fmt.None : string.Join(" · ", parts),
                Fmt.Worst(Fmt.Hi(temp, 60, 70), Fmt.Hi(used, 90, 97))));
            if (rd is not null || wr is not null)
                rows.Add(new("STORAGE", k + "io", "  I/O", $"R {Fmt.Bytes(rd)} · W {Fmt.Bytes(wr)}",
                    Level.Normal, busy, 100));
        }
        if (disks.Count == 0) return;
        var (v, u) = Fmt.BytesParts(read + write);
        m["disk"] = new(v, u, $"R {Fmt.Bytes(read)} · W {Fmt.Bytes(write)}", busiest, read + write);
    }

    void Battery(List<RowData> rows, Dictionary<string, Metric> m)
    {
        var b = First(HardwareType.Battery);
        if (b is null) return;
        const string sec = "BATTERY";
        bool ac = Forms.SystemInformation.PowerStatus.PowerLineStatus == Forms.PowerLineStatus.Online;

        var lvl = Val(b, SensorType.Level, "Charge Level");
        // Negative when the battery holds a bit more than its design capacity.
        var wear = Val(b, SensorType.Level, "Degradation Level") is { } w ? Math.Max(0, w) : (float?)null;
        var chg = Val(b, SensorType.Power, "Charge Rate");
        var dis = Val(b, SensorType.Power, "Discharge Rate");
        var left = Val(b, SensorType.TimeSpan, "Remaining Time (Estimated)", "Remaining Time");
        var level = ac ? Level.Normal : Fmt.Lo(lvl, 25, 10);

        rows.Add(new(sec, "bat.charge", "Charge", $"{Fmt.Pct(lvl)} · {(ac ? "plugged in" : "on battery")}", level, lvl, 100));
        if (chg is > 0) rows.Add(new(sec, "bat.rate", "Rate", $"+{Fmt.Watts(chg)}"));
        else if (dis is > 0) rows.Add(new(sec, "bat.rate", "Rate", $"−{Fmt.Watts(dis)}"));
        if (!ac && left is > 0) rows.Add(new(sec, "bat.left", "Remaining", Fmt.Span(TimeSpan.FromSeconds(left.Value))));
        if (wear is not null) rows.Add(new(sec, "bat.wear", "Wear", Fmt.Pct(wear), Fmt.Hi(wear, 20, 40)));

        if (!ac) rows.Add(new(Sections.Lite, "lite.bat", "BAT", Fmt.Pct(lvl), level, lvl, 100, View.Lite));

        string sub = ac ? (chg is > 0 ? $"charging +{Fmt.Watts(chg)}" : "plugged in")
                   : left is > 0 ? $"{Fmt.SpanShort(TimeSpan.FromSeconds(left.Value))} left" : "on battery";
        m["battery"] = new(Fmt.Num(lvl), "%", sub, lvl, lvl, level);
    }

    void Fans(List<RowData> rows)
    {
        int n = 0;
        foreach (var hw in _pc.Hardware.Where(h => h.HardwareType is HardwareType.Motherboard or HardwareType.EmbeddedController))
            foreach (var sub in hw.SubHardware.Prepend(hw))
                foreach (var s in sub.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value > 0))
                    rows.Add(new("SYSTEM", $"fan{n++}", s.Name, Fmt.Rpm(s.Value)));
    }

    void Combined(Dictionary<string, Metric> m)
    {
        if (_temps.Count > 0)
        {
            var (t, where) = _temps.MaxBy(x => x.Temp);
            m["hottest"] = new(Fmt.Num(t), "°C", where, t, t, Fmt.Hi(t, 85, 95));
        }
        if (_cpuPower is not null || _gpuPower is not null)
        {
            double total = (_cpuPower ?? 0) + (_gpuPower ?? 0);
            _powerPeak = Math.Max(_powerPeak, total);
            m["power"] = new(Fmt.Num(total), "W", $"CPU {Fmt.Num(_cpuPower)} · GPU {Fmt.Num(_gpuPower)}", total / _powerPeak * 100, total);
        }
    }
}
