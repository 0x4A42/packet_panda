using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    private VariableColorizer? _colorizer;
    private CompositeDisposable? _subs;
    private EventHandler? _textChangedHandler;

    public RequestEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _subs?.Dispose();
        _subs = null;

        if (_textChangedHandler is not null)
        {
            BodyEditor.TextChanged -= _textChangedHandler;
            _textChangedHandler = null;
        }

        if (DataContext is not RequestEditorViewModel vm) return;

        _subs = new CompositeDisposable();

        if (_colorizer is not null)
            BodyEditor.TextArea.TextView.LineTransformers.Remove(_colorizer);

        _colorizer = new VariableColorizer();
        BodyEditor.TextArea.TextView.LineTransformers.Add(_colorizer);

        // Sync RequestBody → TextEditor
        vm.WhenAnyValue(x => x.RequestBody)
            .Subscribe(body =>
            {
                if (BodyEditor.Text != (body ?? string.Empty))
                    BodyEditor.Text = body ?? string.Empty;
            })
            .DisposeWith(_subs);

        // Sync TextEditor → RequestBody
        _textChangedHandler = (_, _) =>
        {
            var text = BodyEditor.Text;
            var normalized = string.IsNullOrEmpty(text) ? null : text;
            if (vm.RequestBody != normalized)
                vm.RequestBody = normalized;
        };
        BodyEditor.TextChanged += _textChangedHandler;

        // Update colorizer when active environment changes (must run on UI thread for Redraw)
        vm.WhenAnyValue(x => x.ActiveEnvironmentVariables)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(vars =>
            {
                _colorizer.UpdateEnvironment(vars);
                BodyEditor.TextArea.TextView.Redraw();
            })
            .DisposeWith(_subs);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is RequestEditorViewModel vm)
        {
            BodyEditor.Text = vm.RequestBody ?? string.Empty;
            BodyEditor.TextArea.TextView.Redraw();
        }
    }
}
