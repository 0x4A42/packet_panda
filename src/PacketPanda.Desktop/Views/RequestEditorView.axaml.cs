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

        // Suppress newlines in the single-line URL editor
        UrlEditor.TextArea.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) => { if (e.Key == Key.Enter) e.Handled = true; },
            handledEventsToo: false);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RequestEditorViewModel vm)
        {
            UrlEditor.Document = vm.UrlDocument;
            BodyEditor.Document = vm.BodyDocument;
        }
    }
}
