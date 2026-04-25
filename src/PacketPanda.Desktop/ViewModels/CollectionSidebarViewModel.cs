using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionSidebarViewModel : ReactiveObject
{
    private readonly ICollectionFileStore _collectionStore;
    private readonly ISettingsStore _settings;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialog;
    private readonly Subject<RequestNode> _requestOpened = new();

    private string? _filterText;
    private string? _errorMessage;

    public CollectionSidebarViewModel(
        ICollectionFileStore collectionStore,
        ISettingsStore settings,
        IFilePickerService filePicker,
        IDialogService dialog)
    {
        _collectionStore = collectionStore;
        _settings = settings;
        _filePicker = filePicker;
        _dialog = dialog;

        Collections = new ObservableCollection<CollectionViewModel>();

        NewCollectionCommand = ReactiveCommand.CreateFromTask(NewCollectionAsync);
        OpenFilesCommand = ReactiveCommand.CreateFromTask(OpenFilesAsync);
        OpenFolderCommand = ReactiveCommand.CreateFromTask(OpenFolderAsync);
        RemoveCollectionCommand = ReactiveCommand.Create<CollectionViewModel>(RemoveCollection);
    }

    public ObservableCollection<CollectionViewModel> Collections { get; }

    public string? FilterText
    {
        get => _filterText;
        set => this.RaiseAndSetIfChanged(ref _filterText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<Unit, Unit> NewCollectionCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFilesCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }
    public ReactiveCommand<CollectionViewModel, Unit> RemoveCollectionCommand { get; }

    public IObservable<RequestNode> RequestOpened => _requestOpened;

    public async Task LoadCollectionsAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
            await AddCollectionFromPathAsync(path);
    }

    public void RemoveCollection(CollectionViewModel collection)
    {
        Collections.Remove(collection);
        _ = PersistOpenPathsAsync();
    }

    private async Task NewCollectionAsync()
    {
        var path = await _filePicker.PickSaveCollectionAsync();
        if (path is null) return;

        var name = Path.GetFileNameWithoutExtension(path);
        var root = new FolderNode(Guid.NewGuid().ToString(), name, []);
        var collection = new Collection(name, path, root);

        try
        {
            await _collectionStore.SaveAsync(path, collection);
            AddCollectionViewModel(collection);
            await PersistOpenPathsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create collection: {ex.Message}";
        }
    }

    private async Task OpenFilesAsync()
    {
        var paths = await _filePicker.PickOpenMultipleAsync();
        foreach (var path in paths)
            await AddCollectionFromPathAsync(path);
        if (paths.Count > 0)
            await PersistOpenPathsAsync();
    }

    private async Task OpenFolderAsync()
    {
        var folder = await _filePicker.PickOpenFolderAsync();
        if (folder is null) return;

        var files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            await AddCollectionFromPathAsync(file);

        if (files.Length > 0)
            await PersistOpenPathsAsync();
    }

    private async Task AddCollectionFromPathAsync(string path)
    {
        if (Collections.Any(c => string.Equals(c.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            var collection = await _collectionStore.LoadAsync(path);
            AddCollectionViewModel(collection);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void AddCollectionViewModel(Collection collection)
    {
        var vm = new CollectionViewModel(collection, _collectionStore, _dialog, node => _requestOpened.OnNext(node));
        Collections.Add(vm);
    }

    private async Task PersistOpenPathsAsync()
    {
        try
        {
            var current = await _settings.LoadAsync();
            var paths = Collections.Select(c => c.FilePath).ToList();
            await _settings.SaveAsync(current with { OpenCollectionPaths = paths });
        }
        catch (SettingsException) { }
    }
}

public class CollectionViewModel : ReactiveObject
{
    private readonly ICollectionFileStore _store;
    private readonly IDialogService _dialog;
    private readonly Action<RequestNode> _onRequestOpened;
    private Collection _collection;

    public CollectionViewModel(
        Collection collection,
        ICollectionFileStore store,
        IDialogService dialog,
        Action<RequestNode> onRequestOpened)
    {
        _collection = collection;
        _store = store;
        _dialog = dialog;
        _onRequestOpened = onRequestOpened;

        Items = new ObservableCollection<CollectionNodeViewModel>(
            collection.Root.Children.Select(BuildNodeVm));

        NewRequestCommand = ReactiveCommand.CreateFromTask(AddNewRequestAsync);
        NewFolderCommand = ReactiveCommand.CreateFromTask(AddNewFolderAsync);
    }

    public string Name => _collection.Name;
    public string FilePath => _collection.FilePath;
    public ObservableCollection<CollectionNodeViewModel> Items { get; }

    public ReactiveCommand<Unit, Unit> NewRequestCommand { get; }
    public ReactiveCommand<Unit, Unit> NewFolderCommand { get; }

    private CollectionNodeViewModel BuildNodeVm(CollectionNode node)
    {
        return node switch
        {
            FolderNode f => new CollectionNodeViewModel(
                f.Id, f.Name, isFolder: true, method: null,
                f.Children.Select(BuildNodeVm), sourceNode: f)
            {
                NewRequestCommand = ReactiveCommand.CreateFromTask(() => AddNewRequestToFolderAsync(f.Id)),
                NewFolderCommand = ReactiveCommand.CreateFromTask(() => AddNewFolderToFolderAsync(f.Id)),
            },
            RequestNode r => new CollectionNodeViewModel(
                r.Id, r.Name, isFolder: false,
                method: r.Method.ToString().ToUpperInvariant(),
                children: [], sourceNode: r)
            {
                RequestOpenedCallback = _onRequestOpened
            },
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}")
        };
    }

    private async Task AddNewRequestAsync()
    {
        var id = Guid.NewGuid().ToString();
        var newNode = new RequestNode(id, "New Request", _collection.FilePath, HttpMethod.Get, string.Empty, null);
        var newChildren = _collection.Root.Children.Append(newNode).ToList();
        var candidate = _collection with { Root = _collection.Root with { Children = newChildren } };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        Items.Add(BuildNodeVm(newNode));
        _onRequestOpened(newNode);
    }

    private async Task AddNewFolderAsync()
    {
        var id = Guid.NewGuid().ToString();
        var newFolder = new FolderNode(id, "New Folder", []);
        var newChildren = _collection.Root.Children.Append(newFolder).ToList();
        var candidate = _collection with { Root = _collection.Root with { Children = newChildren } };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        Items.Add(BuildNodeVm(newFolder));
    }

    private async Task AddNewRequestToFolderAsync(string folderId)
    {
        var id = Guid.NewGuid().ToString();
        var newNode = new RequestNode(id, "New Request", _collection.FilePath, HttpMethod.Get, string.Empty, null);
        var (newRoot, found) = InsertIntoFolder(_collection.Root, folderId, newNode);
        if (!found) return;
        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        FindNodeVm(Items, folderId)?.Children.Add(BuildNodeVm(newNode));
        _onRequestOpened(newNode);
    }

    private async Task AddNewFolderToFolderAsync(string folderId)
    {
        var id = Guid.NewGuid().ToString();
        var newFolder = new FolderNode(id, "New Folder", []);
        var (newRoot, found) = InsertIntoFolder(_collection.Root, folderId, newFolder);
        if (!found) return;
        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        FindNodeVm(Items, folderId)?.Children.Add(BuildNodeVm(newFolder));
    }

    private static (FolderNode Root, bool Found) InsertIntoFolder(
        FolderNode current, string targetId, CollectionNode newNode)
    {
        if (current.Id == targetId)
            return (current with { Children = current.Children.Append(newNode).ToList() }, true);

        var changed = false;
        var newChildren = current.Children.Select(child =>
        {
            if (changed || child is not FolderNode f) return child;
            var (updated, found) = InsertIntoFolder(f, targetId, newNode);
            if (!found) return child;
            changed = true;
            return (CollectionNode)updated;
        }).ToList();

        return changed ? (current with { Children = newChildren }, true) : (current, false);
    }

    private static CollectionNodeViewModel? FindNodeVm(IEnumerable<CollectionNodeViewModel> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id) return node;
            var found = FindNodeVm(node.Children, id);
            if (found is not null) return found;
        }
        return null;
    }
}
