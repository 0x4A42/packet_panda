using Avalonia;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    private readonly VariableColorizer _urlColorizer = new();
    private readonly VariableColorizer _bodyColorizer = new();
    private IDisposable? _envSub;

    public RequestEditorView()
    {
        InitializeComponent();

        UrlEditor.TextArea.TextView.LineTransformers.Add(_urlColorizer);
        BodyEditor.TextArea.TextView.LineTransformers.Add(_bodyColorizer);

        UrlEditor.TextArea.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) => { if (e.Key == Key.Enter) e.Handled = true; },
            handledEventsToo: false);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AssignDocuments();
        SubscribeEnvironment();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AssignDocuments();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _envSub?.Dispose();
        _envSub = null;
    }

    private void AssignDocuments()
    {
        if (DataContext is not RequestEditorViewModel vm) return;
        if (UrlEditor.Document != vm.UrlDocument)
            UrlEditor.Document = vm.UrlDocument;
        if (BodyEditor.Document != vm.BodyDocument)
            BodyEditor.Document = vm.BodyDocument;
    }

    private void SubscribeEnvironment()
    {
        _envSub?.Dispose();
        if (DataContext is not RequestEditorViewModel vm) { _envSub = null; return; }

        _envSub = vm.WhenAnyValue(x => x.ActiveEnvironmentVariables)
            .Subscribe(vars =>
            {
                _urlColorizer.UpdateEnvironment(vars);
                _bodyColorizer.UpdateEnvironment(vars);
                UrlEditor.TextArea.TextView.Redraw();
                BodyEditor.TextArea.TextView.Redraw();
            });
    }
}
