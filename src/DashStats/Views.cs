using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace DashStats;

/// <summary>Colours, fonts and small builders shared by every look.</summary>
static class Ui
{
    public static readonly FontFamily Numbers = new("Bahnschrift SemiBold Condensed, Bahnschrift, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    // Must be initialised before the brushes below, which go through Solid().
    static readonly Dictionary<string, Brush> Cache = new();

    public static readonly Brush Text = Solid("#E8ECF1");
    public static readonly Brush Muted = Solid("#8B94A3");
    public static readonly Brush Faint = Solid("#7D8796");
    public static readonly Brush Tile = Solid("#161A21");
    public static readonly Brush Panel = Solid("#13171D");
    public static readonly Brush Track = Solid("#1F242C");
    public static readonly Brush Line = Solid("#1F242C");

    public static Brush Solid(string hex)
    {
        if (Cache.TryGetValue(hex, out var b)) return b;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return Cache[hex] = brush;
    }

    public static Brush Accent(string id) => Solid(Catalog.Metrics.TryGetValue(id, out var i) ? i.Color : "#9AA4B2");
    public static string Label(string id) => Catalog.Metrics.TryGetValue(id, out var i) ? i.Label : id;

    public static Brush ForLevel(Level l) => l switch
    {
        Level.Warn => Solid("#FFC857"),
        Level.Bad => Solid("#FF6B6B"),
        Level.Good => Solid("#7EE2A8"),
        Level.Dim => Faint,
        _ => Text,
    };

    public static TextBlock T(double size, Brush? fg = null, FontWeight? weight = null, FontFamily? font = null, string text = "") =>
        new()
        {
            Text = text,
            FontSize = size,
            Foreground = fg ?? Text,
            FontWeight = weight ?? FontWeights.Normal,
            FontFamily = font ?? SystemFonts.MessageFontFamily,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    public static TextBlock Caps(string text, Brush fg) =>
        T(11, fg, FontWeights.SemiBold, text: text.ToUpperInvariant());

    public static Border Divider() => new() { Height = 1, Background = Line, Margin = new Thickness(0, 4, 0, 4) };
}

/// <summary>A look (Hero, Gauges, Graphs, Minimal): built once for a set of stats, then fed new values every tick.</summary>
abstract class WidgetView
{
    public FrameworkElement Root { get; protected set; } = null!;
    public abstract void Update(IReadOnlyDictionary<string, Metric> m, MetricHistory history);

    public static WidgetView Create(string look, IReadOnlyList<string> big, IReadOnlyList<string> small) => look switch
    {
        "gauges" => new GaugesView(big, small),
        "graphs" => new GraphsView(big, small),
        "minimal" => new MinimalView(big, small),
        _ => new HeroView(big, small),
    };

    protected static Metric Get(IReadOnlyDictionary<string, Metric> m, string id) =>
        m.TryGetValue(id, out var x) ? x : new Metric(Fmt.None, "", "waiting for data…", Level: Level.Dim);

    protected static Level Shown(Metric x) => x.Level == Level.Good ? Level.Normal : x.Level;

    /// <summary>"label ······ value unit · sub" rows for the secondary stats, shared by several looks.</summary>
    protected sealed class SmallRows
    {
        readonly Dictionary<string, (TextBlock Value, TextBlock Sub)> _rows = new();
        public readonly StackPanel Panel = new();

        public SmallRows(IReadOnlyList<string> ids, bool withSub = true)
        {
            foreach (var id in ids)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(Ui.T(12, Ui.Accent(id), text: Ui.Label(id)));
                var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var value = Ui.T(12, font: Ui.Mono);
                var sub = Ui.T(11.5, Ui.Faint, font: Ui.Mono);
                right.Children.Add(value);
                if (withSub) right.Children.Add(sub);
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
                Panel.Children.Add(g);
                _rows[id] = (value, sub);
            }
        }

        public void Update(IReadOnlyDictionary<string, Metric> m)
        {
            foreach (var (id, (value, sub)) in _rows)
            {
                var x = Get(m, id);
                value.Text = Fmt.WithUnit(x.Value, x.Unit);
                value.Foreground = Ui.ForLevel(Shown(x));
                sub.Text = string.IsNullOrEmpty(x.Sub) ? "" : $" · {x.Sub}";
            }
        }
    }
}

/// <summary>Big bold numbers in tiles.</summary>
sealed class HeroView : WidgetView
{
    readonly Dictionary<string, (TextBlock Value, TextBlock Unit, TextBlock Sub)> _tiles = new();
    readonly SmallRows? _small;

