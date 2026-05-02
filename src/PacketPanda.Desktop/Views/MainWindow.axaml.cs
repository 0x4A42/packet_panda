using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TabItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsMiddleButtonPressed)
            return;

        if (sender is Control { DataContext: TabViewModel tab })
        {
            tab.CloseCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private void CollectionTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // First click on an unselected TreeViewItem causes TreeViewItem to handle PointerPressed
        // for selection, which suppresses Tapped. SelectionChanged handles that first-click case.
        if (e.AddedItems.Count > 0 &&
            e.AddedItems[0] is CollectionNodeViewModel { IsFolder: false } vm)
        {
            vm.Open();
        }
    }

    private void CollectionTreeView_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        // Handles re-clicking an already-selected item (SelectionChanged doesn't fire then).
        if (e.Source is Avalonia.Controls.Control ctrl &&
            ctrl.DataContext is CollectionNodeViewModel vm &&
            !vm.IsFolder)
        {
            vm.Open();
            e.Handled = true;
        }
    }
}
