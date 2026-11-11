using System.Windows;

namespace DeltaTR.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            WindowFrame.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
            MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        };
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void SwitchTheme(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel viewModel) viewModel.ToggleTheme();
    }
    private void ChooseSimulator(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ViewModels.GameChoice game } &&
            DataContext is ViewModels.MainViewModel viewModel)
            viewModel.SelectedGame = game;
    }
    private void ToggleMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
