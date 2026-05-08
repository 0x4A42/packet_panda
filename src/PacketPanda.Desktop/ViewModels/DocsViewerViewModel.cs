using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class DocsViewerViewModel : ReactiveObject, ITabContent
{
    public DocsViewerViewModel(string filePath)
    {
        CurrentFilePath = filePath;
        var stem = Path.GetFileNameWithoutExtension(filePath);
        DisplayTitle = stem.Length > 0 ? char.ToUpper(stem[0]) + stem[1..] : stem;
        try { MarkdownContent = File.ReadAllText(filePath); }
        catch { MarkdownContent = "_Could not load document._"; }
    }

    public string MarkdownContent { get; }

    // ITabContent
    public string? CurrentFilePath { get; }
    public string? RequestId => null;
    public string DisplayTitle { get; }
    public bool IsDirty => false;
    public string? MethodLabel => null;
}
