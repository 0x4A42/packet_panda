using System.Reactive;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class QueryParamRowViewModel : ViewModelBase
{
    private bool _enabled = true;
    private string _key = string.Empty;
    private string _value = string.Empty;

    public QueryParamRowViewModel(Action<QueryParamRowViewModel> onRemove)
    {
        RemoveCommand = ReactiveCommand.Create(() => onRemove(this));
    }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    public string Key
    {
        get => _key;
        set => this.RaiseAndSetIfChanged(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }
}
