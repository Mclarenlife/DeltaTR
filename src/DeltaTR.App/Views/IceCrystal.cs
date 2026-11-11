using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeltaTR.App.Views;

/// <summary>A hollow, three-sided prism. The inner rim and outer tips deform independently.</summary>
public sealed class IceCrystal : FrameworkElement
{
    public static readonly DependencyProperty ListeningProperty = DependencyProperty.Register(
        nameof(Listening), typeof(bool), typeof(IceCrystal), new PropertyMetadata(false));
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(int), typeof(IceCrystal), new PropertyMetadata(0));
    public bool Listening { get => (bool)GetValue(ListeningProperty); set => SetValue(ListeningProperty, value); }
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private static readonly Brush[] Body = [
        Gradient("#D8EBFF", "#536CAC", "#BFA3FF"), Gradient("#DAD9FF", "#354C93", "#53DDFF"), Gradient("#9BFBFF", "#2069BD", "#EBEFFF")
    ];
    private static readonly Brush[] Ridge = [
        Gradient("#E8EFFF", "#83ACFF", "#7454F5"), Gradient("#BCA5FF", "#516FA9", "#19355B"), Gradient("#3DDFFF", "#0F3A80", "#819EC6")
    ];
    private static readonly Brush[] Shadow = [
        Gradient("#546691", "#172D55", "#5B58BC"), Gradient("#7C8BC8", "#101E34", "#40537A"), Gradient("#68ACDB", "#2C4777", "#35405A")
    ];
    private static readonly Pen OuterEdge = Line("#C1DAFE", 0.8);
    private static readonly Pen InnerEdge = Line("#CBEAFF", 1.0);
    private static readonly Pen Fold = Line("#77D1F2FF", .65);
    private static readonly Pen[] Halo = [Line("#074B7AF8", 13), Line("#125D8FFF", 6), Line("#327EA4FF", 2.5)];
    private readonly DispatcherTimer clock = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private long previous;
    private double phase, energy;
    public IceCrystal()
    {
        clock.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = Math.Min(0.12, Stopwatch.GetElapsedTime(previous, now).TotalSeconds);
            previous = now;
            var target = Listening ? 0.62 + 0.38 * Math.Sqrt(Math.Clamp(Level, 0, 100) / 100d) : 0;
            energy += (target - energy) * (1 - Math.Exp(-elapsed * 9));
            phase += elapsed * (1.05 + energy * 3.9);
            InvalidateVisual();
        };
        Loaded += (_, _) => UpdateClock();
        Unloaded += (_, _) => clock.Stop();
        IsVisibleChanged += (_, _) => UpdateClock();
    }

    private void UpdateClock()
    {
        if (!IsLoaded || !IsVisible) { clock.Stop(); return; }
        previous = Stopwatch.GetTimestamp();
        clock.Start();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 1) return;
        dc.PushTransform(new TranslateTransform((ActualWidth-size)/2, (ActualHeight-size)/2));
        dc.PushTransform(new ScaleTransform(size/200, size/200));
        dc.PushTransform(new RotateTransform(Math.Sin(phase * .71) * (1.6 + energy * 6), 100, 100));
        Point[] outer = [new(41, 26), new(174, 98), new(47, 175)];
        Point[] inner = [new(71, 70), new(132, 100), new(68, 137)];
        Point[] seam = new Point[3];
        for (var i = 0; i < 3; i++)
        {
            var t = phase + i * 2.1;
            outer[i] += new Vector(Math.Cos(t) * (1.0 + 9 * energy), Math.Sin(t * 1.2) * (1.1 + 9 * energy));
            inner[i] += new Vector(Math.Sin(t * 1.4) * (1 + 10 * energy), Math.Cos(t) * (1.3 + 8 * energy));
            seam[i] = outer[i] + (inner[i] - outer[i]) * (.43 + .17 * Math.Sin(t));
        }
        var outline = Polygon(outer);
        foreach (var glow in Halo) dc.DrawGeometry(null, glow, outline);
        for (var i = 0; i < 3; i++)
        {
            var j = (i + 1) % 3;
            var ridge = seam[i] + (seam[j] - seam[i]) * (.54 + .13 * Math.Sin(phase + i));
            dc.DrawGeometry(Body[i], null, Polygon(outer[i], outer[j], inner[j], inner[i]));
            dc.DrawGeometry(Ridge[i], null, Polygon(outer[i], outer[j], ridge));
            dc.DrawGeometry(Shadow[i], null, Polygon(outer[i], ridge, inner[i]));
            dc.DrawGeometry(Ridge[j], null, Polygon(ridge, inner[j], outer[j]));
            dc.DrawLine(Fold, outer[i], ridge);
            dc.DrawLine(InnerEdge, inner[i], inner[j]);
        }
        dc.DrawGeometry(null, OuterEdge, outline);
        dc.Pop(); dc.Pop(); dc.Pop();
    }

    private static StreamGeometry Polygon(params Point[] points)
    {
        var shape = new StreamGeometry();
        using (var ctx = shape.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
        }
        shape.Freeze();
        return shape;
    }
    private static Pen Line(string hex, double width)
    {
        var pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), width) { LineJoin = PenLineJoin.Round };
        pen.Freeze(); return pen;
    }
    private static Brush Gradient(string start, string middle, string end)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(1,.83) };
        brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(start), 0));
        brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(middle), .53));
        brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(end), 1));
        brush.Freeze(); return brush;
    }
}
