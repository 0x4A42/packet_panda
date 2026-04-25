using System;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    private VariableColorizer? _colorizer;

    public RequestEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not RequestEditorViewModel vm) return;

        if (_colorizer is not null)
            BodyEditor.TextArea.TextView.LineTransformers.Remove(_colorizer);

        _colorizer = new VariableColorizer();
        BodyEditor.TextArea.TextView.LineTransformers.Add(_colorizer);

        // Sync RequestBody ↔ TextEditor (two-way)
        vm.WhenAnyValue(x => x.RequestBody)
            .Subscribe(body =>
            {
                if (BodyEditor.Text != (body ?? string.Empty))
                    BodyEditor.Text = body ?? string.Empty;
            });

        BodyEditor.TextChanged += (_, _) =>
        {
            var text = BodyEditor.Text;
            if (vm.RequestBody != (string.IsNullOrEmpty(text) ? null : text))
                vm.RequestBody = string.IsNullOrEmpty(text) ? null : text;
        };

        // Update colorizer when active environment changes
        vm.WhenAnyValue(x => x.ActiveEnvironmentVariables)
            .Subscribe(vars =>
            {
                _colorizer.UpdateEnvironment(vars);
                BodyEditor.TextArea.TextView.Redraw();
            });
    }
}
