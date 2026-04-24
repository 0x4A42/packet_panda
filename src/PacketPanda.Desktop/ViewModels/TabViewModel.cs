using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class TabViewModel : ReactiveObject
{
    private readonly IDialogService _dialog;
    private readonly Subject<Unit> _closeRequested = new();

    public TabViewModel(ITabContent content, IDialogService dialog)
    {
        Content = content;
        _dialog = dialog;

        if (content is IReactiveObject reactiveContent)
        {
            reactiveContent.PropertyChanged += (_, _) =>
            {
                this.RaisePropertyChanged(nameof(FilePath));
                this.RaisePropertyChanged(nameof(IsDirty));
                this.RaisePropertyChanged(nameof(ShowDirtyIndicator));
                this.RaisePropertyChanged(nameof(Title));
                this.RaisePropertyChanged(nameof(DisplayTitle));
                this.RaisePropertyChanged(nameof(MethodLabel));
            };
        }

        CloseCommand = ReactiveCommand.CreateFromTask(CloseAsync);
    }

    public ITabContent Content { get; }

    public string? FilePath => Content.CurrentFilePath;

    public bool IsDirty => Content.IsDirty;

    public string DisplayTitle => Content.DisplayTitle;

    public string Title => IsDirty ? $"{DisplayTitle} ●" : DisplayTitle;

    public bool ShowDirtyIndicator => IsDirty || FilePath is null;

    public string? MethodLabel => Content.MethodLabel;

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public IObservable<Unit> CloseRequested => _closeRequested;

    private async Task CloseAsync()
    {
        if (IsDirty)
        {
            var discard = await _dialog.ConfirmDiscardAsync(DisplayTitle);
            if (!discard) return;
        }

        _closeRequested.OnNext(Unit.Default);
        if (Content is IDisposable d)
            d.Dispose();
    }
}