    public HeroView(IReadOnlyList<string> big, IReadOnlyList<string> small)
    {
        var root = new StackPanel();
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var id in big)
        {
            var value = Ui.T(44, font: Ui.Numbers);
            value.LineHeight = 44;
            value.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            var unit = Ui.T(12, Ui.Muted);
            unit.Margin = new Thickness(4, 0, 0, 4);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
            row.Children.Add(value);
            row.Children.Add(unit);
            var sub = Ui.T(11, Ui.Muted, font: Ui.Mono);

            var stack = new StackPanel();
            stack.Children.Add(Ui.Caps(Ui.Label(id), Ui.Accent(id)));
            stack.Children.Add(row);
            stack.Children.Add(sub);
            grid.Children.Add(new Border
            {
                Background = Ui.Tile, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(4), Child = stack,
            });
            _tiles[id] = (value, unit, sub);
        }
        root.Children.Add(grid);

        if (small.Count > 0)
        {
            _small = new SmallRows(small);
            root.Children.Add(Ui.Divider());
            root.Children.Add(_small.Panel);
        }
        Root = root;
    }

    public override void Update(IReadOnlyDictionary<string, Metric> m, MetricHistory history)
    {
        foreach (var (id, (value, unit, sub)) in _tiles)
        {
            var x = Get(m, id);
            value.Text = x.Value;
            value.Foreground = Ui.ForLevel(Shown(x));
            unit.Text = x.Unit;
            sub.Text = x.Sub;
        }
        _small?.Update(m);
    }
}

/// <summary>Ring dials for the main stats, bars for the rest.</summary>
sealed class GaugesView : WidgetView
{
    const double Size = 104, Thick = 9;
    readonly Dictionary<string, (Path Arc, TextBlock Value, TextBlock Unit, TextBlock Sub)> _rings = new();
    readonly Dictionary<string, (Border Fill, TextBlock Value)> _bars = new();

    public GaugesView(IReadOnlyList<string> big, IReadOnlyList<string> small)
    {
        var root = new StackPanel();
        var grid = new UniformGrid { Columns = 2 };
        foreach (var id in big)
        {
            var dial = new Grid { Width = Size, Height = Size };
            dial.Children.Add(new Ellipse { Stroke = Ui.Track, StrokeThickness = Thick, Margin = new Thickness(Thick / 2) });
            var arc = new Path { Stroke = Ui.Accent(id), StrokeThickness = Thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            dial.Children.Add(arc);
            var value = Ui.T(26, font: Ui.Numbers);
            var unit = Ui.T(10, Ui.Muted);
            value.HorizontalAlignment = unit.HorizontalAlignment = HorizontalAlignment.Center;
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            center.Children.Add(value);
            center.Children.Add(unit);
            dial.Children.Add(center);

            var label = Ui.Caps(Ui.Label(id), Ui.Accent(id));
            var sub = Ui.T(11, Ui.Muted);
            label.HorizontalAlignment = sub.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 6, 0, 1);

            var cell = new StackPanel { Margin = new Thickness(4, 4, 4, 10) };
            cell.Children.Add(dial);
            cell.Children.Add(label);
            cell.Children.Add(sub);
            grid.Children.Add(cell);
            _rings[id] = (arc, value, unit, sub);
        }
        root.Children.Add(grid);

        if (small.Count > 0)
        {
            root.Children.Add(Ui.Divider());
            foreach (var id in small)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.Children.Add(Ui.T(12, Ui.Muted, text: Ui.Label(id)));
                var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Ui.Track, VerticalAlignment = VerticalAlignment.Center };
                var fill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Ui.Accent(id), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                var bar = new Grid();
                bar.Children.Add(track);
                bar.Children.Add(fill);
                Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                var value = Ui.T(12, font: Ui.Mono);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(value, 2);
                g.Children.Add(value);
                root.Children.Add(g);
                _bars[id] = (fill, value);
                bar.SizeChanged += (_, _) => fill.Tag = bar.ActualWidth;
            }
        }
        Root = root;
    }

    static Geometry Arc(double pct)
    {
        double r = (Size - Thick) / 2, c = Size / 2;
        pct = Math.Clamp(pct, 0, 99.9);
        if (pct <= 0.5) return Geometry.Empty;
        double a = pct / 100 * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    public override void Update(IReadOnlyDictionary<string, Metric> m, MetricHistory history)
    {
        foreach (var (id, (arc, value, unit, sub)) in _rings)
        {
            var x = Get(m, id);
            arc.Data = Arc(x.Pct ?? 0);
            arc.Stroke = x.Level is Level.Warn or Level.Bad ? Ui.ForLevel(x.Level) : Ui.Accent(id);
            value.Text = x.Value;
            unit.Text = x.Unit;
            sub.Text = x.Sub;
        }
        foreach (var (id, (fill, value)) in _bars)
        {
            var x = Get(m, id);
            double width = fill.Tag is double w ? w : 0;
            fill.Width = width * Math.Clamp((x.Pct ?? 0) / 100, 0, 1);
            value.Text = Fmt.WithUnit(x.Value, x.Unit);
            value.Foreground = Ui.ForLevel(Shown(x));
        }
    }
}

