using System.Text.RegularExpressions;

namespace DashStats;

static partial class Fmt
{
    public const string None = "—";

    public static string Pct(double? v) => v is { } x ? $"{x:0}%" : None;
    public static string Temp(double? v) => v is { } x ? $"{x:0}°C" : None;
    public static string Watts(double? v) => v is { } x ? $"{x:0.0} W" : None;
    public static string Ms(double? v) => v is { } x ? (x < 10 ? $"{x:0.0} ms" : $"{x:0} ms") : None;
    public static string Rpm(double? v) => v is { } x ? $"{x:0} rpm" : None;

    public static string Clock(double? mhz) => mhz is not { } x ? None
        : x >= 1000 ? $"{x / 1000:0.00} GHz" : $"{x:0} MHz";

    /// <summary>Network rates, in bits per second.</summary>
    public static string Bits(double bps) =>
        bps >= 1e9 ? $"{bps / 1e9:0.00} Gb/s" :
        bps >= 1e6 ? $"{bps / 1e6:0.0} Mb/s" :
        bps >= 1e3 ? $"{bps / 1e3:0} Kb/s" : $"{bps:0} b/s";

    /// <summary>Disk rates, in bytes per second.</summary>
    public static string Bytes(double? Bps) => Bps is not { } x ? None :
        x >= 1e9 ? $"{x / 1e9:0.0} GB/s" :
        x >= 1e6 ? $"{x / 1e6:0.0} MB/s" :
        x >= 1e3 ? $"{x / 1e3:0} KB/s" : $"{x:0} B/s";

    public static string Num(double? v, string format = "0") => v is { } x ? x.ToString(format) : None;

    /// <summary>"49°C", "9%", "16 ms": symbols hug the number, words get a space.</summary>
    public static string WithUnit(string value, string unit) =>
        string.IsNullOrEmpty(unit) || value == None ? value :
        unit[0] is '%' or '°' ? value + unit : $"{value} {unit}";

    /// <summary>Network rate as ("42.1", "Mb/s") for layouts that size number and unit separately.</summary>
    public static (string Value, string Unit) BitsParts(double bps) =>
        bps >= 1e9 ? ($"{bps / 1e9:0.00}", "Gb/s") :
        bps >= 1e6 ? ($"{bps / 1e6:0.0}", "Mb/s") :
        ($"{bps / 1e3:0}", "Kb/s");

    public static (string Value, string Unit) BytesParts(double Bps) =>
        Bps >= 1e9 ? ($"{Bps / 1e9:0.0}", "GB/s") :
        Bps >= 1e6 ? (Bps >= 1e7 ? $"{Bps / 1e6:0}" : $"{Bps / 1e6:0.0}", "MB/s") :
        ($"{Bps / 1e3:0}", "KB/s");

    public static string SpanShort(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m" : $"{t.Minutes}m";

    public static string Span(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m" :
        t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m" :
        t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s" : $"{t.Seconds}s";

    public static string Ago(DateTime at) => Span(DateTime.Now - at) + " ago";

    /// <summary>Higher is worse (temps, load, latency).</summary>
    public static Level Hi(double? v, double warn, double bad) =>
        v is not { } x ? Level.Dim : x >= bad ? Level.Bad : x >= warn ? Level.Warn : Level.Normal;

    /// <summary>Lower is worse (fps, signal).</summary>
    public static Level Lo(double? v, double warn, double bad) =>
        v is not { } x ? Level.Dim : x <= bad ? Level.Bad : x <= warn ? Level.Warn : Level.Normal;

    public static Level Worst(params Level[] levels) => levels.Length == 0 ? Level.Normal : levels.Max();

    /// <summary>"AMD Ryzen AI 9 365 w/ Radeon 880M" → "Ryzen AI 9 365".</summary>
    public static string Short(string name)
    {
        int w = name.IndexOf(" w/ ", StringComparison.Ordinal);
        if (w > 0) name = name[..w];
        foreach (var junk in new[] { "(TM)", "(R)", "(tm)", "(r)", "NVIDIA ", "GeForce ", "AMD ", "Intel ", " Processor", " Laptop GPU" })
            name = name.Replace(junk, junk == " Laptop GPU" ? " Laptop" : "", StringComparison.Ordinal);
        name = Spaces().Replace(name, " ").Trim();
        return name.Length > 30 ? name[..29] + "…" : name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
