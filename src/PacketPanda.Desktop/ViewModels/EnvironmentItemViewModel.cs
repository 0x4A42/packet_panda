using System.Reactive;
using PacketPanda.Core.Environments;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class EnvironmentItemViewModel : ReactiveObject
{
    private bool _isEditing;
    private string _editingName = string.Empty;

    public EnvironmentRef Ref { get; private set; }
    public string Name => Ref.Name;
    public string FilePath => Ref.FilePath;

    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    public ReactiveCommand<Unit, Unit> ActivateCommand { get; }
    public ReactiveCommand<Unit, Unit> RenameCommand { get; }
    public ReactiveCommand<Unit, Unit> CommitRenameCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelRenameCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public EnvironmentItemViewModel(
        EnvironmentRef envRef,
        Action onActivate,
        Action onRename,
        Func<Task> onCommitRename,
        Action onCancelRename,
        Action onDuplicate,
        Action onDelete)
    {
        Ref = envRef;
        ActivateCommand = ReactiveCommand.Create(onActivate);
        RenameCommand = ReactiveCommand.Create(onRename);
        CommitRenameCommand = ReactiveCommand.CreateFromTask(onCommitRename);
        CancelRenameCommand = ReactiveCommand.Create(onCancelRename);
        DuplicateCommand = ReactiveCommand.Create(onDuplicate);
        DeleteCommand = ReactiveCommand.Create(onDelete);
    }

    public void UpdateRef(EnvironmentRef newRef)
    {
        Ref = newRef;
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(FilePath));
    }

    public void BeginEdit()
    {
        EditingName = Ref.Name;
        IsEditing = true;
    }

    public void CancelEdit() => IsEditing = false;
}
