using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace EarKit.Controls;

/// EQ curve preview (Catmull-Rom smoothing)
public sealed class EqCurve : FrameworkElement
{
    double[] _vals = new double[10];
    double _max = 6;
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(EqCurve),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(EqCurve),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(EqCurve),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    /// Set the dB values and range for each frequency band.
    public void SetValues(double[] db, double maxAbs) { _vals = db.ToArray(); _max = Math.Max(1, maxAbs); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20 || _vals.Length < 2) return;
        double padL = 40, padR = 12, padT = 10, padB = 10;
        double iw = w - padL - padR, ih = h - padT - padB;
        var gp = new Pen(GridBrush, 1);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tf = new Typeface("Segoe UI");
        foreach (var lv in new[] { _max, _max / 2, 0, -_max / 2, -_max })
        {
            double y = padT + ih / 2 - lv / _max * ih / 2;
            dc.DrawLine(gp, new Point(padL, y), new Point(w - padR, y));
            var ft = new FormattedText((lv > 0 ? "+" : "") + lv.ToString("0.#") + " dB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, TextBrush, dpi);
            dc.DrawText(ft, new Point(padL - ft.Width - 6, y - ft.Height / 2));
        }
        int n = _vals.Length;
        var pts = new Point[n];
        for (int i = 0; i < n; i++)
            pts[i] = new Point(padL + iw * (i + 0.5) / n, padT + ih / 2 - Math.Clamp(_vals[i], -_max, _max) / _max * ih / 2);

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        void Path(StreamGeometryContext ctx)
        {
            for (int i = 0; i < n - 1; i++)
            {
                var p0 = i > 0 ? pts[i - 1] : pts[i];
                var p1 = pts[i]; var p2 = pts[i + 1];
                var p3 = i + 2 < n ? pts[i + 2] : p2;
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, true, true);
            }
        }
        using (var ctx = line.Open()) { ctx.BeginFigure(pts[0], false, false); Path(ctx); }
        double zero = padT + ih / 2;
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, zero), true, true);
            ctx.LineTo(pts[0], false, false);
            Path(ctx);
            ctx.LineTo(new Point(pts[n - 1].X, zero), false, false);
        }
        line.Freeze(); fill.Freeze();
        Brush fb = Brushes.Transparent;
        if (Accent is SolidColorBrush sb)
        {
            var c = sb.Color;
            var lg = new LinearGradientBrush(Color.FromArgb(90, c.R, c.G, c.B), Color.FromArgb(10, c.R, c.G, c.B), 90);
            lg.Freeze(); fb = lg;
        }
        dc.DrawGeometry(fb, null, fill);
        dc.DrawGeometry(null, new Pen(Accent, 2.5) { LineJoin = PenLineJoin.Round }, line);
        foreach (var p in pts) dc.DrawEllipse(Accent, null, p, 3.5, 3.5);
    }
}