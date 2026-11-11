using System.Windows;
using DeltaTR.App.ViewModels;

namespace DeltaTR.App;

public partial class CalibrationWindow : Window
{
    public CalibrationWindow(CalibrationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += () => DialogResult = true;
    }
}
