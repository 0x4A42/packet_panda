using System.Collections.ObjectModel;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionNodeViewModel : ReactiveObject
{
    private bool _isExpanded;

    public CollectionNodeViewModel(string name, string path, bool isFolder, string? method, IEnumerable<CollectionNodeViewModel> children)
    {
        Name = name;
        Path = path;
        IsFolder = isFolder;
        Method = method;
        Children = new ObservableCollection<CollectionNodeViewModel>(children);
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsFolder { get; }
    public string? Method { get; }
    public ObservableCollection<CollectionNodeViewModel> Children { get; }

    public string DisplayName => !IsFolder && Method is not null ? $"{Method} {Name}" : Name;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public static CollectionNodeViewModel From(CollectionNode node)
    {
        return node switch
        {
            FolderNode f => new CollectionNodeViewModel(f.Name, f.Path, isFolder: true, method: null, f.Children.Select(From)),
            RequestNode r => new CollectionNodeViewModel(r.Name, r.Path, isFolder: false, method: r.Method, []),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}")
        };
    }
}
