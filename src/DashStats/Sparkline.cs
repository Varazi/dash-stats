using System.Windows;
using System.Windows.Media;

namespace DashStats;

/// <summary>A cheap 60-sample line chart drawn directly in OnRender.</summary>
sealed class Sparkline : FrameworkElement
{
    const int Length = 60;
    readonly double[] _v = new double[Length];
    int _n, _i;
    readonly Pen _pen;
    readonly Brush? _fill;

    public double FixedMax { get; set; }

    public Sparkline(Brush stroke, bool fill = false, double thickness = 1.2)
    {
        _pen = new Pen(stroke, thickness) { LineJoin = PenLineJoin.Round };
        _pen.Freeze();
        if (fill && stroke is SolidColorBrush s)
        {
            _fill = new SolidColorBrush(s.Color) { Opacity = 0.18 };
            _fill.Freeze();
        }
    }

    public void Push(double v)
    {
        _v[_i] = double.IsFinite(v) ? Math.Max(0, v) : 0;
        _i = (_i + 1) % Length;
        if (_n < Length) _n++;
        if (IsVisible) InvalidateVisual();
    }

    /// <summary>Replaces the contents with a history (oldest first).</summary>
    public void Set(IReadOnlyList<double> values)
    {
        _n = _i = 0;
        foreach (var v in values.Skip(Math.Max(0, values.Count - Length)))
        {
            _v[_i] = double.IsFinite(v) ? Math.Max(0, v) : 0;
            _i = (_i + 1) % Length;
            _n++;
        }
        InvalidateVisual();
    }

    double At(int k) => _v[(_i - _n + k + Length) % Length];

    protected override void OnRender(DrawingContext dc)
    {
        if (_n < 2) return;
        double w = ActualWidth, h = ActualHeight - 2;
        double max = FixedMax > 0 ? FixedMax : 0;
        if (max <= 0)
        {
            for (int k = 0; k < _n; k++) max = Math.Max(max, At(k));
            max = Math.Max(max * 1.15, 1e-9);
        }
        double step = w / (Length - 1);
        Point P(int k) => new(w - (_n - 1 - k) * step, 1 + h - Math.Min(1, At(k) / max) * h);

        if (_fill is not null)
        {
            var area = new StreamGeometry();
            using (var c = area.Open())
            {
                c.BeginFigure(new Point(P(0).X, h + 1), true, true);
                for (int k = 0; k < _n; k++) c.LineTo(P(k), false, false);
                c.LineTo(new Point(w, h + 1), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(_fill, null, area);
        }

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(P(0), false, false);
            for (int k = 1; k < _n; k++) c.LineTo(P(k), true, false);
        }
        line.Freeze();
        dc.DrawGeometry(null, _pen, line);
    }
}
