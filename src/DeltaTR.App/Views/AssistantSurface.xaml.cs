using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeltaTR.App.Views;

public partial class AssistantSurface : UserControl
{
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(string), typeof(AssistantSurface), new PropertyMetadata("Right", (d, _) => ((AssistantSurface)d).ArrangeLayers()));
    public string Placement { get => (string)GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }
    public AssistantSurface() { InitializeComponent(); ArrangeLayers(); }

    private void ArrangeLayers()
    {
        var horizontal = Placement is "Left" or "Right";
        var textFirst = Placement is "Left" or "Top";
        Tail.HorizontalAlignment = horizontal ? (textFirst ? HorizontalAlignment.Right : HorizontalAlignment.Left) : HorizontalAlignment.Center;
        Tail.VerticalAlignment = horizontal ? VerticalAlignment.Center : (textFirst ? VerticalAlignment.Bottom : VerticalAlignment.Top);
        var (angle, x, y) = Placement switch
        {
            "Left" => (180, 8, 0), "Top" => (-90, 0, 8), "Bottom" => (90, 0, -8), _ => (0, -8, 0)
        };
        var tailTransform = new TransformGroup();
        tailTransform.Children.Add(new RotateTransform(angle));
        tailTransform.Children.Add(new TranslateTransform(x, y));
        Tail.RenderTransform = tailTransform;
        Grid.SetColumn(Bubble, horizontal && !textFirst ? 1 : 0);
        Grid.SetRow(Bubble, !horizontal && !textFirst ? 1 : 0);
        Grid.SetColumn(Avatar, horizontal && textFirst ? 1 : 0);
        Grid.SetRow(Avatar, !horizontal && textFirst ? 1 : 0);
    }
}
