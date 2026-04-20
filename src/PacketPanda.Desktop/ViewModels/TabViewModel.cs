using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class TabViewModel : ReactiveObject
{
    private readonly IDialogService _dialog;
    private readonly Subject<Unit> _closeRequested = new();

    public TabViewModel(RequestEditorViewModel editor, IDialogService dialog)
    {
        Editor = editor;
        _dialog = dialog;

        this.WhenAnyValue(t => t.Editor.CurrentFilePath, t => t.Editor.IsDirty)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(FilePath));
                this.RaisePropertyChanged(nameof(IsDirty));
                this.RaisePropertyChanged(nameof(Title));
            });

        CloseCommand = ReactiveCommand.CreateFromTask(CloseAsync);
    }

    public RequestEditorViewModel Editor { get; }

    public string? FilePath => Editor.CurrentFilePath;

    public bool IsDirty => Editor.IsDirty;

    public string Title
    {
        get
        {
            var name = FilePath is null
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(FilePath);
            return IsDirty ? $"{name} ●" : name;
        }
    }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public IObservable<Unit> CloseRequested => _closeRequested;

    private async Task CloseAsync()
    {
        if (IsDirty)
        {
            var discard = await _dialog.ConfirmDiscardAsync(Title);
            if (!discard) return;
        }

        _closeRequested.OnNext(Unit.Default);
    }
}
