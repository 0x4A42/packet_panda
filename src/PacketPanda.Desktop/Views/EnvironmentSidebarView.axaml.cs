using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class EnvironmentSidebarView : ReactiveUserControl<EnvironmentSidebarViewModel>
{
    private IDisposable? _editingItemSub;

    public EnvironmentSidebarView()
    {
        InitializeComponent();

        var listBox = this.FindControl<ListBox>("EnvList")!;
        listBox.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _editingItemSub?.Dispose();
        if (ViewModel is not null)
        {
            _editingItemSub = ViewModel
                .WhenAnyValue(vm => vm.EditingItem)
                .Subscribe(item =>
                {
                    if (item is not null)
                        FocusRenameTextBox(item);
                });
        }
    }

    private void FocusRenameTextBox(EnvironmentItemViewModel item)
    {
        var listBox = this.FindControl<ListBox>("EnvList")!;
        Dispatcher.UIThread.Post(() =>
        {
            var container = listBox.ContainerFromItem(item);
            if (container?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } tb)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null) return;
        if (sender is not ListBox lb) return;

        if (lb.SelectedItem is EnvironmentItemViewModel item)
        {
            ViewModel.OpenEnvironmentCommand.Execute(item.Ref).Subscribe();
            lb.SelectedItem = null;
        }
    }

    internal void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not TextBox tb) return;
        if (tb.DataContext is EnvironmentItemViewModel item && item.IsEditing)
            ViewModel.CommitRenameCommand.Execute(item).Subscribe();
    }
}
