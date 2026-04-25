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

    private void CollectionTreeView_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is Avalonia.Controls.Control ctrl &&
            ctrl.DataContext is CollectionNodeViewModel vm)
        {
            vm.Open();
        }
    }
}
