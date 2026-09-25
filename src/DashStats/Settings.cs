using System.Text.Json;

namespace DashStats;

public sealed class Settings
{
    /// <summary>TL, TR, BL or BR.</summary>
    public string Corner { get; set; } = "BL";
    /// <summary>Distance from the corner, in physical pixels.</summary>
    public int MarginX { get; set; } = 16;
    public int MarginY { get; set; } = 16;
    /// <summary>Monitor device name; null means the primary monitor.</summary>
    public string? Screen { get; set; }

    /// <summary>Preset ids the user ticked at setup (see <see cref="Catalog.Presets"/>).</summary>
    public List<string> Uses { get; set; } = [];
    /// <summary>The ticked preset whose stats get the big spots.</summary>
    public string? MainUse { get; set; }
    /// <summary>hero, gauges, graphs or minimal.</summary>
    public string Look { get; set; } = "hero";
    /// <summary>Show the full list of every reading instead of the chosen look.</summary>
    public bool ShowAll { get; set; }

    public bool Lite { get; set; }
    public bool Overlay { get; set; }
    public bool StartWithWindows { get; set; }
    public bool FirstRunDone { get; set; }

    public int IntervalMs { get; set; } = 1000;
    public string PingHost { get; set; } = "1.1.1.1";

    /// <summary>Row keys the user pruned from the widget.</summary>
    public HashSet<string> Hidden { get; set; } = [];

    static string FilePath => Path.Combine(Setup.DataDir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) { Log.Write(e); }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Setup.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e) { Log.Write(e); }
    }
}

static class Log
{
    static readonly object Gate = new();

    public static void Write(object? what)
    {
        if (what is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Setup.DataDir);
                var path = Path.Combine(Setup.DataDir, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
