using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class EnvironmentEditorView : ReactiveUserControl<EnvironmentEditorViewModel>
{
    public EnvironmentEditorView()
    {
        InitializeComponent();
    }
}
