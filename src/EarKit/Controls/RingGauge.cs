using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace EarKit.Controls;

/// Power ring
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int), typeof(RingGauge),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(RingGauge),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(nameof(Charging), typeof(bool), typeof(RingGauge),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SubBrushProperty = DependencyProperty.Register(nameof(SubBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), FrameworkPropertyMetadataOptions.AffectsRender));

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush SubBrush { get => (Brush)GetValue(SubBrushProperty); set => SetValue(SubBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    static readonly Brush Green = Frozen(Color.FromRgb(0x22, 0xC5, 0x5E));
    static readonly Brush Amber = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));
    static readonly Brush Red = Frozen(Color.FromRgb(0xEF, 0x44, 0x44));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public RingGauge() { Width = 120; Height = 140; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double d = Math.Min(w, h - 26), th = Math.Max(6, d * 0.085);
        var c = new Point(w / 2, d / 2);
        double r = d / 2 - th / 2 - 1;
        if (r <= 0) return;
        dc.DrawEllipse(null, new Pen(TrackBrush, th), c, r, r);
        bool valid = Value >= 0 && Value <= 100;
        if (valid && Value > 0)
        {
            var brush = Value <= 20 ? Red : Value <= 40 ? Amber : Green;
            var pen = new Pen(brush, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (Value >= 100) dc.DrawEllipse(null, pen, c, r, r);
            else
            {
                double a = Value / 100.0 * 2 * Math.PI;
                var p0 = new Point(c.X, c.Y - r);
                var p1 = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(p0, false, false);
                    ctx.ArcTo(p1, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var tfn = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        string main = valid ? Value + "%" : "--";
        var ft = new FormattedText(main, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, d * 0.22, TextBrush, dpi);
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2 - (Charging ? d * 0.06 : 0)));
        if (Charging)
        {
            var ch = new FormattedText("⚡ Charging", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tfn, d * 0.09, SubBrush, dpi);
            dc.DrawText(ch, new Point(c.X - ch.Width / 2, c.Y + d * 0.12));
        }
        var lt = new FormattedText(Label ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tfn, 13, SubBrush, dpi);
        dc.DrawText(lt, new Point(c.X - lt.Width / 2, d + 6));
    }
}