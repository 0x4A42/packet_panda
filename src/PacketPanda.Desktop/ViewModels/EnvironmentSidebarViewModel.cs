using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using PacketPanda.Core.Environments;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Desktop.ViewModels;

public class EnvironmentSidebarViewModel : ReactiveObject
{
    private readonly IEnvironmentFileStore _store;
    private readonly IDialogService _dialog;
    private readonly Func<string?> _getWorkspaceRoot;
    private readonly IFilePickerService _picker;
    private readonly Dictionary<string, EnvironmentItemViewModel> _itemVmCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Subject<string> _environmentOpenRequested = new();
    private readonly Subject<(string Name, string FilePath)> _newEnvironmentCreated = new();
    private readonly Subject<string> _activateRequested = new();
    private readonly Subject<(EnvironmentRef OldRef, EnvironmentRef NewRef)> _environmentRenamed = new();
    private readonly Subject<EnvironmentRef> _environmentDeleted = new();
    private readonly Subject<EnvironmentRef> _environmentAdded = new();

    private readonly ObservableCollection<EnvironmentRef> _allItems = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeEnvironmentName;
    private string _searchText = string.Empty;
    private EnvironmentRef? _globalsRef;
    private EnvironmentItemViewModel? _editingItem;

    public ObservableCollection<EnvironmentItemViewModel> Items { get; } = new();
    public IObservable<string> EnvironmentOpenRequested => _environmentOpenRequested;
    public IObservable<(string Name, string FilePath)> NewEnvironmentCreated => _newEnvironmentCreated;
    public IObservable<string> ActivateRequested => _activateRequested;
    public IObservable<(EnvironmentRef OldRef, EnvironmentRef NewRef)> EnvironmentRenamed => _environmentRenamed;
    public IObservable<EnvironmentRef> EnvironmentDeleted => _environmentDeleted;
    public IObservable<EnvironmentRef> EnvironmentAdded => _environmentAdded;

    public string? ActiveEnvironmentName
    {
        get => _activeEnvironmentName;
        set => this.RaiseAndSetIfChanged(ref _activeEnvironmentName, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            ApplyFilter();
        }
    }

    public EnvironmentRef? GlobalsRef
    {
        get => _globalsRef;
        private set => this.RaiseAndSetIfChanged(ref _globalsRef, value);
    }

    public EnvironmentItemViewModel? EditingItem
    {
        get => _editingItem;
        private set => this.RaiseAndSetIfChanged(ref _editingItem, value);
    }

    public ReactiveCommand<Unit, Unit> NewEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> DeleteEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> OpenEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> ActivateEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentItemViewModel, Unit> RenameEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentItemViewModel, Unit> CommitRenameCommand { get; }
    public ReactiveCommand<EnvironmentItemViewModel, Unit> CancelRenameCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> DuplicateEnvironmentCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenEnvironmentFileCommand { get; }

    public EnvironmentSidebarViewModel(
        IEnvironmentFileStore store,
        IDialogService dialog,
        IFilePickerService picker,
        Func<string?> getWorkspaceRoot)
    {
        _store = store;
        _dialog = dialog;
        _picker = picker;
        _getWorkspaceRoot = getWorkspaceRoot;

        NewEnvironmentCommand = ReactiveCommand.CreateFromTask(NewEnvironmentAsync);
        DeleteEnvironmentCommand = ReactiveCommand.CreateFromTask<EnvironmentRef>(DeleteEnvironmentAsync);
        OpenEnvironmentCommand = ReactiveCommand.Create<EnvironmentRef>(env =>
            _environmentOpenRequested.OnNext(env.FilePath));
        ActivateEnvironmentCommand = ReactiveCommand.Create<EnvironmentRef>(env =>
            _activateRequested.OnNext(env.Name));
        RenameEnvironmentCommand = ReactiveCommand.Create<EnvironmentItemViewModel>(item =>
        {
            EditingItem?.CancelEdit();
            item.BeginEdit();
            EditingItem = item;
        });
        CommitRenameCommand = ReactiveCommand.CreateFromTask<EnvironmentItemViewModel>(CommitRenameAsync);
        CancelRenameCommand = ReactiveCommand.Create<EnvironmentItemViewModel>(item =>
        {
            item.CancelEdit();
            if (ReferenceEquals(EditingItem, item)) EditingItem = null;
        });
        DuplicateEnvironmentCommand = ReactiveCommand.CreateFromTask<EnvironmentRef>(DuplicateEnvironmentAsync);
        OpenEnvironmentFileCommand = ReactiveCommand.CreateFromTask(OpenEnvironmentFileAsync);
    }

    public void SetEnvironments(IReadOnlyList<EnvironmentRef> envs)
    {
        _allItems.Clear();
        foreach (var e in envs)
            _allItems.Add(e);
        ApplyFilter();
    }

    public void SetGlobalsRef(EnvironmentRef? globalsRef)
    {
        GlobalsRef = globalsRef;
    }

    public void UpdateEnvironmentRef(string filePath, string newName)
    {
        var existing = _allItems.FirstOrDefault(i => i.FilePath == filePath);
        if (existing is null || existing.Name == newName) return;
        var newRef = new EnvironmentRef(newName, filePath);
        var idx = _allItems.IndexOf(existing);
        if (idx >= 0) _allItems[idx] = newRef;
        if (_itemVmCache.TryGetValue(filePath, out var vm)) vm.UpdateRef(newRef);
        ApplyFilter();
    }

