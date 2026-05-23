using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class MainWindow : Window
{
    private IDisposable? _editingStartedSub;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _editingStartedSub?.Dispose();
        if (DataContext is ShellViewModel shell)
        {
            _editingStartedSub = shell.Sidebar.EditingNodeStarted
                .Subscribe(item =>
                    Dispatcher.UIThread.Post(() => FocusRenameTextBoxInTree(item), DispatcherPriority.Input));
        }
    }

    private void FocusRenameTextBoxInTree(object item)
    {
        var treeView = this.FindControl<TreeView>("CollectionTree");
        if (treeView is null) return;
        var tb = treeView.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.DataContext == item && t.IsVisible);
        if (tb is null) return;
        tb.Focus();
        tb.SelectAll();
    }

    internal void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is CollectionNodeViewModel nodeVm && nodeVm.IsEditing)
            nodeVm.CommitRenameCommand?.Execute().Subscribe();
        else if (tb.DataContext is CollectionViewModel colVm && colVm.IsEditing)
            colVm.CommitRenameCommand.Execute().Subscribe();
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

    private void CollectionTreeView_Tapped(object? sender, TappedEventArgs e)
    {
        // Handles re-clicking an already-selected item (SelectionChanged doesn't fire then).
        if (e.Source is Control ctrl &&
            ctrl.DataContext is CollectionNodeViewModel vm &&
            !vm.IsFolder)
        {
            vm.Open();
            e.Handled = true;
        }
    }
}
