using Avalonia.Controls;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class EnvironmentSidebarView : ReactiveUserControl<EnvironmentSidebarViewModel>
{
    public EnvironmentSidebarView()
    {
        InitializeComponent();

        var listBox = this.FindControl<ListBox>("EnvList")!;
        listBox.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null) return;
        if (sender is not ListBox lb) return;

        if (lb.SelectedItem is PacketPanda.Core.Environments.EnvironmentRef envRef)
        {
            ViewModel.OpenEnvironmentCommand.Execute(envRef).Subscribe();
            lb.SelectedItem = null;
        }
    }
}
