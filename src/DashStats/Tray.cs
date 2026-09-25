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
        _menu.Items.Add(_c.Win?.IsVisible == false ? "Show widget" : "Hide widget", null, (_, _) => _c.ToggleVisible())
             .Font = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Change what I monitor…", null, (_, _) => _c.OpenSetup());
        if (s.Uses.Count > 0) Check("Show every reading", null, s.ShowAll, _c.ToggleShowAll);
        Check("Lite view", "Ctrl+Alt+L", s.Lite, _c.ToggleLite);
        Check("Overlay on top", "Ctrl+Alt+M", s.Overlay, _c.ToggleOverlay);

        var pos = new Forms.ToolStripMenuItem("Position");
        foreach (var (code, name) in Controller.Corners)
            pos.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) => _c.SetCorner(code)) { Checked = s.Corner == code });
        _menu.Items.Add(pos);

        var restore = new Forms.ToolStripMenuItem($"Show hidden rows ({s.Hidden.Count})", null, (_, _) => _c.ShowAllRows())
            { Enabled = s.Hidden.Count > 0 };
        _menu.Items.Add(restore);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        Check("Start with Windows", null, s.StartWithWindows, _c.ToggleStartup);
        _menu.Items.Add("Open settings folder", null, (_, _) => _c.OpenDataFolder());
        _menu.Items.Add("Uninstall…", null, (_, _) => _c.Uninstall());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => _c.Exit());
    }

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
