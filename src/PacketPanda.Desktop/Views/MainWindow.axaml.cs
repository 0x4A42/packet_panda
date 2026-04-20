using Avalonia.Controls;
using Avalonia.Input;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: CollectionNodeViewModel node }) return;
        if (DataContext is not ShellViewModel shell) return;
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        shell.Sidebar.OpenRequestCommand.Execute(node).Subscribe();
    }
}
