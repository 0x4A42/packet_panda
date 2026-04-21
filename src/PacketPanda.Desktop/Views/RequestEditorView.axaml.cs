using Avalonia.Input;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;

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

        this.WhenActivated(d =>
        {
            this.WhenAnyValue(x => x.ViewModel)
                .WhereNotNull()
                .Subscribe(vm =>
                {
                    UrlEditor.Document = vm.UrlDocument;
                    BodyEditor.Document = vm.BodyDocument;
                })
                .DisposeWith(d);
        });
    }
}
