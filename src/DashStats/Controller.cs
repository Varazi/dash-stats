using System.Diagnostics;
using System.Windows;

namespace DashStats;

/// <summary>Every user action lives here so the widget menu, tray menu and hotkeys behave identically.</summary>
public sealed class Controller(Settings s)
{
    public Settings S { get; } = s;
    public MainWindow Win { get; set; } = null!;

    public static readonly (string Code, string Name)[] Corners =
        [("TL", "Top left"), ("TR", "Top right"), ("BL", "Bottom left"), ("BR", "Bottom right")];

    // ---------- live data ----------

    public MetricHistory History { get; } = new();
    public IReadOnlyDictionary<string, Metric> Latest { get; private set; } = new Dictionary<string, Metric>();

    /// <summary>Raised on the UI thread after each tick, so open windows (setup preview) can refresh.</summary>
    public event Action? Ticked;

    public void OnTick(List<RowData> rows, Dictionary<string, Metric> metrics)
    {
        Latest = metrics;
        History.Push(metrics);
        Win.Apply(rows);
        Win.ApplyMetrics();
        Ticked?.Invoke();
    }

    /// <summary>One-line verdict for the widget header: the network diagnosis first, then the worst stat.</summary>
    public (string Text, Level Level) Status()
    {
        if (Latest.TryGetValue("net", out var net) && net.Level is Level.Bad or Level.Warn)
            return ("Network: " + net.Sub, net.Level);
        var worst = Latest
            .Where(kv => kv.Key != "net" && kv.Value.Level is Level.Bad or Level.Warn)
            .OrderByDescending(kv => kv.Value.Level)
            .Select(kv => (KeyValuePair<string, Metric>?)kv)
            .FirstOrDefault();
        if (worst is { } w)
        {
            // Say why, not just the number: "Ping: jitter 34 · 0% loss" beats an amber "Ping 16 ms".
            var x = w.Value;
            string why = w.Key == "ping" ? x.Sub : Fmt.WithUnit(x.Value, x.Unit);
            return ($"{Ui.Label(w.Key)}: {why}", x.Level);
        }
        return (Latest.Count == 0 ? "Starting…" : "Healthy", Latest.Count == 0 ? Level.Dim : Level.Good);
    }

    // ---------- setup ----------

    SetupWindow? _setup;

    public void OpenSetup()
    {
        if (_setup is { IsVisible: true }) { _setup.Activate(); return; }
        _setup = new SetupWindow(this);
        _setup.Closed += (_, _) => _setup = null;
        // Windows won't let a background app steal focus, so start it on top once to make sure it's seen.
        _setup.Topmost = true;
        _setup.ContentRendered += (_, _) => { _setup.Topmost = false; _setup.Activate(); };
        _setup.Show();
        _setup.Activate();
    }

    public void ApplySetup(IEnumerable<string> uses, string? main, string look)
    {
        S.Uses = uses.ToList();
        S.MainUse = main;
        S.Look = look;
        S.ShowAll = false;
        S.Save();
        Win.RebuildView();
        Win.ApplyMode();
    }

    // ---------- view toggles ----------

    public void ToggleLite()
    {
        S.Lite = !S.Lite;
        S.Save();
        Win.RebuildView();
        Win.ApplyMode();
    }

    public void ToggleShowAll()
    {
        S.ShowAll = !S.ShowAll;
        S.Save();
        Win.ApplyMode();
    }

    public void ToggleOverlay()
    {
        S.Overlay = !S.Overlay;
        S.Save();
        if (!Win.IsVisible) Win.Show();
        Win.ApplyMode();
    }

    public void ToggleVisible()
    {
        if (Win.IsVisible) Win.Hide();
        else ShowWidget();
    }

    public void ShowWidget()
    {
        Win.Show();
        Win.ApplyMode();
    }

    public void SetCorner(string corner)
    {
        S.Corner = corner;
        S.MarginX = S.MarginY = 16;
        S.Save();
        Win.Reposition();
    }

    public void HideRow(string key)
    {
        S.Hidden.Add(key);
        S.Save();
        Win.UpdateVisibility();
    }

    public void ShowAllRows()
    {
        S.Hidden.Clear();
        S.Save();
        Win.UpdateVisibility();
    }

    public void ToggleStartup()
    {
        S.StartWithWindows = Setup.SetStartup(!S.StartWithWindows);
        S.Save();
    }

    public void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo(Setup.DataDir) { UseShellExecute = true });

    public void Uninstall()
    {
        if (Setup.Uninstall()) Exit(save: false);
    }

    public void Exit() => Exit(save: true);

    void Exit(bool save)
    {
        if (save) S.Save();
        Application.Current.Shutdown();
    }
}
