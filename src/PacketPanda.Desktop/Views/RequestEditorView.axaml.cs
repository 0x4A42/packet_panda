using Avalonia;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    public RequestEditorView()
    {
        InitializeComponent();

        UrlEditor.TextArea.TextView.LineTransformers.Add(new VariableColorizer());
        BodyEditor.TextArea.TextView.LineTransformers.Add(new VariableColorizer());

        UrlEditor.TextArea.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) => { if (e.Key == Key.Enter) e.Handled = true; },
            handledEventsToo: false);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AssignDocuments();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AssignDocuments();
    }

    private void AssignDocuments()
    {
        if (DataContext is not RequestEditorViewModel vm) return;
        if (UrlEditor.Document != vm.UrlDocument)
            UrlEditor.Document = vm.UrlDocument;
        if (BodyEditor.Document != vm.BodyDocument)
            BodyEditor.Document = vm.BodyDocument;
    }
}
