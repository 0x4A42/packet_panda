using Avalonia.Controls;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class DocsSidebarView : UserControl
{
    public DocsSidebarView()
    {
        InitializeComponent();
    }

    private void OnDocItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not DocItemViewModel item) return;
        if (DataContext is not DocsSidebarViewModel vm) return;

        listBox.SelectedItem = null;
        vm.OpenDocCommand.Execute(item).Subscribe();
    }
}
