using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class MainWindow : ReactiveWindow<RequestEditorViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
