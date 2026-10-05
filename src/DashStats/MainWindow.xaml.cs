using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DashStats;

public partial class MainWindow : Window
{
    const int HotOverlay = 1, HotLite = 2, HotHide = 3;
    const double FullWidth = 370, LiteWidth = 210;
    const long StaleMs = 5000;

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xA3)));
    static readonly Dictionary<Level, Brush> LevelBrush = new()
    {
        [Level.Dim] = Freeze(new SolidColorBrush(Color.FromRgb(0x7D, 0x87, 0x96))),
        [Level.Normal] = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF1))),
        [Level.Good] = Freeze(new SolidColorBrush(Color.FromRgb(0x7E, 0xE2, 0xA8))),
        [Level.Warn] = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57))),
        [Level.Bad] = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))),
    };
    /// <summary>0..1 opacity of the widget's surfaces, from the transparency setting (desktop and pinned alike).</summary>
    double Surface => 1 - Math.Clamp(S.OverlayTransparency, 0, 100) / 100.0;

    sealed class SectionUi
    {
        public required string Name;
        public required StackPanel Panel;
        public required TextBlock Header;
        public required int Order;
    }

    sealed class RowUi
    {
        public required RowData Data;
        public required Grid Root;
        public required TextBlock Label, Value;
        public required SectionUi Section;
        public Sparkline? Spark;
        public long LastSeen;
    }

    readonly Controller _ctl;
    readonly Dictionary<string, SectionUi> _sections = new();
    readonly Dictionary<string, RowUi> _rows = new();
    readonly DispatcherTimer _topmost;
    IntPtr _hwnd;
    int _created;

    Settings S => _ctl.S;

    public MainWindow(Controller ctl)
    {
        _ctl = ctl;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        // Reposition after the resize has reached the native window, or right/bottom corners use the old size.
        SizeChanged += (_, _) => { Reposition(); Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);
        Deactivated += (_, _) => { if (!S.Overlay) SendToBottom(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);

        // Other "always on top" windows (taskbar, game launchers) can jump above us; reassert now and then.
        _topmost = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
        {
            if (S.Overlay && IsVisible)
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }, Dispatcher);

        Apply([new RowData("SYSTEM", "sys.starting", "Status", "starting sensors…", Level.Dim)]);
        Apply([new RowData(Sections.Lite, "lite.starting", "", "starting sensors…", Level.Dim, View: View.Lite)]);
        RebuildView();
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    // ---------- rows ----------

    public void Apply(List<RowData> data)
    {
        long now = Environment.TickCount64;
        foreach (var d in data)
        {
            if (!_rows.TryGetValue(d.Key, out var r))
            {
                r = CreateRow(d);
                _rows[d.Key] = r;
            }
            r.Data = d;
            r.LastSeen = now;
            if (r.Label.Text != d.Label) r.Label.Text = d.Label;
            if (r.Value.Text != d.Value) r.Value.Text = d.Value;
            r.Value.Foreground = LevelBrush[d.Level];
            if (d.Spark is { } v)
            {
                r.Spark ??= AddSpark(r);
                r.Spark.FixedMax = d.SparkMax;
                r.Spark.Push(v);
            }
        }
        UpdateVisibility();
    }

    SectionUi GetSection(string name)
    {
        if (_sections.TryGetValue(name, out var sec)) return sec;

        var accent = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(Sections.Accent(name))));
        var header = new TextBlock
        {
            Text = name,
            Foreground = accent,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var panel = new StackPanel();
        panel.Children.Add(header);
        sec = new SectionUi { Name = name, Panel = panel, Header = header, Order = Sections.Rank(name) * 1000 + _created++ };
        _sections[name] = sec;

        int at = 0;
        while (at < Body.Children.Count && ((SectionUi)((StackPanel)Body.Children[at]).Tag).Order < sec.Order) at++;
        panel.Tag = sec;
        Body.Children.Insert(at, panel);
        return sec;
    }

    RowUi CreateRow(RowData d)
    {
        var sec = GetSection(d.Section);
        bool lite = d.View == View.Lite;
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lite ? 42 : 84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Foreground = LabelBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var value = new TextBlock
        {
            FontFamily = Mono,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 0, 0),
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);

        var row = new RowUi { Data = d, Root = grid, Label = label, Value = value, Section = sec };
        grid.Tag = row;
        grid.Background = Brushes.Transparent; // so right-click hits the whole row
        sec.Panel.Children.Add(grid);
        return row;
    }

    Sparkline AddSpark(RowUi r)
    {
        var s = new Sparkline(r.Section.Header.Foreground) { Width = 64, Height = 14, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(s, 2);
        r.Root.Children.Add(s);
        return s;
    }

    public void UpdateVisibility()
    {
        long now = Environment.TickCount64;
        foreach (var r in _rows.Values)
        {
            bool vis = (r.Data.View == View.Lite) == S.Lite
                       && !S.Hidden.Contains(r.Data.Key)
                       && now - r.LastSeen < StaleMs;
            r.Root.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var sec in _sections.Values)
        {
            bool any = false;
            foreach (var child in sec.Panel.Children)
                if (child is Grid { Visibility: Visibility.Visible }) { any = true; break; }
            sec.Panel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            sec.Header.Visibility = S.Lite ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ---------- window mode & placement ----------

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        foreach (var (id, key) in new[] { (HotOverlay, 'M'), (HotLite, 'L'), (HotHide, 'H') })
            if (!Native.RegisterHotKey(_hwnd, id, mods, key))
                Log.Write($"Hotkey Ctrl+Alt+{key} is taken by another app");
        ApplyMode();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case HotOverlay: _ctl.ToggleOverlay(); break;
                case HotLite: _ctl.ToggleLite(); break;
                case HotHide: _ctl.ToggleVisible(); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Desktop mode: owned by the desktop window, kept at the bottom of the stack, draggable.
    /// Overlay mode: topmost and click-through, so it floats over games without stealing input.
    /// </summary>
    public void ApplyMode()
    {
        if (_hwnd == IntPtr.Zero) return;

        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TOOLWINDOW;
        ex &= ~Native.WS_EX_APPWINDOW;
        ex = S.Overlay ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        if (S.Overlay)
        {
            Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, IntPtr.Zero);
            Topmost = true;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _topmost.Start();
        }
        else
        {
            _topmost.Stop();
            Topmost = false;
            // Owning the window by the desktop keeps it visible through Win+D / "show desktop".
            Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, Native.GetShellWindow());
            SendToBottom();
        }

        Buttons.Visibility = S.Overlay ? Visibility.Collapsed : Visibility.Visible;
        Frame.Background = Freeze(new SolidColorBrush(Color.FromArgb((byte)(255 * Surface), 0x0E, 0x11, 0x16)));
        Frame.BorderBrush = Freeze(new SolidColorBrush(Color.FromArgb((byte)(0x26 * Surface), 0xFF, 0xFF, 0xFF)));
        bool list = ShowingList;
        Frame.Width = list ? (S.Lite ? LiteWidth : FullWidth) : (S.Lite ? LookLiteWidth : LookWidth);
        Body.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        LookHost.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        ViewBtn.Text = S.Lite ? "FULL" : "LITE";
        UpdateVisibility();
        Reposition();
    }

    // ---------- the chosen look ----------

    const double LookWidth = 400, LookLiteWidth = 380;
    WidgetView? _view;

    /// <summary>The raw list of every reading: when asked for, or before setup has picked anything.</summary>
    bool ShowingList => S.Uses.Count == 0;

    public void RebuildView()
    {
        var (big, small) = Catalog.Compose(S.Uses, S.MainUse);
        Ui.SurfaceOpacity = Surface;
        try { _view = WidgetView.Create(S.Look, big, S.Lite ? [] : small); }
        finally { Ui.SurfaceOpacity = 1; }
        LookHost.Child = _view.Root;
        ApplyMetrics();
    }

    public void ApplyMetrics()
    {
        _view?.Update(_ctl.Latest, _ctl.History);
        var (text, level) = _ctl.Status();
        StatusText.Text = text;
        StatusDot.Fill = level switch
        {
            Level.Bad => LevelBrush[Level.Bad],
            Level.Warn => LevelBrush[Level.Warn],
            Level.Dim => LevelBrush[Level.Dim],
            _ => LevelBrush[Level.Good],
        };
    }

    void SendToBottom()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    Forms.Screen TargetScreen() =>
        Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == S.Screen) ?? Forms.Screen.PrimaryScreen!;

    /// <summary>Anchors the window to its corner in physical pixels, so it grows away from the corner.</summary>
    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        var wa = TargetScreen().WorkingArea;
        // Size from our own layout (in pixels), not GetWindowRect, which can lag behind a SizeToContent resize.
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        if (w <= 0 || h <= 0)
        {
            Native.GetWindowRect(_hwnd, out var r);
            w = r.Right - r.Left;
            h = r.Bottom - r.Top;
        }
        int x = S.Corner.EndsWith('L') ? wa.Left + S.MarginX : wa.Right - w - S.MarginX;
        int y = S.Corner.StartsWith('T') ? wa.Top + S.MarginY : wa.Bottom - h - S.MarginY;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>After a drag, remember the nearest corner and the distance to it.</summary>
    void SnapToCorner()
    {
        Native.GetWindowRect(_hwnd, out var r);
        var screen = Forms.Screen.FromHandle(_hwnd);
        var wa = screen.WorkingArea;
        bool left = (r.Left + r.Right) / 2 < wa.Left + wa.Width / 2;
        bool top = (r.Top + r.Bottom) / 2 < wa.Top + wa.Height / 2;
        S.Corner = (top ? "T" : "B") + (left ? "L" : "R");
        S.MarginX = Math.Max(0, left ? r.Left - wa.Left : wa.Right - r.Right);
        S.MarginY = Math.Max(0, top ? r.Top - wa.Top : wa.Bottom - r.Bottom);
        S.Screen = screen.Primary ? null : screen.DeviceName;
        S.Save();
        Reposition();
    }

    // ---------- mouse ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (S.Overlay || e.Handled) return;
        try { DragMove(); } catch (InvalidOperationException) { }
        SnapToCorner();
        SendToBottom();
    }

    void ViewBtn_Down(object sender, MouseButtonEventArgs e) { e.Handled = true; _ctl.ToggleLite(); }
    void PinBtn_Down(object sender, MouseButtonEventArgs e) { e.Handled = true; _ctl.ToggleOverlay(); }
    void EditBtn_Down(object sender, MouseButtonEventArgs e) { e.Handled = true; _ctl.OpenSetup(); }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;

        var m = new ContextMenu();
        m.Items.Add(Item("Change what I monitor…", _ctl.OpenSetup));
        m.Items.Add(Item("Lite view", _ctl.ToggleLite, S.Lite, "Ctrl+Alt+L"));
        m.Items.Add(Item("Pin on top", _ctl.ToggleOverlay, S.Overlay, "Ctrl+Alt+M"));
        var see = new MenuItem { Header = "Transparency" };
        foreach (var t in Controller.TransparencySteps)
            see.Items.Add(Item(Controller.TransparencyName(t), () => _ctl.SetOverlayTransparency(t), S.OverlayTransparency == t));
        m.Items.Add(see);
        var pos = new MenuItem { Header = "Position" };
        foreach (var (code, cname) in Controller.Corners)
            pos.Items.Add(Item(cname, () => _ctl.SetCorner(code), S.Corner == code));
        m.Items.Add(pos);
        m.Items.Add(new Separator());
        m.Items.Add(Item($"Check for updates… (v{Updater.CurrentText})", _ctl.CheckForUpdatesNow));
        var settings = new MenuItem { Header = "Settings" };
        settings.Items.Add(Item("Start with Windows", _ctl.ToggleStartup, S.StartWithWindows));
        settings.Items.Add(Item("Check for updates automatically", _ctl.ToggleAutoUpdate, S.CheckForUpdates));
        settings.Items.Add(Item("Open settings folder", _ctl.OpenDataFolder));
        settings.Items.Add(new Separator());
        settings.Items.Add(Item("Uninstall…", _ctl.Uninstall));
        m.Items.Add(settings);
        m.Items.Add(new Separator());
        m.Items.Add(Item("Hide widget", _ctl.ToggleVisible, null, "Ctrl+Alt+H"));
        m.Items.Add(Item("Exit", _ctl.Exit));

        m.PlacementTarget = this;
        m.IsOpen = true;
    }

    static MenuItem Item(string text, Action act, bool? check = null, string? keys = null)
    {
        var mi = new MenuItem { Header = text, InputGestureText = keys ?? "" };
        if (check is { } c) mi.IsChecked = c;
        mi.Click += (_, _) => act();
        return mi;
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var id in new[] { HotOverlay, HotLite, HotHide }) Native.UnregisterHotKey(_hwnd, id);
        base.OnClosed(e);
    }
}
