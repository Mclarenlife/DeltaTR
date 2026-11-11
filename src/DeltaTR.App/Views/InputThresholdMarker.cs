using System.Windows;
using System.Windows.Media;

namespace DeltaTR.App.Views;

public sealed class InputThresholdMarker : System.Windows.Controls.Control
{
    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(int), typeof(InputThresholdMarker),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Threshold
    {
        get => (int)GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = Math.Min(2, ActualWidth);
        var x = (ActualWidth - width) * Math.Clamp(Threshold, 0, 100) / 100d;
        drawingContext.DrawRectangle(Foreground, null, new Rect(x, 0, width, ActualHeight));
    }
}
