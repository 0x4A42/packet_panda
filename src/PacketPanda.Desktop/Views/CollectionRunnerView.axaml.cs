using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class CollectionRunnerView : ReactiveUserControl<CollectionRunnerViewModel>
{
    public CollectionRunnerView()
    {
        InitializeComponent();
    }
}
