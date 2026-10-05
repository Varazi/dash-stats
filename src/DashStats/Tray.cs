using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DashStats;

sealed class Tray : IDisposable
{
    readonly Controller _c;
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ContextMenuStrip _menu = new();
    readonly IntPtr _hicon;

    public Tray(Controller c)
    {
        _c = c;
        _menu.Opening += (_, _) => Build();
        Build();

        using var bmp = DrawIcon();
        _hicon = bmp.GetHicon();
        _icon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.FromHandle(_hicon),
            Text = "DashStats",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) _c.ToggleVisible(); };
    }

    void Build()
    {
        var s = _c.S;
        _menu.Items.Clear();
        var bold = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold);
        // Pinned, the widget ignores clicks, so the way back has to be the first thing here.
        if (s.Overlay)
            _menu.Items.Add(new Forms.ToolStripMenuItem("Unpin widget", null, (_, _) => _c.ToggleOverlay())
                { Font = bold, ShortcutKeyDisplayString = "Ctrl+Alt+M" });
        var hide = _menu.Items.Add(_c.Win?.IsVisible == false ? "Show widget" : "Hide widget", null, (_, _) => _c.ToggleVisible());
        if (!s.Overlay) hide.Font = bold;
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Change what I monitor…", null, (_, _) => _c.OpenSetup());
        Check("Lite view", "Ctrl+Alt+L", s.Lite, _c.ToggleLite);
        if (!s.Overlay) Check("Pin on top", "Ctrl+Alt+M", false, _c.ToggleOverlay);

        var see = new Forms.ToolStripMenuItem("Overlay transparency");
        foreach (var t in Controller.TransparencySteps)
            see.DropDownItems.Add(new Forms.ToolStripMenuItem(Controller.TransparencyName(t), null, (_, _) => _c.SetOverlayTransparency(t))
                { Checked = s.OverlayTransparency == t });
        _menu.Items.Add(see);

        var pos = new Forms.ToolStripMenuItem("Position");
        foreach (var (code, name) in Controller.Corners)
            pos.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) => _c.SetCorner(code)) { Checked = s.Corner == code });
        _menu.Items.Add(pos);

        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add($"Check for updates… (v{Updater.CurrentText})", null, (_, _) => _c.CheckForUpdatesNow());

        // Set once, rarely touched.
        var settings = new Forms.ToolStripMenuItem("Settings");
        settings.DropDownItems.Add(new Forms.ToolStripMenuItem("Start with Windows", null, (_, _) => _c.ToggleStartup()) { Checked = s.StartWithWindows });
        settings.DropDownItems.Add(new Forms.ToolStripMenuItem("Check for updates automatically", null, (_, _) => _c.ToggleAutoUpdate()) { Checked = s.CheckForUpdates });
        settings.DropDownItems.Add(new Forms.ToolStripMenuItem("Open settings folder", null, (_, _) => _c.OpenDataFolder()));
        settings.DropDownItems.Add(new Forms.ToolStripSeparator());
        settings.DropDownItems.Add(new Forms.ToolStripMenuItem("Uninstall…", null, (_, _) => _c.Uninstall()));
        _menu.Items.Add(settings);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => _c.Exit());
    }

    /// <summary>A Windows notification from the tray icon.</summary>
    public void Tip(string title, string text) => _icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);

    void Check(string text, string? keys, bool on, Action act) =>
        _menu.Items.Add(new Forms.ToolStripMenuItem(text, null, (_, _) => act()) { Checked = on, ShortcutKeyDisplayString = keys });

    /// <summary>A tiny bar-chart glyph so we don't ship an .ico.</summary>
    static Drawing.Bitmap DrawIcon()
    {
        var bmp = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 20, 24, 30)))
            g.FillEllipse(bg, 0, 0, 31, 31);
        (int h, Drawing.Color c)[] bars =
        [
            (10, Drawing.Color.FromArgb(78, 168, 255)),
            (18, Drawing.Color.FromArgb(91, 214, 138)),
            (14, Drawing.Color.FromArgb(255, 209, 102)),
        ];
        for (int i = 0; i < bars.Length; i++)
        {
            using var b = new Drawing.SolidBrush(bars[i].c);
            g.FillRectangle(b, 8 + i * 6, 24 - bars[i].h, 4, bars[i].h);
        }
        return bmp;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        Native.DestroyIcon(_hicon);
    }
}
