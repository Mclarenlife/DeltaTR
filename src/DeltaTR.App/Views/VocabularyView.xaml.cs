using System.Windows.Controls;
using System.Windows;
using DeltaTR.App.ViewModels;
namespace DeltaTR.App.Views;
public partial class VocabularyView : UserControl
{
    public VocabularyView() => InitializeComponent();

    private void PinSelected(object sender, RoutedEventArgs e) => SetPinned(true);
    private void UnpinSelected(object sender, RoutedEventArgs e) => SetPinned(false);
    private void SetPinned(bool pin)
    {
        if (DataContext is not MainViewModel vm) return;
        var selection = Fields.SelectedItems.Cast<VocabularyEntry>().ToArray();
        if (selection.Length == 0) return;
        vm.SetVocabularyPinned(selection, pin);
        var keys = selection.Select(entry => entry.Field.Key).ToHashSet();
        foreach (var entry in Fields.Items.Cast<VocabularyEntry>().Where(row => keys.Contains(row.Field.Key)))
            if (!Fields.SelectedItems.Contains(entry)) Fields.SelectedItems.Add(entry);
    }
}
