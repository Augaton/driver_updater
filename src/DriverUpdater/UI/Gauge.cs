using System.Windows;
using System.Windows.Media;

namespace DriverUpdater.UI;

/// <summary>
/// A ring gauge, three quarters of a circle, drawn directly: cyan while comfortable,
/// amber when busy, pink when saturated.
/// </summary>
public sealed class Gauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Gauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(Gauge), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Percentage, 0 to 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    const double Start = 135, Sweep = 270; // degrees, clockwise from the right

    protected override void OnRender(DrawingContext drawing)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = (size - Thickness) / 2;

        drawing.DrawGeometry(null, RoundPen(Brush("Edge", Brushes.DimGray)), Arc(center, radius, Sweep));
        double value = Math.Clamp(Value, 0, 100);
        if (value > 0.5)
        {
            var color = value >= 85 ? Brush("NeonPink", Brushes.DeepPink)
                : value >= 60 ? Brush("NeonAmber", Brushes.Orange)
                : Brush("NeonCyan", Brushes.Cyan);
            drawing.DrawGeometry(null, RoundPen(color), Arc(center, radius, Sweep * value / 100));
        }
    }

    Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    Pen RoundPen(Brush brush) => new(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    static Geometry Arc(Point center, double radius, double sweep)
    {
        Point At(double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(Start), isFilled: false, isClosed: false);
            context.ArcTo(At(Start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }
}
