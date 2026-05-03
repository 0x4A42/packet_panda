using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionNodeViewModel : ReactiveObject
{
    private bool _isExpanded;
    private bool _isEditing;
    private string _editingName = string.Empty;
    private readonly ObservableAsPropertyHelper<bool> _showFolderActions;
    private readonly ObservableAsPropertyHelper<bool> _showRequestActions;

    public CollectionNodeViewModel(
        string id,
        string name,
        bool isFolder,
        string? method,
        IEnumerable<CollectionNodeViewModel> children,
        CollectionNode? sourceNode = null)
    {
        Id = id;
        _name = name;
        IsFolder = isFolder;
        Method = method;
        Children = new ObservableCollection<CollectionNodeViewModel>(children);
        SourceNode = sourceNode;

        _showFolderActions = this.WhenAnyValue(x => x.IsEditing)
            .Select(editing => isFolder && !editing)
            .ToProperty(this, x => x.ShowFolderActions);

        _showRequestActions = this.WhenAnyValue(x => x.IsEditing)
            .Select(editing => !isFolder && !editing)
            .ToProperty(this, x => x.ShowRequestActions);
    }

    private string _name;

    public string Id { get; }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public bool IsFolder { get; }
    public string? Method { get; }
    public CollectionNode? SourceNode { get; set; }
    public Action<RequestNode>? RequestOpenedCallback { get; init; }
    public ReactiveCommand<Unit, Unit>? NewRequestCommand { get; init; }
    public ReactiveCommand<Unit, Unit>? NewFolderCommand { get; init; }
    public ReactiveCommand<Unit, Unit>? RenameFolderCommand { get; set; }
    public ReactiveCommand<Unit, Unit>? CopyFolderCommand { get; init; }
    public ReactiveCommand<Unit, Unit>? DeleteFolderCommand { get; init; }
    public ReactiveCommand<Unit, Unit>? RenameRequestCommand { get; set; }
    public ReactiveCommand<Unit, Unit>? DuplicateRequestCommand { get; init; }
    public ReactiveCommand<Unit, Unit>? DeleteRequestCommand { get; init; }
    public ReactiveCommand<Unit, Unit>? CommitRenameCommand { get; set; }
    public ReactiveCommand<Unit, Unit>? CancelRenameCommand { get; set; }
    public ObservableCollection<CollectionNodeViewModel> Children { get; }

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

    public bool ShowFolderActions => _showFolderActions.Value;
    public bool ShowRequestActions => _showRequestActions.Value;

    public string DisplayName => !IsFolder && Method is not null ? $"{Method} {Name}" : Name;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public void BeginEdit()
    {
        EditingName = Name;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        EditingName = string.Empty;
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
