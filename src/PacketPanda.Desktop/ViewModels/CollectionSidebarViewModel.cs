using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json;
using PacketPanda.Core.Http;
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
    private readonly Subject<object> _editingNodeStarted = new();
    private readonly Subject<FolderNode> _folderRunRequested = new();
    private readonly Subject<string> _environmentFileFound = new();

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
    public IObservable<object> EditingNodeStarted => _editingNodeStarted;
    public IObservable<FolderNode> FolderRunRequested => _folderRunRequested;

    /// <summary>Environment files discovered while opening a folder, for the environment sidebar to load.</summary>
    public IObservable<string> EnvironmentFileFound => _environmentFileFound;

    public async Task LoadCollectionsAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
            await AddCollectionFromPathAsync(path);
    }

    public async Task SyncCollectionFromDiskAsync(string filePath)
    {
        var vm = Collections.FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (vm is not null) await vm.SyncFromDiskAsync();
    }

    public void RemoveCollection(CollectionViewModel collection)
    {
        Collections.Remove(collection);
        _ = PersistOpenPathsAsync();
    }

    private async Task NewCollectionAsync()
    {
        var colDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "collections");
        try { Directory.CreateDirectory(colDir); } catch { /* best-effort */ }

        const string baseName = "New Collection";
        var name = baseName;
        var counter = 2;
        var path = Path.Combine(colDir, $"{name}.json");
        while (Collections.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                  || c.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase))
               || File.Exists(path))
        {
            name = $"{baseName} {counter++}";
            path = Path.Combine(colDir, $"{name}.json");
        }

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

        var files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var openedCollection = false;
        foreach (var file in files)
        {
            if (IsEnvironmentFile(file))
            {
                _environmentFileFound.OnNext(file);
            }
            else
            {
                await AddCollectionFromPathAsync(file);
                openedCollection = true;
            }
        }

        if (openedCollection)
            await PersistOpenPathsAsync();
    }

    /// <summary>
    /// A JSON file is treated as an environment when its root has a <c>values</c> array
    /// and no <c>info</c> object (which is what marks a collection).
    /// </summary>
    private static bool IsEnvironmentFile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && !root.TryGetProperty("info", out _)
                && root.TryGetProperty("values", out var values)
                && values.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
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
        var vm = new CollectionViewModel(
            collection, _collectionStore, _dialog,
            node => _requestOpened.OnNext(node),
            onEditingStarted: item => _editingNodeStarted.OnNext(item),
            onDuplicated: dup =>
            {
                AddCollectionViewModel(dup);
                _ = PersistOpenPathsAsync();
            },
            onDeleted: v =>
            {
                Collections.Remove(v);
                _ = PersistOpenPathsAsync();
            },
            onRemoved: v =>
            {
                Collections.Remove(v);
                _ = PersistOpenPathsAsync();
            },
            onFolderRunRequested: folder => _folderRunRequested.OnNext(folder));
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
    private readonly Action<Collection>? _onDuplicated;
    private readonly Action<CollectionViewModel>? _onDeleted;
    private readonly Action<object>? _onEditingStarted;
    private readonly Action<FolderNode>? _onFolderRunRequested;
    private Collection _collection;
    private bool _isEditing;
    private string _editingName = string.Empty;

    public CollectionViewModel(
        Collection collection,
        ICollectionFileStore store,
        IDialogService dialog,
        Action<RequestNode> onRequestOpened,
        Action<Collection>? onDuplicated = null,
        Action<CollectionViewModel>? onDeleted = null,
        Action<CollectionViewModel>? onRemoved = null,
        Action<object>? onEditingStarted = null,
        Action<FolderNode>? onFolderRunRequested = null)
    {
        _collection = collection;
        _store = store;
        _dialog = dialog;
        _onRequestOpened = onRequestOpened;
        _onDuplicated = onDuplicated;
        _onDeleted = onDeleted;
        _onEditingStarted = onEditingStarted;
        _onFolderRunRequested = onFolderRunRequested;

        Items = new ObservableCollection<CollectionNodeViewModel>(
            collection.Root.Children
                .OrderBy(c => c is FolderNode ? 0 : 1)
                .Select(BuildNodeVm));

        NewRequestCommand = ReactiveCommand.CreateFromTask(AddNewRequestAsync);
        NewFolderCommand = ReactiveCommand.CreateFromTask(AddNewFolderAsync);
        RenameCommand = ReactiveCommand.Create(BeginCollectionEdit);
        CommitRenameCommand = ReactiveCommand.CreateFromTask(CommitRenameCollectionAsync);
        CancelRenameCommand = ReactiveCommand.Create(CancelRenameCollection);
        DuplicateCommand = ReactiveCommand.CreateFromTask(DuplicateCollectionAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteCollectionAsync);
        RunCollectionCommand = ReactiveCommand.Create(() => _onFolderRunRequested?.Invoke(_collection.Root));
        RemoveCommand = ReactiveCommand.Create(() => onRemoved?.Invoke(this));
    }

    public string Name => _collection.Name;
    public string FilePath => _collection.FilePath;

    public async Task SyncFromDiskAsync()
    {
        try
        {
            _collection = await _store.LoadAsync(_collection.FilePath);
            var index = BuildNodeIndex(_collection.Root);
            foreach (var vm in Items)
                RefreshSourceNodes(vm, index);
        }
        catch { /* best-effort */ }
    }

    private static Dictionary<string, CollectionNode> BuildNodeIndex(FolderNode folder)
    {
        var dict = new Dictionary<string, CollectionNode>(StringComparer.Ordinal);
        IndexNode(dict, folder);
        return dict;
    }

    private static void IndexNode(Dictionary<string, CollectionNode> dict, CollectionNode node)
    {
        dict[node.Id] = node;
        if (node is FolderNode f)
            foreach (var child in f.Children)
                IndexNode(dict, child);
    }

    private static void RefreshSourceNodes(CollectionNodeViewModel vm, Dictionary<string, CollectionNode> index)
    {
        if (index.TryGetValue(vm.Id, out var node))
            vm.SourceNode = node;
        foreach (var child in vm.Children)
            RefreshSourceNodes(child, index);
    }
    public ObservableCollection<CollectionNodeViewModel> Items { get; }

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

    public ReactiveCommand<Unit, Unit> NewRequestCommand { get; }
    public ReactiveCommand<Unit, Unit> NewFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> RenameCommand { get; }
    public ReactiveCommand<Unit, Unit> CommitRenameCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelRenameCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> RunCollectionCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    private CollectionNodeViewModel BuildNodeVm(CollectionNode node)
    {
        switch (node)
        {
            case FolderNode f:
                var folderVm = new CollectionNodeViewModel(
                    f.Id, f.Name, isFolder: true, method: null,
                    f.Children.OrderBy(c => c is FolderNode ? 0 : 1).Select(BuildNodeVm), sourceNode: f)
                {
                    NewRequestCommand = ReactiveCommand.CreateFromTask(() => AddNewRequestToFolderAsync(f.Id)),
                    NewFolderCommand = ReactiveCommand.CreateFromTask(() => AddNewFolderToFolderAsync(f.Id)),
                    CopyFolderCommand = ReactiveCommand.CreateFromTask(() => CopyFolderAsync(f.Id)),
                    DeleteFolderCommand = ReactiveCommand.CreateFromTask(() => DeleteFolderAsync(f.Id)),
                    RunFolderCallback = _onFolderRunRequested,
                };
                folderVm.RenameFolderCommand = ReactiveCommand.Create(() => StartEditNode(folderVm));
                folderVm.CommitRenameCommand = ReactiveCommand.CreateFromTask(() => CommitRenameFolderAsync(folderVm, f.Id));
                folderVm.CancelRenameCommand = ReactiveCommand.Create(() => folderVm.CancelEdit());
                return folderVm;

            case RequestNode r:
                var requestVm = new CollectionNodeViewModel(
                    r.Id, r.Name, isFolder: false,
                    method: r.Method.ToString().ToUpperInvariant(),
                    children: [], sourceNode: r)
                {
                    RequestOpenedCallback = _onRequestOpened,
                    DuplicateRequestCommand = ReactiveCommand.CreateFromTask(() => DuplicateRequestAsync(r.Id)),
                    DeleteRequestCommand = ReactiveCommand.CreateFromTask(() => DeleteRequestAsync(r.Id)),
                };
                requestVm.RenameRequestCommand = ReactiveCommand.Create(() => StartEditNode(requestVm));
                requestVm.CommitRenameCommand = ReactiveCommand.CreateFromTask(() => CommitRenameRequestAsync(requestVm, r.Id));
                requestVm.CancelRenameCommand = ReactiveCommand.Create(() => requestVm.CancelEdit());
                return requestVm;

            default:
                throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}");
        }
    }

    private void StartEditNode(CollectionNodeViewModel vm)
    {
        vm.BeginEdit();
        _onEditingStarted?.Invoke(vm);
    }

    private void BeginCollectionEdit()
    {
        EditingName = _collection.Name;
        IsEditing = true;
        _onEditingStarted?.Invoke(this);
    }

    private void CancelRenameCollection()
    {
        IsEditing = false;
        EditingName = string.Empty;
    }

    private async Task CommitRenameCollectionAsync()
    {
        var newName = EditingName.Trim();
        IsEditing = false;
        EditingName = string.Empty;

        if (string.IsNullOrWhiteSpace(newName) || newName == _collection.Name) return;

        var safeName = string.Concat(newName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(safeName)) return;

        var dirPath = Path.GetDirectoryName(_collection.FilePath) ?? string.Empty;
        var newPath = Path.Combine(dirPath, safeName + ".json");

        var candidate = _collection with { Name = newName, FilePath = newPath };
        await _store.SaveAsync(newPath, candidate);
        if (!string.Equals(_collection.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
            try { File.Delete(_collection.FilePath); } catch { /* best-effort */ }

        _collection = candidate;
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(FilePath));
    }

    private async Task CommitRenameFolderAsync(CollectionNodeViewModel vm, string folderId)
    {
        var newName = vm.EditingName.Trim();
        vm.CancelEdit();
        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

        var (newRoot, found) = RenameNodeInTree(_collection.Root, folderId, newName);
        if (!found) return;

        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        vm.Name = newName;
    }

    private async Task CommitRenameRequestAsync(CollectionNodeViewModel vm, string requestId)
    {
        var newName = vm.EditingName.Trim();
        vm.CancelEdit();
        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

        var (newRoot, found) = RenameNodeInTree(_collection.Root, requestId, newName);
        if (!found) return;

        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        vm.Name = newName;
    }

    private async Task AddNewRequestAsync()
    {
        var id = Guid.NewGuid().ToString();
        var newNode = new RequestNode(id, "New Request", _collection.FilePath, HttpMethod.Get, string.Empty,
            new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>());
        var newChildren = _collection.Root.Children.Append(newNode).ToList();
        var candidate = _collection with { Root = _collection.Root with { Children = newChildren } };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        InsertNodeVmSorted(Items, BuildNodeVm(newNode));
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
        InsertNodeVmSorted(Items, BuildNodeVm(newFolder));
    }

    private async Task AddNewRequestToFolderAsync(string folderId)
    {
        var id = Guid.NewGuid().ToString();
        var newNode = new RequestNode(id, "New Request", _collection.FilePath, HttpMethod.Get, string.Empty,
            new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>());
        var (newRoot, found) = InsertIntoFolder(_collection.Root, folderId, newNode);
        if (!found) return;
        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;
        var folderVm = FindNodeVm(Items, folderId);
        if (folderVm is not null) InsertNodeVmSorted(folderVm.Children, BuildNodeVm(newNode));
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
        var folderVm = FindNodeVm(Items, folderId);
        if (folderVm is not null) InsertNodeVmSorted(folderVm.Children, BuildNodeVm(newFolder));
    }

    private async Task CopyFolderAsync(string folderId)
    {
        var source = FindFolderNode(_collection.Root, folderId);
        if (source is null) return;

        var copyName = source.Name + " - copy";
        var copy = DeepCopyFolder(source, copyName);

        var parentId = FindParentId(_collection.Root, folderId);
        if (parentId is null) return;

        var (newRoot, found) = InsertIntoFolder(_collection.Root, parentId, copy);
        if (!found) return;

        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;

        var copyVm = BuildNodeVm(copy);
        if (parentId == _collection.Root.Id)
            InsertNodeVmSorted(Items, copyVm);
        else
        {
            var parentVm = FindNodeVm(Items, parentId);
            if (parentVm is not null) InsertNodeVmSorted(parentVm.Children, copyVm);
        }
    }

    private async Task DeleteFolderAsync(string folderId)
    {
        var folder = FindFolderNode(_collection.Root, folderId);
        if (folder is null) return;

        var confirmed = await _dialog.ConfirmAsync("Delete Folder", $"Delete '{folder.Name}' and all its contents?");
        if (!confirmed) return;

        var (newRoot, found) = RemoveFromTree(_collection.Root, folderId);
        if (!found) return;

        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;

        RemoveNodeVm(Items, folderId);
    }

    private async Task DuplicateRequestAsync(string requestId)
    {
        var original = FindRequestNode(_collection.Root, requestId);
        if (original is null) return;

        var copy = original with { Id = Guid.NewGuid().ToString(), Name = original.Name + " - copy" };
        var parentId = FindParentId(_collection.Root, requestId);
        if (parentId is null) return;

        var (newRoot, found) = InsertIntoFolder(_collection.Root, parentId, copy);
        if (!found) return;

        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;

        var copyVm = BuildNodeVm(copy);
        if (parentId == _collection.Root.Id)
            InsertNodeVmSorted(Items, copyVm);
        else
        {
            var parentVm = FindNodeVm(Items, parentId);
            if (parentVm is not null) InsertNodeVmSorted(parentVm.Children, copyVm);
        }

        _onRequestOpened(copy);
    }

    private async Task DeleteRequestAsync(string requestId)
    {
        var node = FindRequestNode(_collection.Root, requestId);
        if (node is null) return;

        var confirmed = await _dialog.ConfirmAsync("Delete Request", $"Delete '{node.Name}'?");
        if (!confirmed) return;

        var (newRoot, found) = RemoveFromTree(_collection.Root, requestId);
        if (!found) return;

        var candidate = _collection with { Root = newRoot };
        await _store.SaveAsync(candidate.FilePath, candidate);
        _collection = candidate;

        RemoveNodeVm(Items, requestId);
    }

    private async Task DuplicateCollectionAsync()
    {
        var copyName = _collection.Name + " - copy";
        var dirPath = Path.GetDirectoryName(_collection.FilePath) ?? string.Empty;
        var safeName = string.Concat(copyName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        var newPath = Path.Combine(dirPath, safeName + ".json");

        var newRoot = DeepCopyCollectionTree(_collection.Root, newPath);
        var copy = new Collection(copyName, newPath, newRoot);
        await _store.SaveAsync(newPath, copy);

        _onDuplicated?.Invoke(copy);
    }

    private async Task DeleteCollectionAsync()
    {
        var confirmed = await _dialog.ConfirmAsync(
            "Delete Collection",
            $"Delete '{_collection.Name}'? This permanently deletes the file.");
        if (!confirmed) return;

        try { File.Delete(_collection.FilePath); } catch { /* best-effort */ }
        _onDeleted?.Invoke(this);
    }

    private static RequestNode? FindRequestNode(FolderNode current, string id)
    {
        foreach (var child in current.Children)
        {
            if (child is RequestNode r && r.Id == id) return r;
            if (child is FolderNode f)
            {
                var found = FindRequestNode(f, id);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static FolderNode DeepCopyCollectionTree(FolderNode source, string newCollectionPath)
    {
        var newChildren = source.Children.Select<CollectionNode, CollectionNode>(child => child switch
        {
            FolderNode f => DeepCopyCollectionTree(f, newCollectionPath),
            RequestNode r => r with { Id = Guid.NewGuid().ToString(), CollectionFilePath = newCollectionPath },
            _ => child
        }).ToList();
        return new FolderNode(Guid.NewGuid().ToString(), source.Name, newChildren);
    }

    private static void InsertNodeVmSorted(ObservableCollection<CollectionNodeViewModel> items, CollectionNodeViewModel vm)
    {
        if (vm.IsFolder)
        {
            var idx = 0;
            while (idx < items.Count && items[idx].IsFolder) idx++;
            items.Insert(idx, vm);
        }
        else
        {
            items.Add(vm);
        }
    }

    private static FolderNode? FindFolderNode(FolderNode current, string id)
    {
        if (current.Id == id) return current;
        foreach (var child in current.Children)
        {
            if (child is FolderNode f)
            {
                var found = FindFolderNode(f, id);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static string? FindParentId(FolderNode current, string targetId)
    {
        foreach (var child in current.Children)
        {
            if (child.Id == targetId) return current.Id;
            if (child is FolderNode f)
            {
                var found = FindParentId(f, targetId);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static (FolderNode Root, bool Found) RenameNodeInTree(FolderNode current, string targetId, string newName)
    {
        if (current.Id == targetId)
            return (current with { Name = newName }, true);

        var changed = false;
        var newChildren = current.Children.Select(child =>
        {
            if (changed) return child;
            if (child.Id == targetId)
            {
                changed = true;
                return child switch
                {
                    FolderNode f => f with { Name = newName },
                    RequestNode r => r with { Name = newName },
                    _ => child
                };
            }
            if (child is FolderNode folder)
            {
                var (updated, found) = RenameNodeInTree(folder, targetId, newName);
                if (found) { changed = true; return updated; }
            }
            return child;
        }).ToList();

        return changed ? (current with { Children = newChildren }, true) : (current, false);
    }

    private static (FolderNode Root, bool Found) RemoveFromTree(FolderNode current, string targetId)
    {
        var directRemoval = current.Children.Any(c => c.Id == targetId);
        if (directRemoval)
            return (current with { Children = current.Children.Where(c => c.Id != targetId).ToList() }, true);

        var changed = false;
        var newChildren = current.Children.Select(child =>
        {
            if (changed || child is not FolderNode f) return child;
            var (updated, found) = RemoveFromTree(f, targetId);
            if (!found) return child;
            changed = true;
            return updated;
        }).ToList();

        return changed ? (current with { Children = newChildren }, true) : (current, false);
    }

    private static FolderNode DeepCopyFolder(FolderNode source, string newName)
    {
        var newChildren = source.Children.Select<CollectionNode, CollectionNode>(child => child switch
        {
            FolderNode f => DeepCopyFolder(f, f.Name),
            RequestNode r => r with { Id = Guid.NewGuid().ToString() },
            _ => child
        }).ToList();
        return new FolderNode(Guid.NewGuid().ToString(), newName, newChildren);
    }

    private static void RemoveNodeVm(ObservableCollection<CollectionNodeViewModel> nodes, string id)
    {
        var target = nodes.FirstOrDefault(n => n.Id == id);
        if (target is not null) { nodes.Remove(target); return; }
        foreach (var node in nodes)
            RemoveNodeVm(node.Children, id);
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
            return updated;
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
