namespace PacketPanda.Desktop.ViewModels;

public interface ITabContent
{
    string? CurrentFilePath { get; }
    string DisplayTitle { get; }
    bool IsDirty { get; }
    string? MethodLabel { get; }   // null for non-request tabs
}
