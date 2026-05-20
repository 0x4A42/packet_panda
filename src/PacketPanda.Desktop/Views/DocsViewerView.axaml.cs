using Avalonia.Controls;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class DocsViewerView : UserControl
{
    public DocsViewerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DocsViewerViewModel vm)
                Content = MarkdownDocRenderer.Render(vm.MarkdownContent);
        };
    }
}
