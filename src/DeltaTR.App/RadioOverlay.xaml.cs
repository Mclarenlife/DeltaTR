using System.Windows;
using System.Windows.Input;

namespace DeltaTR.App;

public partial class RadioOverlay : Window
{
    public RadioOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
            Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        };
        Left = SystemParameters.WorkArea.Right - 380;
        Top = SystemParameters.WorkArea.Top + 24;
    }
    private void DragHeader(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void CloseOverlay(object sender, RoutedEventArgs args) => Close();
}
