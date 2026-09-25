using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DashStats;

/// <summary>Pick what this PC is used for and a look; the preview on the right is the real widget, fed live.</summary>
public partial class SetupWindow : Window
{
    sealed record Card(Preset Preset, Button Body, Border Box, TextBlock Check, Button Main);

    readonly Controller _ctl;
    readonly HashSet<string> _uses;
    string? _main;
    string _look;
    readonly List<Card> _cards = [];
    readonly List<(string Id, Button Btn)> _lookButtons = [];
    WidgetView? _view;

    public SetupWindow(Controller ctl)
    {
        _ctl = ctl;
        _uses = [.. ctl.S.Uses];
        _main = ctl.S.MainUse;
        _look = Catalog.Looks.Any(l => l.Id == ctl.S.Look) ? ctl.S.Look : "hero";
        InitializeComponent();

        foreach (var p in Catalog.Presets) Cards.Children.Add(BuildCard(p));
        foreach (var (id, name, desc) in Catalog.Looks)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0) });
            var btn = new Button { Style = (Style)FindResource("Flat"), Content = content, Margin = new Thickness(4), Padding = new Thickness(12, 10, 12, 10) };
            btn.Click += (_, _) => { _look = id; Refresh(); };
            Looks.Children.Add(btn);
            _lookButtons.Add((id, btn));
        }

        SourceInitialized += (_, _) => Native.DarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        _ctl.Ticked += OnTick;
        Closed += (_, _) => _ctl.Ticked -= OnTick;
        Refresh();
    }

    UIElement BuildCard(Preset p)
    {
        var check = new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Foreground = Ui.Solid("#0E1116"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var box = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), Child = check, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        var text = new StackPanel { Margin = new Thickness(0, 0, 86, 0) };
        text.Children.Add(new TextBlock { Text = p.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = p.Watches, FontSize = 12, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var inner = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        inner.Children.Add(box);
        inner.Children.Add(text);

        var body = new Button
        {
            Style = (Style)FindResource("Flat"), Content = inner, Padding = new Thickness(14),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 76,
        };
        var main = new Button
        {
            Style = (Style)FindResource("Flat"), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 0),
        };
        body.Click += (_, _) =>
        {
            if (!_uses.Remove(p.Id)) _uses.Add(p.Id);
            if (_main is null || !_uses.Contains(_main)) _main = Catalog.Presets.FirstOrDefault(x => _uses.Contains(x.Id))?.Id;
            Refresh();
        };
        main.Click += (_, _) => { _main = p.Id; Refresh(); };

        var grid = new Grid { Margin = new Thickness(5) };
        grid.Children.Add(body);
        grid.Children.Add(main);
        _cards.Add(new Card(p, body, box, check, main));
        return grid;
    }

    void Refresh()
    {
        foreach (var c in _cards)
        {
            bool on = _uses.Contains(c.Preset.Id), isMain = on && c.Preset.Id == _main;
            c.Body.Background = Ui.Solid(isMain ? "#14202E" : on ? "#161A21" : "#0E1116");
            c.Body.BorderBrush = Ui.Solid(isMain ? "#4EA8FF" : on ? "#3A414D" : "#1F242C");
            c.Box.Background = on ? Ui.Solid("#4EA8FF") : Brushes.Transparent;
            c.Box.BorderBrush = Ui.Solid(on ? "#4EA8FF" : "#5A6270");
            c.Check.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            c.Main.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            c.Main.Content = isMain ? "★ Main" : "Make main";
            c.Main.Background = isMain ? Ui.Solid("#4EA8FF") : Brushes.Transparent;
            c.Main.Foreground = isMain ? Ui.Solid("#0E1116") : Ui.Muted;
            c.Main.BorderBrush = Ui.Solid(isMain ? "#4EA8FF" : "#3A414D");
        }
        foreach (var (id, btn) in _lookButtons)
        {
            bool sel = id == _look;
            btn.Background = Ui.Solid(sel ? "#E8ECF1" : "#161A21");
            btn.Foreground = Ui.Solid(sel ? "#0E1116" : "#E8ECF1");
            btn.BorderBrush = Ui.Solid(sel ? "#E8ECF1" : "#262B33");
        }

        var chosen = Catalog.Presets.Where(p => _uses.Contains(p.Id)).ToList();
        var mainName = chosen.FirstOrDefault(p => p.Id == _main)?.Name;
        Summary.Text = chosen.Count == 0 ? "Pick at least one use"
            : $"{chosen.Count} picked · main: {mainName}";
        SaveBtn.IsEnabled = chosen.Count > 0;
        PreviewTitle.Text = "YOUR WIDGET · " + Catalog.Looks.First(l => l.Id == _look).Name.ToUpperInvariant();

        var (big, small) = Catalog.Compose(_uses, _main);
        if (big.Count == 0)
        {
            _view = null;
            PreviewHost.Child = new TextBlock { Text = "Pick at least one use on the left.", Foreground = Ui.Muted };
        }
        else
        {
            _view = WidgetView.Create(_look, big, small);
            PreviewHost.Child = _view.Root;
        }
        OnTick();
    }

    void OnTick()
    {
        _view?.Update(_ctl.Latest, _ctl.History);
        var (text, level) = _ctl.Status();
        PreviewStatus.Text = text;
        PreviewDot.Fill = Ui.ForLevel(level == Level.Normal ? Level.Good : level);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _ctl.ApplySetup(Catalog.Presets.Where(p => _uses.Contains(p.Id)).Select(p => p.Id), _main, _look);
        Close();
    }
}
