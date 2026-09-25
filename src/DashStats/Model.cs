namespace DashStats;

public enum Level { Dim, Normal, Good, Warn, Bad }

public enum View { Full, Lite }

/// <summary>One line on the widget. Collectors emit these every tick; the window renders them.</summary>
/// <param name="Section">Group heading; its first word picks order and accent colour (see <see cref="Sections"/>).</param>
/// <param name="Key">Stable id, used to remember rows the user hid.</param>
/// <param name="Spark">If set, pushed into the row's sparkline.</param>
/// <param name="SparkMax">Fixed sparkline ceiling; 0 means auto-scale.</param>
public sealed record RowData(
    string Section, string Key, string Label, string Value,
    Level Level = Level.Normal, double? Spark = null, double SparkMax = 0, View View = View.Full);

public static class Sections
{
    public const string Lite = "LITE";

    static readonly (string Prefix, string Color)[] Order =
    [
        ("LITE", "#E8ECF1"),
        ("FPS", "#FFD166"),
        ("CPU", "#4EA8FF"),
        ("GPU", "#5BD68A"),
        ("MEMORY", "#C792EA"),
        ("STORAGE", "#FF9F6E"),
        ("NETWORK", "#45D0E0"),
        ("BATTERY", "#A3E635"),
        ("SYSTEM", "#9AA4B2"),
    ];

    public static int Rank(string section)
    {
        for (int i = 0; i < Order.Length; i++)
            if (section.StartsWith(Order[i].Prefix, StringComparison.Ordinal)) return i;
        return Order.Length;
    }

    public static string Accent(string section)
    {
        int r = Rank(section);
        return r < Order.Length ? Order[r].Color : "#9AA4B2";
    }
}
