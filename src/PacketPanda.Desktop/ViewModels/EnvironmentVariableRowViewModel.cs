using System;
using System.Reactive;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class EnvironmentVariableRowViewModel : ReactiveObject
{
    private string _name = string.Empty;
    private string _value = string.Empty;
    private string _description = string.Empty;
    private bool _isEnabled = true;
    private Action? _deleteSelf;

    public EnvironmentVariableRowViewModel()
    {
        DeleteCommand = ReactiveCommand.Create(
            () => _deleteSelf?.Invoke(),
            this.WhenAnyValue(x => x.Name, name => !string.IsNullOrEmpty(name)));
    }

    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public void SetDeleteCallback(Action callback) => _deleteSelf = callback;

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

    public bool Enabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }
}
