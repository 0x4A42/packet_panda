using Avalonia.Controls;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class HistorySidebarView : UserControl
{
    public HistorySidebarView()
    {
        InitializeComponent();
    }

    private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not HistoryItemViewModel item) return;
        if (DataContext is not HistorySidebarViewModel vm) return;

        listBox.SelectedItem = null;
        vm.OpenCommand.Execute(item).Subscribe();
    }
}