    public void CommitEnvironment(string filePath, string name)
    {
        _pendingPaths.Remove(filePath);
        if (!_allItems.Any(i => i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
        {
            _allItems.Add(new EnvironmentRef(name, filePath));
            ApplyFilter();
        }
        else
        {
            UpdateEnvironmentRef(filePath, name);
        }
    }

    public void DiscardPendingEnvironment(string filePath) => _pendingPaths.Remove(filePath);

    private void ApplyFilter()
    {
        Items.Clear();
        var query = _searchText.Trim();
        foreach (var e in _allItems)
        {
            if (string.IsNullOrEmpty(query) ||
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (!_itemVmCache.TryGetValue(e.FilePath, out var itemVm))
                {
                    itemVm = new EnvironmentItemViewModel(e);
                    _itemVmCache[e.FilePath] = itemVm;
                }
                Items.Add(itemVm);
            }
        }
    }

    private async Task OpenEnvironmentFileAsync()
    {
        var path = await _picker.PickOpenAsync();
        if (path is not null)
            _environmentOpenRequested.OnNext(path);
    }

    private Task NewEnvironmentAsync()
    {
        var envDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "environments");

        try { Directory.CreateDirectory(envDir); } catch { /* best-effort */ }

        const string baseName = "New Environment";
        var name = baseName;
        var counter = 2;
        var filePath = Path.Combine(envDir, $"{name}.json");

        while (_allItems.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                   i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
               || _pendingPaths.Contains(filePath)
               || File.Exists(filePath))
        {
            name = $"{baseName} {counter++}";
            filePath = Path.Combine(envDir, $"{name}.json");
        }

        _pendingPaths.Add(filePath);
        _newEnvironmentCreated.OnNext((name, filePath));
        return Task.CompletedTask;
    }

    private async Task CommitRenameAsync(EnvironmentItemViewModel item)
    {
        var newName = item.EditingName.Trim();
        var envRef = item.Ref;

        item.CancelEdit();
        if (ReferenceEquals(EditingItem, item)) EditingItem = null;

        if (string.IsNullOrWhiteSpace(newName) || newName == envRef.Name) return;

        var safeName = string.Concat(newName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(safeName)) return;

        var lastSep = envRef.FilePath.LastIndexOfAny(new[] { '/', '\\' });
        var dirPrefix = lastSep >= 0 ? envRef.FilePath[..(lastSep + 1)] : string.Empty;
        var newPath = dirPrefix + safeName + ".json";

        try
        {
            var env = await _store.LoadAsync(envRef.FilePath);
            var renamed = env with { Name = newName, FilePath = newPath };
            await _store.SaveAsync(newPath, renamed);
            if (!string.Equals(envRef.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
                File.Delete(envRef.FilePath);

            var newRef = new EnvironmentRef(newName, newPath);
            var idx = _allItems.IndexOf(envRef);
            if (idx >= 0) _allItems[idx] = newRef;

            if (_itemVmCache.Remove(envRef.FilePath, out var vm))
            {
                vm.UpdateRef(newRef);
                _itemVmCache[newRef.FilePath] = vm;
            }

            ApplyFilter();
            _environmentRenamed.OnNext((envRef, newRef));
        }
        catch (Exception ex)
        {
            await _dialog.ConfirmAsync("Rename Failed", ex.Message);
        }
    }

    private async Task DuplicateEnvironmentAsync(EnvironmentRef envRef)
    {
        const string suffix = " - duplicate";
        var newName = envRef.Name + suffix;
        var counter = 2;
        while (_allItems.Any(i => i.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            newName = envRef.Name + suffix + $" {counter++}";

        var safeName = string.Concat(newName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        var lastSep = envRef.FilePath.LastIndexOfAny(new[] { '/', '\\' });
        var dirPrefix = lastSep >= 0 ? envRef.FilePath[..(lastSep + 1)] : string.Empty;
        var newPath = dirPrefix + safeName + ".json";

        try
        {
            var env = await _store.LoadAsync(envRef.FilePath);
            var copy = env with { Name = newName, FilePath = newPath };
            await _store.SaveAsync(newPath, copy);

            var newRef = new EnvironmentRef(newName, newPath);
            if (!_allItems.Any(i => i.FilePath == newPath))
            {
                _allItems.Add(newRef);
                ApplyFilter();
                _environmentAdded.OnNext(newRef);
            }

            _environmentOpenRequested.OnNext(newPath);
        }
        catch (Exception ex)
        {
            await _dialog.ConfirmAsync("Duplicate Failed", ex.Message);
        }
    }

    private async Task DeleteEnvironmentAsync(EnvironmentRef envRef)
    {
        var confirmed = await _dialog.ConfirmAsync(
            "Delete Environment",
            $"Delete '{envRef.Name}'? This cannot be undone.");
        if (!confirmed) return;

        try { File.Delete(envRef.FilePath); } catch { /* best-effort */ }

        var existing = _allItems.FirstOrDefault(i => i.FilePath == envRef.FilePath);
        if (existing is not null)
        {
            _allItems.Remove(existing);
            _itemVmCache.Remove(envRef.FilePath);
            ApplyFilter();
        }

        _environmentDeleted.OnNext(envRef);
    }
}
