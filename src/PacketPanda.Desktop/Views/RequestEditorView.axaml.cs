using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    private CompositeDisposable? _subs;

    public RequestEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _subs?.Dispose();
        _subs = null;

        if (DataContext is not RequestEditorViewModel vm) return;

        _subs = new CompositeDisposable();

        // Repaint URL overlay whenever the URL or active environment changes.
        vm.WhenAnyValue(x => x.Url, x => x.ActiveEnvironmentVariables)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(t => VariableTextHighlighter.Apply(UrlOverlay, t.Item1, t.Item2))
            .DisposeWith(_subs);

        // Repaint body overlay whenever the body or active environment changes.
        vm.WhenAnyValue(x => x.RequestBody, x => x.ActiveEnvironmentVariables)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(t => VariableTextHighlighter.Apply(BodyOverlay, t.Item1, t.Item2))
            .DisposeWith(_subs);
    }
}
