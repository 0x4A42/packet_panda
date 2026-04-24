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

    public EnvironmentItemViewModel(EnvironmentRef envRef) => Ref = envRef;

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
