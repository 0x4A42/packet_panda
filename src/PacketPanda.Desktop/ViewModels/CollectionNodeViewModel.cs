using System.Collections.ObjectModel;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionNodeViewModel : ReactiveObject
{
    private bool _isExpanded;

    public CollectionNodeViewModel(
        string id,
        string name,
        bool isFolder,
        string? method,
        IEnumerable<CollectionNodeViewModel> children,
        CollectionNode? sourceNode = null)
    {
        Id = id;
        Name = name;
        IsFolder = isFolder;
        Method = method;
        Children = new ObservableCollection<CollectionNodeViewModel>(children);
        SourceNode = sourceNode;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public string? Method { get; }
    public CollectionNode? SourceNode { get; }
    public Action<RequestNode>? RequestOpenedCallback { get; init; }
    public ObservableCollection<CollectionNodeViewModel> Children { get; }

    public string DisplayName => !IsFolder && Method is not null ? $"{Method} {Name}" : Name;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public void Open()
    {
        if (SourceNode is RequestNode rn)
            RequestOpenedCallback?.Invoke(rn);
    }

    public static CollectionNodeViewModel From(CollectionNode node)
    {
        return node switch
        {
            FolderNode f => new CollectionNodeViewModel(
                f.Id, f.Name, isFolder: true, method: null,
                f.Children.Select(From), sourceNode: f),
            RequestNode r => new CollectionNodeViewModel(
                r.Id, r.Name, isFolder: false,
                method: r.Method.ToString().ToUpperInvariant(),
                children: [], sourceNode: r),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}")
        };
    }

    public static CollectionNodeViewModel From(CollectionNode node, Action<RequestNode> onRequestOpened)
    {
        return node switch
        {
            FolderNode f => new CollectionNodeViewModel(
                f.Id, f.Name, isFolder: true, method: null,
                f.Children.Select(c => From(c, onRequestOpened)), sourceNode: f),
            RequestNode r => new CollectionNodeViewModel(
                r.Id, r.Name, isFolder: false,
                method: r.Method.ToString().ToUpperInvariant(),
                children: [], sourceNode: r)
            {
                RequestOpenedCallback = onRequestOpened
            },
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}")
        };
    }
}
