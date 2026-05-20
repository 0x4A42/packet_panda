using System.Reactive;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class FormRowViewModel : ViewModelBase
{
    private bool _enabled = true;
    private string _name = string.Empty;
    private string _value = string.Empty;

    public FormRowViewModel(Action<FormRowViewModel> onRemove)
    {
        RemoveCommand = ReactiveCommand.Create(() => onRemove(this));
    }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }
}
