using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Persistence;
using Environment = PacketPanda.Core.Environments.Environment;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class ShellViewModel : ViewModelBase, IDisposable
{
    private static readonly EnvironmentRef NoEnvironmentSentinel = new("No environment", string.Empty);

    private readonly Func<RequestEditorViewModel> _editorFactory;
    private readonly IDialogService _dialog;
    private readonly IFilePickerService _filePicker;
    private readonly IEnvironmentFileStore _environmentStore;
    private readonly ISettingsStore _settings;
    private readonly Dictionary<TabViewModel, IDisposable> _closeSubscriptions = new();
    private readonly BehaviorSubject<Environment?> _activeEnvSubject = new(null);
    private readonly IDisposable _envOpenSub;
    private readonly IDisposable _newEnvCreatedSub;
    private readonly IDisposable _activateEnvSub;
    private readonly IDisposable _envRenamedSub;
    private readonly IDisposable _envDeletedSub;
    private readonly IDisposable _envAddedSub;
    private readonly IDisposable _requestOpenedSub;

    private TabViewModel? _activeTab;
    private EnvironmentRef? _selectedEnvironment;
    private SidebarTab _activeSidebarTab = SidebarTab.Requests;
    private string? _pendingActiveEnvName;

    private EnvironmentRef? _globalsRef;
    private Environment? _globals;
    private Environment? _loadedActiveEnv;

    public ShellViewModel(
        CollectionSidebarViewModel sidebar,
        Func<RequestEditorViewModel> editorFactory,
        IDialogService dialog,
        IEnvironmentFileStore environmentStore,
        IFilePickerService filePicker,
        ISettingsStore settings)
    {
        Sidebar = sidebar;
        _editorFactory = editorFactory;
        _dialog = dialog;
        _filePicker = filePicker;
        _environmentStore = environmentStore;
        _settings = settings;

        Tabs = new ObservableCollection<TabViewModel>();
        EnvironmentsForPicker = new ObservableCollection<EnvironmentRef> { NoEnvironmentSentinel };

        NewTabCommand = ReactiveCommand.Create(AddBlankTab);
        CloseActiveTabCommand = ReactiveCommand.Create(
            () =>
            {
                if (ActiveTab is not null)
                    ActiveTab.CloseCommand.Execute().Subscribe();
            });
        SetRequestsTabCommand = ReactiveCommand.Create(() => { ActiveSidebarTab = SidebarTab.Requests; });
        SetEnvironmentsTabCommand = ReactiveCommand.Create(() => { ActiveSidebarTab = SidebarTab.Environments; });
        SaveActiveTabCommand = ReactiveCommand.CreateFromTask(SaveActiveTabAsync);

        EnvironmentSidebar = new EnvironmentSidebarViewModel(
            environmentStore,
            dialog,
            filePicker,
            () => null);

        _requestOpenedSub = sidebar.RequestOpened.Subscribe(node => _ = OpenRequestNodeInTabAsync(node));

        _envOpenSub = EnvironmentSidebar.EnvironmentOpenRequested
            .Subscribe(path => _ = OpenEnvironmentInTabAsync(path));

        _newEnvCreatedSub = EnvironmentSidebar.NewEnvironmentCreated
            .Subscribe(ev => _ = OpenNewEnvironmentInTabAsync(ev.Name, ev.FilePath));

        _activateEnvSub = EnvironmentSidebar.ActivateRequested
            .Subscribe(name =>
            {
                SelectedEnvironment = EnvironmentsForPicker.FirstOrDefault(e => e.Name == name)
                    ?? NoEnvironmentSentinel;
            });

        _envRenamedSub = EnvironmentSidebar.EnvironmentRenamed
            .Subscribe(rename => HandleEnvironmentRenamed(rename.OldRef, rename.NewRef));

        _envDeletedSub = EnvironmentSidebar.EnvironmentDeleted
            .Subscribe(HandleEnvironmentDeleted);

        _envAddedSub = EnvironmentSidebar.EnvironmentAdded
            .Subscribe(added =>
            {
                if (!EnvironmentsForPicker.Any(e => e.FilePath == added.FilePath))
                {
                    EnvironmentsForPicker.Add(added);
                    _ = PersistKnownEnvironmentPathsAsync();
                }
            });
    }

    public SidebarTab ActiveSidebarTab
    {
        get => _activeSidebarTab;
        set => this.RaiseAndSetIfChanged(ref _activeSidebarTab, value);
    }

    public CollectionSidebarViewModel Sidebar { get; }
    public EnvironmentSidebarViewModel EnvironmentSidebar { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }
    public ObservableCollection<EnvironmentRef> EnvironmentsForPicker { get; }

    // Kept for backward compat with MainWindow binding
    public ObservableCollection<EnvironmentRef> AvailableEnvironments => EnvironmentsForPicker;

    public IObservable<Environment?> ActiveEnvironmentObservable => _activeEnvSubject;

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set => this.RaiseAndSetIfChanged(ref _activeTab, value);
    }

    public EnvironmentRef? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEnvironment, value);
            _ = OnSelectedEnvironmentChangedAsync(value);
        }
    }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseActiveTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetRequestsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetEnvironmentsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveActiveTabCommand { get; }

    public async Task InitializeAsync(AppSettings settings)
    {
        SetPendingActiveEnvironmentName(settings.ActiveEnvironmentName);

        var paths = settings.OpenCollectionPaths ?? [];
        if (paths.Count > 0)
            await Sidebar.LoadCollectionsAsync(paths);

        var envPaths = settings.KnownEnvironmentPaths ?? [];
        if (envPaths.Count > 0)
        {
            var refs = new List<EnvironmentRef>();
            foreach (var p in envPaths.Where(File.Exists))
            {
                try
                {
                    var env = await _environmentStore.LoadAsync(p);
                    refs.Add(new EnvironmentRef(env.Name, p));
                }
                catch
                {
                    refs.Add(new EnvironmentRef(Path.GetFileNameWithoutExtension(p), p));
                }
            }
            if (refs.Count > 0)
            {
                EnvironmentSidebar.SetEnvironments(refs);
                foreach (var r in refs)
                    if (!EnvironmentsForPicker.Any(e => e.FilePath == r.FilePath))
                        EnvironmentsForPicker.Add(r);
            }
        }

        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");
        await EnsureGlobalsAsync(globalsPath);
    }

    private async Task EnsureGlobalsAsync(string globalsPath)
    {
        _globalsRef = new EnvironmentRef("Globals", globalsPath);
        EnvironmentSidebar.SetGlobalsRef(_globalsRef);

        if (!File.Exists(globalsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(globalsPath)!);
            var empty = new Environment("Globals", globalsPath,
                new Dictionary<string, string>(StringComparer.Ordinal));
            try { await _environmentStore.SaveAsync(globalsPath, empty); }
            catch { /* best-effort */ }
        }

        try { _globals = await _environmentStore.LoadAsync(globalsPath); }
        catch { _globals = null; }

        await PublishMergedEnvAsync();
    }

    private async Task SaveActiveTabAsync()
    {
        var content = ActiveTab?.Content;
        if (content is RequestEditorViewModel req)
            await req.SaveCommand.Execute();
        else if (content is EnvironmentEditorViewModel env)
            await env.SaveCommand.Execute();
    }

    private void AddBlankTab()
    {
        var editor = _editorFactory();
        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private Task OpenRequestNodeInTabAsync(RequestNode node)
    {
        var existing = Tabs.FirstOrDefault(t => t.Content is RequestEditorViewModel r && r.RequestId == node.Id);
        if (existing is not null) { ActiveTab = existing; return Task.CompletedTask; }

        var editor = _editorFactory();
        editor.RequestId = node.Id;
        editor.RequestName = node.Name;
        editor.CollectionFilePath = node.CollectionFilePath;
        editor.SelectedMethod = node.Method;
        editor.Url = node.Url;
        editor.RequestBody = node.Body;
        editor.MarkSaved();

        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
        return Task.CompletedTask;
    }

    public Task OpenNewEnvironmentInTabAsync(string name, string filePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null) { ActiveTab = existing; return Task.CompletedTask; }

        var envEditor = new EnvironmentEditorViewModel(_environmentStore, _dialog, _filePicker);
        var env = new Environment(name, filePath,
            new Dictionary<string, string>(StringComparer.Ordinal));
        envEditor.Load(env);
        envEditor.MarkAsNew();

        var isFirstSave = true;
        var tab = new TabViewModel(envEditor, _dialog);
        var savedSub = envEditor.Saved.Subscribe(savedEnv =>
        {
            if (isFirstSave)
            {
                isFirstSave = false;
                EnvironmentSidebar.DiscardPendingEnvironment(filePath);
                EnvironmentSidebar.CommitEnvironment(savedEnv.FilePath, savedEnv.Name);
                EnvironmentsForPicker.Add(new EnvironmentRef(savedEnv.Name, savedEnv.FilePath));
                _ = PersistKnownEnvironmentPathsAsync();
            }
            else
            {
                SyncEnvRefOnSave(savedEnv);
            }
            if (_globalsRef?.FilePath == savedEnv.FilePath) { _globals = savedEnv; _ = PublishMergedEnvAsync(); }
            else if (SelectedEnvironment?.FilePath == savedEnv.FilePath) { _loadedActiveEnv = savedEnv; _ = PublishMergedEnvAsync(); }
        });
        _closeSubscriptions[tab] = new CompositeDisposable(
            tab.CloseRequested.Subscribe(_ =>
            {
                if (isFirstSave) EnvironmentSidebar.DiscardPendingEnvironment(filePath);
                CloseTab(tab);
            }),
            savedSub);
        Tabs.Add(tab);
        ActiveTab = tab;
        return Task.CompletedTask;
    }

    public async Task OpenEnvironmentInTabAsync(string filePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var envEditor = new EnvironmentEditorViewModel(_environmentStore, _dialog, _filePicker);
        try
        {
            var env = await _environmentStore.LoadAsync(filePath);
            envEditor.Load(env);
        }
        catch (EnvironmentFileException ex)
        {
            envEditor.ErrorMessage = ex.Message;
        }

        var tab = new TabViewModel(envEditor, _dialog);
        var savedSub = envEditor.Saved.Subscribe(env =>
        {
            if (_globalsRef?.FilePath == env.FilePath)
            {
                _globals = env;
                _ = PublishMergedEnvAsync();
            }
            else if (SelectedEnvironment?.FilePath == env.FilePath)
            {
                _loadedActiveEnv = env;
                _ = PublishMergedEnvAsync();
            }
            SyncEnvRefOnSave(env);
        });
        _closeSubscriptions[tab] = new CompositeDisposable(
            tab.CloseRequested.Subscribe(_ => CloseTab(tab)),
            savedSub);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    public void SetPendingActiveEnvironmentName(string? name) => _pendingActiveEnvName = name;

    private void HandleEnvironmentRenamed(EnvironmentRef oldRef, EnvironmentRef newRef)
    {
        // Update picker
        var pickerIdx = EnvironmentsForPicker.IndexOf(oldRef);
        if (pickerIdx >= 0) EnvironmentsForPicker[pickerIdx] = newRef;

        // Update SelectedEnvironment if it pointed to the renamed env
        if (_selectedEnvironment?.FilePath == oldRef.FilePath)
        {
            _selectedEnvironment = newRef;
            this.RaisePropertyChanged(nameof(SelectedEnvironment));
        }

        // Close old tab and open new one
        var oldTab = Tabs.FirstOrDefault(t => t.FilePath == oldRef.FilePath);
        if (oldTab is not null)
        {
            CloseTab(oldTab);
            _ = OpenEnvironmentInTabAsync(newRef.FilePath);
        }
    }

    private void HandleEnvironmentDeleted(EnvironmentRef deleted)
    {
        var pickerEntry = EnvironmentsForPicker.FirstOrDefault(e => e.FilePath == deleted.FilePath);
        if (pickerEntry is not null)
        {
            EnvironmentsForPicker.Remove(pickerEntry);
            _ = PersistKnownEnvironmentPathsAsync();
        }

        if (_selectedEnvironment?.FilePath == deleted.FilePath)
            SelectedEnvironment = NoEnvironmentSentinel;

        var openTab = Tabs.FirstOrDefault(t => t.FilePath == deleted.FilePath);
        if (openTab is not null)
            CloseTab(openTab);
    }

    private async Task OnSelectedEnvironmentChangedAsync(EnvironmentRef? envRef)
    {
        var isSentinel = envRef is null || envRef.FilePath == string.Empty;

        if (isSentinel)
        {
            _loadedActiveEnv = null;
            await PersistActiveEnvNameAsync(null);
            await PublishMergedEnvAsync();
            return;
        }

        try
        {
            var env = await _environmentStore.LoadAsync(envRef!.FilePath);
            _loadedActiveEnv = env;
            await PersistActiveEnvNameAsync(envRef.Name);
        }
        catch (EnvironmentFileException)
        {
            _loadedActiveEnv = null;
        }

        await PublishMergedEnvAsync();
    }

    /// <summary>
    /// Updates a variable in whichever source environment currently provides it
    /// (active env wins over globals because it shadows it in the merged view), then
    /// republishes the merged environment so colorizers and the variable resolver pick
    /// up the new value.  If the variable isn't defined anywhere, it's added to the
    /// active env (or globals if no active env is selected).
    /// </summary>
    public async Task UpdateActiveEnvironmentVariableAsync(string name, string value, VariableTarget target = VariableTarget.ActiveEnv)
    {
        Environment? source;
        bool isGlobals;

        if (target == VariableTarget.Globals)
        {
            if (_globals is null) return;
            source = _globals;
            isGlobals = true;
        }
        else
        {
            // Auto-detect: active env shadows globals
            if (_loadedActiveEnv is not null && _loadedActiveEnv.Variables.ContainsKey(name))
            {
                source = _loadedActiveEnv;
                isGlobals = false;
            }
            else if (_globals is not null && _globals.Variables.ContainsKey(name))
            {
                source = _globals;
                isGlobals = true;
            }
            else if (_loadedActiveEnv is not null)
            {
                source = _loadedActiveEnv;
                isGlobals = false;
            }
            else if (_globals is not null)
            {
                source = _globals;
                isGlobals = true;
            }
            else
            {
                return;
            }
        }

        var newVars = new Dictionary<string, string>(source.Variables, StringComparer.Ordinal)
        {
            [name] = value,
        };
        var updated = source with { Variables = newVars };

        try
        {
            await _environmentStore.SaveAsync(updated.FilePath, updated);
            if (isGlobals) _globals = updated;
            else _loadedActiveEnv = updated;
            await PublishMergedEnvAsync();
        }
        catch (EnvironmentFileException)
        {
            // best-effort — leave in-memory state alone if save failed
        }
    }

    private Task PublishMergedEnvAsync()
    {
        var merged = MergeEnvironments(_globals, _loadedActiveEnv);
        _activeEnvSubject.OnNext(merged);
        return Task.CompletedTask;
    }

    private static Environment? MergeEnvironments(Environment? globals, Environment? active)
    {
        if (globals is null && active is null) return null;

        var mergedVars = new Dictionary<string, string>(StringComparer.Ordinal);

        if (globals is not null)
        {
            var disabledGlobals = globals.DisabledVariables is not null
                ? new HashSet<string>(globals.DisabledVariables, StringComparer.Ordinal)
                : null;
            foreach (var kvp in globals.Variables)
                if (disabledGlobals?.Contains(kvp.Key) != true)
                    mergedVars[kvp.Key] = kvp.Value;
        }

        if (active is not null)
        {
            var disabledActive = active.DisabledVariables is not null
                ? new HashSet<string>(active.DisabledVariables, StringComparer.Ordinal)
                : null;
            foreach (var kvp in active.Variables)
                if (disabledActive?.Contains(kvp.Key) != true)
                    mergedVars[kvp.Key] = kvp.Value;
        }

        var name = active?.Name ?? globals?.Name ?? "Merged";
        var filePath = active?.FilePath ?? globals?.FilePath ?? string.Empty;
        return new Environment(name, filePath, mergedVars);
    }

    private void SyncEnvRefOnSave(PacketPanda.Core.Environments.Environment env)
    {
        if (_globalsRef?.FilePath == env.FilePath) return;

        EnvironmentSidebar.UpdateEnvironmentRef(env.FilePath, env.Name);

        var pickerIdx = EnvironmentsForPicker.IndexOf(
            EnvironmentsForPicker.FirstOrDefault(e => e.FilePath == env.FilePath)!);
        if (pickerIdx >= 0)
        {
            EnvironmentsForPicker[pickerIdx] = new EnvironmentRef(env.Name, env.FilePath);
            if (_selectedEnvironment?.FilePath == env.FilePath)
            {
                _selectedEnvironment = EnvironmentsForPicker[pickerIdx];
                this.RaisePropertyChanged(nameof(SelectedEnvironment));
            }
        }
    }

    private async Task PersistKnownEnvironmentPathsAsync()
    {
        try
        {
            var paths = EnvironmentsForPicker
                .Where(e => !string.IsNullOrEmpty(e.FilePath))
                .Select(e => e.FilePath)
                .ToList();
            var current = await _settings.LoadAsync();
            await _settings.SaveAsync(current with { KnownEnvironmentPaths = paths });
        }
        catch (SettingsException) { }
    }

    private async Task PersistActiveEnvNameAsync(string? name)
    {
        try
        {
            var current = await _settings.LoadAsync();
            await _settings.SaveAsync(current with { ActiveEnvironmentName = name });
        }
        catch (SettingsException) { }
    }

    public void Dispose()
    {
        _envOpenSub.Dispose();
        _newEnvCreatedSub.Dispose();
        _activateEnvSub.Dispose();
        _envRenamedSub.Dispose();
        _envDeletedSub.Dispose();
        _envAddedSub.Dispose();
        _requestOpenedSub.Dispose();
        foreach (var sub in _closeSubscriptions.Values)
            sub.Dispose();
        _closeSubscriptions.Clear();
        _activeEnvSubject.Dispose();
    }

    private void CloseTab(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        var wasActive = ReferenceEquals(ActiveTab, tab);
        Tabs.RemoveAt(index);

        if (_closeSubscriptions.Remove(tab, out var sub))
            sub.Dispose();

        if (!wasActive) return;

        if (Tabs.Count == 0)
            ActiveTab = null;
        else
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }
}
