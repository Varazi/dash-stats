namespace DashStats;

/// <summary>One headline stat, split into parts so each look can lay it out its own way.</summary>
/// <param name="Pct">0–100, for gauges and bars; null when there is no natural scale.</param>
/// <param name="Raw">The underlying number, for history graphs (falls back to <paramref name="Pct"/>).</param>
public sealed record Metric(
    string Value, string Unit = "", string Sub = "",
    double? Pct = null, double? Raw = null, Level Level = Level.Normal);

public sealed record MetricInfo(string Id, string Label, string Color);

/// <summary>A kind of use ("Gaming", "Hosting a server") and the four stats it cares about most.</summary>
public sealed record Preset(string Id, string Name, string Watches, string[] Big);

public static class Catalog
{
    public static readonly Dictionary<string, MetricInfo> Metrics = new MetricInfo[]
    {
        new("fps", "FPS", "#FFD166"),
        new("hottest", "Hottest", "#FF9F6E"),
        new("cpu", "CPU", "#4EA8FF"),
        new("core", "Busiest core", "#4EA8FF"),
        new("gpu", "GPU", "#5BD68A"),
        new("vram", "VRAM", "#5BD68A"),
        new("gpupow", "GPU power", "#5BD68A"),
        new("gputemp", "GPU temp", "#FF9F6E"),
        new("encoder", "Video encoder", "#5BD68A"),
        new("ram", "RAM", "#C792EA"),
        new("disk", "Disk", "#B8C0CC"),
        new("down", "Download", "#45D0E0"),
        new("up", "Upload", "#45D0E0"),
        new("ping", "Ping", "#45D0E0"),
        new("net", "Connection", "#7EE2A8"),
        new("conns", "Connections", "#45D0E0"),
        new("uptime", "Uptime", "#B8C0CC"),
        new("battery", "Battery", "#A3E635"),
        new("power", "Power draw", "#A3E635"),
    }.ToDictionary(m => m.Id);

    /// <summary>
    /// Stats that are a share of something with a real "full" (load, memory, charge). Only these get a bar in the
    /// Gauges look; temperatures, power, speeds, ping and counts have no natural maximum, so they show as numbers.
    /// </summary>
    public static readonly HashSet<string> Bars = ["cpu", "core", "gpu", "vram", "ram", "encoder", "battery"];

    public static readonly Preset[] Presets =
    [
        new("coding", "Coding & builds", "CPU and busiest core, RAM, disk activity", ["cpu", "core", "ram", "disk"]),
        new("server", "Hosting a server", "Upload, connections, packet loss, uptime", ["up", "conns", "net", "uptime"]),
        new("ai", "AI / model training", "GPU load, VRAM, GPU temp and power", ["gpu", "vram", "gputemp", "gpupow"]),
        new("gaming", "Gaming", "FPS and 1% lows, temps, ping", ["fps", "hottest", "ping", "gpu"]),
        new("network", "Networking", "Speeds, ping, jitter, loss, hiccups", ["down", "ping", "net", "up"]),
        new("farm", "GPU farming / rendering", "GPU load, temps, power, for long runs", ["gpu", "gputemp", "gpupow", "uptime"]),
        new("stream", "Streaming / recording", "Upload, video encoder, CPU, FPS", ["up", "encoder", "cpu", "fps"]),
        new("everyday", "Everyday / on battery", "Battery, power draw, temperatures", ["battery", "power", "hottest", "cpu"]),
    ];

    public static readonly (string Id, string Name, string Desc)[] Looks =
    [
        ("hero", "Hero numbers", "Big bold figures"),
        ("gauges", "Gauges", "Dials, like a car"),
        ("graphs", "Graphs", "Live history lines"),
        ("minimal", "Minimal", "Quiet, text only"),
    ];

    /// <summary>
    /// The main use supplies the big stats; every other ticked use adds its stats as small rows (no repeats).
    /// </summary>
    public static (List<string> Big, List<string> Small) Compose(ICollection<string> uses, string? main)
    {
        var chosen = Presets.Where(p => uses.Contains(p.Id)).ToList();
        var mainPreset = chosen.FirstOrDefault(p => p.Id == main) ?? chosen.FirstOrDefault();
        var big = mainPreset?.Big.Where(Applies).ToList() ?? [];
        var small = new List<string>();
        foreach (var p in chosen.Where(p => p != mainPreset))
            foreach (var id in p.Big.Where(Applies))
                if (!big.Contains(id) && !small.Contains(id)) small.Add(id);
        return (big, small);
    }

    /// <summary>Stats this PC can't have: Battery on a desktop would only ever show "—".</summary>
    static bool Applies(string id) => id != "battery" || HasBattery;

    static readonly bool HasBattery =
        System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
}

/// <summary>The last minute of every metric, so a look switch doesn't start graphs from empty.</summary>
public sealed class MetricHistory
{
    const int Length = 60;
    readonly Dictionary<string, List<double>> _values = new();

    public void Push(IReadOnlyDictionary<string, Metric> metrics)
    {
        foreach (var (id, m) in metrics)
        {
            if ((m.Raw ?? m.Pct) is not { } v) continue;
            if (!_values.TryGetValue(id, out var list)) _values[id] = list = new List<double>(Length);
            if (list.Count == Length) list.RemoveAt(0);
            list.Add(v);
        }
    }

    public IReadOnlyList<double> Get(string id) => _values.TryGetValue(id, out var l) ? l : [];
}
