using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    public RequestEditorView()
    {
        InitializeComponent();
    }
}
