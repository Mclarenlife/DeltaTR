using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using DeltaTR.App.Input;
using DeltaTR.App.ViewModels;

namespace DeltaTR.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        BindTalkButton.LostKeyboardFocus += (_, _) => (DataContext as MainViewModel)?.SetBindingMode(false);
        Unloaded += (_, _) => (DataContext as MainViewModel)?.SetBindingMode(false);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm) KeyInput.Password = vm.ApiKeyEdit;
        };
    }
    private void ToggleBinding(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        BindTalkButton.Focus();
        vm.SetBindingMode(!vm.BindingInput);
    }
    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { BindingInput: true } vm) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) vm.SetBindingMode(false);
        else vm.AcceptBinding(TalkBinding.Keyboard(KeyInterop.VirtualKeyFromKey(key)));
    }
    private void KeyChanged(object sender, System.Windows.RoutedEventArgs args)
    {
        if (DataContext is ViewModels.MainViewModel vm) vm.ApiKeyEdit = KeyInput.Password;
    }
}
