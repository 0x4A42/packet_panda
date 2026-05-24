using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using PacketPanda.Core.Environments;
using PacketPanda.Core.History;
using PacketPanda.Core.Http;
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
    private readonly BehaviorSubject<EnvironmentContext?> _activeEnvSubject = new(null);
    private readonly IDisposable _envOpenSub;
    private readonly IDisposable _newEnvCreatedSub;
    private readonly IDisposable _activateEnvSub;
    private readonly IDisposable _envRenamedSub;
    private readonly IDisposable _envDeletedSub;
    private readonly IDisposable _envAddedSub;
    private readonly IDisposable _requestOpenedSub;
    private readonly IDisposable? _folderRunSub;
    private readonly IDisposable _docOpenSub;
    private readonly IDisposable _collectionEnvFoundSub;
    private readonly IDisposable _historyOpenSub;
    private readonly IDisposable _pasteCurlSub;

    private TabViewModel? _activeTab;
    private EnvironmentRef? _selectedEnvironment;
    private SidebarTab _activeSidebarTab = SidebarTab.Requests;
    private string? _pendingActiveEnvName;
    private bool _isDarkTheme;

    private EnvironmentRef? _globalsRef;
    private Environment? _globals;
    private Environment? _loadedActiveEnv;

    public ShellViewModel(
        CollectionSidebarViewModel sidebar,
        Func<RequestEditorViewModel> editorFactory,
        IDialogService dialog,
        IEnvironmentFileStore environmentStore,
        IFilePickerService filePicker,
        ISettingsStore settings,
        IHistoryStore historyStore,
        Func<FolderNode, CollectionRunnerViewModel>? runnerFactory = null)
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
        SetHistoryTabCommand = ReactiveCommand.Create(() => { ActiveSidebarTab = SidebarTab.History; });
        SetDocsTabCommand = ReactiveCommand.Create(() => { ActiveSidebarTab = SidebarTab.Docs; });
        SaveActiveTabCommand = ReactiveCommand.CreateFromTask(SaveActiveTabAsync);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync);

        HistorySidebar = new HistorySidebarViewModel(historyStore);
        _historyOpenSub = HistorySidebar.HistoryOpenRequested.Subscribe(OpenHistoryEntryInTab);

        EnvironmentSidebar = new EnvironmentSidebarViewModel(
            environmentStore,
            dialog,
            filePicker);

        DocsSidebar = new DocsSidebarViewModel(
            Path.Combine(AppContext.BaseDirectory, "docs"));

        _docOpenSub = DocsSidebar.DocOpenRequested.Subscribe(OpenDocInTab);

        _requestOpenedSub = sidebar.RequestOpened.Subscribe(node => _ = OpenRequestNodeInTabAsync(node));
        _pasteCurlSub = sidebar.PasteCurlRequested.Subscribe(OpenCurlImportInTab);

        _collectionEnvFoundSub = sidebar.EnvironmentFileFound
            .Subscribe(path => _ = EnvironmentSidebar.RegisterEnvironmentFromPathAsync(path));

        if (runnerFactory is not null)
        {
            _folderRunSub = sidebar.FolderRunRequested.Subscribe(folder =>
            {
                var existing = Tabs.FirstOrDefault(t => t.Content is CollectionRunnerViewModel runner
                    && runner.CurrentFilePath == folder.Id);
                if (existing is not null)
                {
                    ActiveTab = existing;
                    return;
                }
                var vm = runnerFactory(folder);
                var tab = new TabViewModel(vm, _dialog);
                _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
                Tabs.Add(tab);
                ActiveTab = tab;
            });
        }

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

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public CollectionSidebarViewModel Sidebar { get; }
    public EnvironmentSidebarViewModel EnvironmentSidebar { get; }
    public HistorySidebarViewModel HistorySidebar { get; }
    public DocsSidebarViewModel DocsSidebar { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }
    public ObservableCollection<EnvironmentRef> EnvironmentsForPicker { get; }

    // Kept for backward compat with MainWindow binding
    public ObservableCollection<EnvironmentRef> AvailableEnvironments => EnvironmentsForPicker;

    public IObservable<EnvironmentContext?> ActiveEnvironmentObservable => _activeEnvSubject;

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
            // null is only ever a transient state produced by Avalonia's ComboBox during a
            // collection Replace event — real deselection arrives as NoEnvironmentSentinel.
            // Discard null entirely so the backing field is never cleared by the transient pulse,
            // allowing HandleEnvironmentRenamed/SyncEnvRefOnSave to compare FilePath correctly.
            if (value is null) return;
            this.RaiseAndSetIfChanged(ref _selectedEnvironment, value);
            _ = OnSelectedEnvironmentChangedAsync(value);
        }
    }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseActiveTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetRequestsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetEnvironmentsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetHistoryTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetDocsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveActiveTabCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }

    public async Task InitializeAsync(AppSettings settings)
    {
        IsDarkTheme = settings.IsDarkTheme;
        if (Avalonia.Application.Current is App app) app.ApplyTheme(settings.IsDarkTheme);

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

        if (_pendingActiveEnvName is not null)
        {
            var match = EnvironmentsForPicker.FirstOrDefault(e => e.Name == _pendingActiveEnvName);
            if (match is not null)
                SelectedEnvironment = match;
            _pendingActiveEnvName = null;
        }

        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");
        await EnsureGlobalsAsync(globalsPath);
        await HistorySidebar.LoadAsync();
    }

    private async Task EnsureGlobalsAsync(string globalsPath)
    {
        _globalsRef = new EnvironmentRef("Globals", globalsPath);
        EnvironmentSidebar.SetGlobalsRef(_globalsRef);

        if (!File.Exists(globalsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(globalsPath) ?? throw new InvalidOperationException("Invalid globals path"));
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
        _closeSubscriptions[tab] = new CompositeDisposable(
            tab.CloseRequested.Subscribe(_ => CloseTab(tab)),
            WireHistoryCapture(editor));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    internal Task OpenRequestNodeInTabAsync(RequestNode node)
    {
        var existing = Tabs.FirstOrDefault(t => t.Content is RequestEditorViewModel r && r.RequestId == node.Id);
        if (existing is not null) { ActiveTab = existing; return Task.CompletedTask; }

        var editor = _editorFactory();
        editor.RequestId = node.Id;
        editor.RequestName = node.Name;
        editor.CollectionFilePath = node.CollectionFilePath;
        editor.SelectedMethod = node.Method;
        editor.Url = node.Url;
        editor.LoadBody(node.Body);
        editor.LoadHeaders(node.Headers);
        editor.LoadAuth(node.Auth);
        editor.LoadQueryParams(node.QueryParams);
        editor.LoadScripts(node.PreRequestScript, node.PostResponseScript);
        editor.MarkSaved();

        var tab = new TabViewModel(editor, _dialog);
        var savedSub = editor.SavedSuccessfully
            .Subscribe(path => _ = Sidebar.SyncCollectionFromDiskAsync(path));
        _closeSubscriptions[tab] = new CompositeDisposable(
            tab.CloseRequested.Subscribe(_ => CloseTab(tab)),
            savedSub,
            WireHistoryCapture(editor));
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

    public void OpenDocInTab(string filePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.Content is DocsViewerViewModel
            && t.FilePath == filePath);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var vm = new DocsViewerViewModel(filePath);
        var tab = new TabViewModel(vm, _dialog);
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    public void SetPendingActiveEnvironmentName(string? name) => _pendingActiveEnvName = name;

    private void HandleEnvironmentRenamed(EnvironmentRef oldRef, EnvironmentRef newRef)
    {
        // Capture before the Replace fires — the ComboBox sends a transient null through the
        // two-way binding during Replace, which we now discard in the setter, but capturing
        // here keeps the logic clear regardless of binding timing.
        var wasSelected = _selectedEnvironment?.FilePath == oldRef.FilePath;

        var pickerIdx = EnvironmentsForPicker.IndexOf(oldRef);
        if (pickerIdx >= 0) EnvironmentsForPicker[pickerIdx] = newRef;

        // Re-select via the setter so OnSelectedEnvironmentChangedAsync reloads from the new path.
        if (wasSelected)
            SelectedEnvironment = newRef;

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
            // ReSharper disable once NullableWarningSuppressionIsUsed
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
            // Explicit ActiveEnv target: always write to the active environment.
            // If the variable also exists in globals, the active env entry will shadow it,
            // which is exactly the override behaviour the caller asked for.
            if (_loadedActiveEnv is not null)
            {
                source = _loadedActiveEnv;
                isGlobals = false;
            }
            else if (_globals is not null)
            {
                // No active env loaded — fall back to globals so the value isn't silently lost.
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
        var ctx = merged is null ? null : new EnvironmentContext(merged, _globals?.Variables, _loadedActiveEnv?.Variables);
        _activeEnvSubject.OnNext(ctx);
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

    private void SyncEnvRefOnSave(Environment env)
    {
        if (_globalsRef?.FilePath == env.FilePath) return;

        EnvironmentSidebar.UpdateEnvironmentRef(env.FilePath, env.Name);

        var newRef = new EnvironmentRef(env.Name, env.FilePath);
        var existing = EnvironmentsForPicker.FirstOrDefault(e => e.FilePath == env.FilePath);
        if (existing is null) return;

        // Only replace the item when the name actually changed. An unconditional Replace fires
        // a CollectionChanged event that causes Avalonia's ComboBox to transiently clear
        // SelectedItem, which flows back through the two-way binding and wipes the active env.
        if (existing != newRef)
        {
            var pickerIdx = EnvironmentsForPicker.IndexOf(existing);
            if (pickerIdx >= 0)
                EnvironmentsForPicker[pickerIdx] = newRef;
        }

        if (_selectedEnvironment?.FilePath == env.FilePath)
        {
            _selectedEnvironment = newRef;
            this.RaisePropertyChanged(nameof(SelectedEnvironment));
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

    private async Task OpenSettingsAsync()
    {
        var current = await _settings.LoadAsync();
        var vm = new SettingsViewModel(_settings, current);
        await _dialog.ShowSettingsAsync(vm);
        var updated = await _settings.LoadAsync();
        IsDarkTheme = updated.IsDarkTheme;
    }

    private void OpenHistoryEntryInTab(HistoryEntry entry)
    {
        var editor = _editorFactory();
        editor.SelectedMethod = entry.Method;
        editor.Url = entry.Url;
        editor.LoadBody(entry.Body);
        editor.LoadHeaders(entry.Headers);
        editor.LoadAuth(entry.Auth);
        editor.LoadHistoryResponse(entry);

        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = new CompositeDisposable(
            tab.CloseRequested.Subscribe(_ => CloseTab(tab)),
            WireHistoryCapture(editor));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private IDisposable WireHistoryCapture(RequestEditorViewModel editor)
        => editor.HistoryEntryRecorded.Subscribe(HistorySidebar.AddEntry);

    private void OpenCurlImportInTab(ParsedCurlRequest parsed)
    {
        var editor = _editorFactory();
        editor.Url = parsed.Url;
        editor.SelectedMethod = parsed.Method;
        editor.LoadHeaders(parsed.Headers);
        editor.LoadBody(parsed.Body);
        editor.RequestName = "Imported from cURL";

        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = new CompositeDisposable(
            tab.CloseRequested.Subscribe(_ => CloseTab(tab)),
            WireHistoryCapture(editor));
        Tabs.Add(tab);
        ActiveTab = tab;
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
        _folderRunSub?.Dispose();
        _docOpenSub.Dispose();
        _collectionEnvFoundSub.Dispose();
        _historyOpenSub.Dispose();
        _pasteCurlSub.Dispose();
        HistorySidebar.Dispose();
        DocsSidebar.Dispose();
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
