using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PersonalFinance.Wpf;

public class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 8);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(231, 233, 246)), 7), center, radius, radius);
        var percent = Math.Clamp(Value, 0, 100);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(128, 94, 255)), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (percent >= 100) dc.DrawEllipse(null, pen, center, radius, radius);
        else if (percent > 0)
        {
            var angle = percent / 100 * Math.PI * 2 - Math.PI / 2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                ctx.ArcTo(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)),
                    new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, pen, geometry);
        }
        var text = new FormattedText($"{Value:0}%", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            26, new SolidColorBrush(Color.FromRgb(27, 25, 41)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
