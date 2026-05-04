namespace PacketPanda.Desktop.ViewModels;

public sealed class TestResultViewModel(string name, bool passed, string? errorMessage)
{
    public string Name { get; } = name;
    public bool Passed { get; } = passed;
    public string? ErrorMessage { get; } = errorMessage;
}
