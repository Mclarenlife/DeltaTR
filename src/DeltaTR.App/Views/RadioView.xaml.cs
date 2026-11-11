using System.Windows.Controls;
using System.Windows.Input;
using DeltaTR.App.ViewModels;

namespace DeltaTR.App.Views;

public partial class RadioView : UserControl
{
    private bool ownsCapture;
    public RadioView() { InitializeComponent(); Unloaded += (_, _) => CancelMouseTalk(); }
    private void StartTalk(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsListening || !vm.CanTalk) return;
        e.Handled = true;
        vm.BeginTalk(); ownsCapture = vm.IsListening;
        if (ownsCapture) ((Button)sender).CaptureMouse();
    }
    private async void EndTalk(object sender, MouseButtonEventArgs e)
    {
        if (!ownsCapture) return;
        e.Handled = true; ownsCapture = false;
        ((Button)sender).ReleaseMouseCapture();
        if (DataContext is MainViewModel vm) await vm.EndTalkAsync();
    }
    private void LostTalkCapture(object sender, MouseEventArgs e) => CancelMouseTalk();
    private void CancelMouseTalk()
    {
        if (ownsCapture && DataContext is MainViewModel vm) vm.CancelVoice();
        ownsCapture = false;
    }
}