/// <summary>A live history line under each main stat.</summary>
sealed class GraphsView : WidgetView
{
    readonly Dictionary<string, (TextBlock Value, TextBlock Unit, TextBlock Sub, Sparkline Spark)> _panels = new();
    readonly SmallRows? _small;

    public GraphsView(IReadOnlyList<string> big, IReadOnlyList<string> small)
    {
        var root = new StackPanel();
        foreach (var id in big)
        {
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Ui.Caps(Ui.Label(id), Ui.Accent(id)));
            var sub = Ui.T(11, Ui.Muted);
            left.Children.Add(sub);
            head.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            var value = Ui.T(30, font: Ui.Numbers);
            var unit = Ui.T(12, Ui.Muted);
            unit.Margin = new Thickness(4, 0, 0, 4);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            right.Children.Add(value);
            right.Children.Add(unit);
            Grid.SetColumn(right, 1);
            head.Children.Add(right);

            var spark = new Sparkline(Ui.Accent(id), fill: true, thickness: 1.5) { Height = 36, Margin = new Thickness(0, 6, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(spark);
            root.Children.Add(new Border
            {
                Background = Ui.Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 8),
                Margin = new Thickness(0, 0, 0, 6), Child = stack,
            });
            _panels[id] = (value, unit, sub, spark);
        }

        if (small.Count > 0)
        {
            _small = new SmallRows(small, withSub: false);
            _small.Panel.Margin = new Thickness(4, 2, 4, 0);
            root.Children.Add(_small.Panel);
        }
        Root = root;
    }

    public override void Update(IReadOnlyDictionary<string, Metric> m, MetricHistory history)
    {
        foreach (var (id, (value, unit, sub, spark)) in _panels)
        {
            var x = Get(m, id);
            value.Text = x.Value;
            value.Foreground = Ui.ForLevel(Shown(x));
            unit.Text = x.Unit;
            sub.Text = x.Sub;
            spark.FixedMax = x.Raw is null && x.Pct is not null ? 100 : 0;
            spark.Set(history.Get(id));
        }
        _small?.Update(m);
    }
}

/// <summary>Quiet: one strip of main numbers, the rest as small dim text.</summary>
sealed class MinimalView : WidgetView
{
    readonly Dictionary<string, (TextBlock Value, TextBlock Unit)> _cells = new();
    readonly Dictionary<string, TextBlock> _small = new();

    public MinimalView(IReadOnlyList<string> big, IReadOnlyList<string> small)
    {
        var root = new StackPanel();
        var strip = new UniformGrid { Columns = Math.Max(1, big.Count), Margin = new Thickness(0, 2, 0, 2) };
        foreach (var id in big)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var value = Ui.T(20, font: Ui.Mono, weight: FontWeights.SemiBold);
            var unit = Ui.T(11, Ui.Muted, font: Ui.Mono);
            unit.Margin = new Thickness(3, 0, 0, 3);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            line.Children.Add(value);
            line.Children.Add(unit);
            var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            cell.Children.Add(line);
            cell.Children.Add(Ui.T(10, Ui.Accent(id), text: Ui.Label(id)));
            strip.Children.Add(cell);
            _cells[id] = (value, unit);
        }
        root.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = strip,
        });

        if (small.Count > 0)
        {
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var id in small)
            {
                var g = new Grid { Margin = new Thickness(0, 1, 14, 1) };
                g.Children.Add(Ui.T(11, Ui.Faint, font: Ui.Mono, text: Ui.Label(id)));
                var v = Ui.T(11, Ui.Solid("#B8C0CC"), font: Ui.Mono);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                g.Children.Add(v);
                grid.Children.Add(g);
                _small[id] = v;
            }
            root.Children.Add(grid);
        }
        Root = root;
    }

    public override void Update(IReadOnlyDictionary<string, Metric> m, MetricHistory history)
    {
        foreach (var (id, (value, unit)) in _cells)
        {
            var x = Get(m, id);
            value.Text = x.Value;
            value.Foreground = Ui.ForLevel(Shown(x));
            unit.Text = x.Unit;
        }
        foreach (var (id, v) in _small)
        {
            var x = Get(m, id);
            v.Text = Fmt.WithUnit(x.Value, x.Unit);
            v.Foreground = x.Level is Level.Warn or Level.Bad ? Ui.ForLevel(x.Level) : Ui.Solid("#B8C0CC");
        }
    }
}
