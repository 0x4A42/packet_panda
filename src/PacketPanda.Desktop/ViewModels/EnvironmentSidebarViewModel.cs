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

    private readonly Subject<string> _environmentOpenRequested = new();
    private readonly Subject<(string Name, string FilePath)> _newEnvironmentCreated = new();
    private readonly Subject<string> _activateRequested = new();
    private readonly Subject<(EnvironmentRef OldRef, EnvironmentRef NewRef)> _environmentRenamed = new();

    private readonly ObservableCollection<EnvironmentRef> _allItems = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeEnvironmentName;
    private string _searchText = string.Empty;
    private EnvironmentRef? _globalsRef;

    public ObservableCollection<EnvironmentRef> Items { get; } = new();
    public IObservable<string> EnvironmentOpenRequested => _environmentOpenRequested;
    public IObservable<(string Name, string FilePath)> NewEnvironmentCreated => _newEnvironmentCreated;
    public IObservable<string> ActivateRequested => _activateRequested;
    public IObservable<(EnvironmentRef OldRef, EnvironmentRef NewRef)> EnvironmentRenamed => _environmentRenamed;

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

    public ReactiveCommand<Unit, Unit> NewEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> DeleteEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> OpenEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> ActivateEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> RenameEnvironmentCommand { get; }
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
        RenameEnvironmentCommand = ReactiveCommand.CreateFromTask<EnvironmentRef>(RenameEnvironmentAsync);
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
                Items.Add(e);
        }
    }

    private async Task OpenEnvironmentFileAsync()
    {
        var path = await _picker.PickOpenAsync();
        if (path is not null)
            _environmentOpenRequested.OnNext(path);
    }

    private async Task NewEnvironmentAsync()
    {
        var root = _getWorkspaceRoot();
        if (root is null)
        {
            await _dialog.ConfirmAsync("No workspace open",
                "Open a workspace folder first by clicking 'Open…' in the Requests tab.");
            return;
        }

        var envDir = Path.Combine(root, "environments");
        Directory.CreateDirectory(envDir);

        const string baseName = "New Environment";
        var name = baseName;
        var counter = 2;
        var safeName = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        var filePath = Path.Combine(envDir, $"{safeName}.toml");

        while (_allItems.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                   i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
               || _pendingPaths.Contains(filePath)
               || File.Exists(filePath))
        {
            name = $"{baseName} {counter++}";
            safeName = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            filePath = Path.Combine(envDir, $"{safeName}.toml");
        }

        _pendingPaths.Add(filePath);
        _newEnvironmentCreated.OnNext((name, filePath));
    }

    private async Task RenameEnvironmentAsync(EnvironmentRef envRef)
    {
        var newName = await _dialog.PromptTextAsync("Rename Environment", "New name");
        if (string.IsNullOrWhiteSpace(newName) || newName == envRef.Name) return;

        var safeName = string.Concat(newName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(safeName)) return;

        var dir = Path.GetDirectoryName(envRef.FilePath)!;
        var newPath = Path.Combine(dir, $"{safeName}.toml");

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
        var newName = await _dialog.PromptTextAsync("Duplicate Environment", "New environment name");
        if (string.IsNullOrWhiteSpace(newName)) return;

        var safeName = string.Concat(newName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(safeName)) return;

        var dir = Path.GetDirectoryName(envRef.FilePath)!;
        var newPath = Path.Combine(dir, $"{safeName}.toml");

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
            ApplyFilter();
        }
    }
}
