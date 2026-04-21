using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class EnvironmentVariableRowViewModel : ReactiveObject
{
    private string _name = string.Empty;
    private string _value = string.Empty;
    private string _description = string.Empty;

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

    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }
}
