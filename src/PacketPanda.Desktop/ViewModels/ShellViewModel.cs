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
    private const string GlobalsFileName = "globals.toml";

    private readonly Func<RequestEditorViewModel> _editorFactory;
    private readonly IDialogService _dialog;
    private readonly IRequestFileStore _store;
    private readonly IFilePickerService _filePicker;
    private readonly IEnvironmentFileStore _environmentStore;
    private readonly ISettingsStore _settings;
    private readonly Dictionary<TabViewModel, IDisposable> _closeSubscriptions = new();
    private readonly BehaviorSubject<Environment?> _activeEnvSubject = new(null);
    private readonly IDisposable _envScannedSub;
    private readonly IDisposable _envOpenSub;
    private readonly IDisposable _newEnvCreatedSub;
    private readonly IDisposable _activateEnvSub;
    private readonly IDisposable _envRenamedSub;
    private readonly IDisposable _envDeletedSub;

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
        IRequestFileStore store,
        IFilePickerService filePicker,
        IEnvironmentFileStore environmentStore,
        ISettingsStore settings)
    {
        Sidebar = sidebar;
        _editorFactory = editorFactory;
        _dialog = dialog;
        _store = store;
        _filePicker = filePicker;
        _environmentStore = environmentStore;
        _settings = settings;

        Tabs = new ObservableCollection<TabViewModel>();
        EnvironmentsForPicker = new ObservableCollection<EnvironmentRef> { NoEnvironmentSentinel };

        NewTabCommand = ReactiveCommand.Create(AddBlankTab);
        OpenFileInTabCommand = ReactiveCommand.CreateFromTask<string>(OpenFileInTabAsync);
        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
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
            () => Sidebar.WorkspacePath);

        Sidebar.RequestOpened.InvokeCommand(OpenFileInTabCommand);

        _envScannedSub = sidebar.EnvironmentsScanned.Subscribe(envs =>
            _ = HandleEnvironmentsScanAsync(envs));

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
    public ReactiveCommand<string, Unit> OpenFileInTabCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseActiveTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetRequestsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SetEnvironmentsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveActiveTabCommand { get; }

    private async Task SaveActiveTabAsync()
    {
        var content = ActiveTab?.Content;
        if (content is RequestEditorViewModel req)
            await req.SaveCommand.Execute();
        else if (content is EnvironmentEditorViewModel env)
            await env.SaveCommand.Execute();
    }

    private async Task OpenFileAsync()
    {
        var path = await _filePicker.PickOpenAsync();
        if (path is null) return;

        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            await Sidebar.LoadWorkspaceAsync(dir);

        await OpenFileInTabAsync(path);
    }

    private void AddBlankTab()
    {
        var editor = _editorFactory();
        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private async Task OpenFileInTabAsync(string path)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == path);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var editor = _editorFactory();
        try
        {
            var request = await _store.LoadAsync(path);
            editor.SelectedMethod = request.Method;
            editor.Url = request.Url;
            editor.RequestBody = request.Body;
            editor.CurrentFilePath = path;
            editor.MarkSaved();
        }
        catch (RequestFileException ex)
        {
            editor.ErrorMessage = ex.Message;
            editor.CurrentFilePath = path;
        }

        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    public Task OpenNewEnvironmentInTabAsync(string name, string filePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null) { ActiveTab = existing; return Task.CompletedTask; }

        var envEditor = new EnvironmentEditorViewModel(_environmentStore, _dialog);
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

        var envEditor = new EnvironmentEditorViewModel(_environmentStore, _dialog);
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

    private async Task HandleEnvironmentsScanAsync(IReadOnlyList<EnvironmentRef> envs)
    {
        var root = Sidebar.WorkspacePath;
        if (root is not null)
            await EnsureGlobalsAsync(root, envs);

        var nonGlobals = envs
            .Where(e => !IsGlobalsFile(e.FilePath))
            .ToList();

        EnvironmentSidebar.SetGlobalsRef(_globalsRef);
        EnvironmentSidebar.SetEnvironments(nonGlobals);
        SyncEnvironmentsForPicker(nonGlobals);
        RestoreSelectedEnvironment();
    }

    private static bool IsGlobalsFile(string filePath) =>
        Path.GetFileName(filePath).Equals(GlobalsFileName, StringComparison.OrdinalIgnoreCase);

    private async Task EnsureGlobalsAsync(string workspaceRoot, IReadOnlyList<EnvironmentRef> scannedEnvs)
    {
        var globalsPath = Path.Combine(workspaceRoot, "environments", GlobalsFileName);

        _globalsRef = scannedEnvs.FirstOrDefault(e => IsGlobalsFile(e.FilePath))
            ?? new EnvironmentRef("Globals", globalsPath);

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
    }

    private void SyncEnvironmentsForPicker(IReadOnlyList<EnvironmentRef> nonGlobals)
    {
        EnvironmentsForPicker.Clear();
        EnvironmentsForPicker.Add(NoEnvironmentSentinel);
        foreach (var e in nonGlobals)
            EnvironmentsForPicker.Add(e);
    }

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
            EnvironmentsForPicker.Remove(pickerEntry);

        if (_selectedEnvironment?.FilePath == deleted.FilePath)
            SelectedEnvironment = NoEnvironmentSentinel;

        var openTab = Tabs.FirstOrDefault(t => t.FilePath == deleted.FilePath);
        if (openTab is not null)
            CloseTab(openTab);
    }

    private void RestoreSelectedEnvironment()
    {
        var nameToRestore = _pendingActiveEnvName ?? _selectedEnvironment?.Name;
        var isPendingRestore = _pendingActiveEnvName is not null;
        _pendingActiveEnvName = null;

        EnvironmentRef match;
        if (nameToRestore is null || nameToRestore == NoEnvironmentSentinel.Name)
        {
            match = NoEnvironmentSentinel;
        }
        else
        {
            match = EnvironmentsForPicker.FirstOrDefault(e => e.Name == nameToRestore)
                ?? NoEnvironmentSentinel;
        }

        if (isPendingRestore)
        {
            SelectedEnvironment = match;
        }
        else
        {
            _selectedEnvironment = match;
            this.RaisePropertyChanged(nameof(SelectedEnvironment));
        }
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
        if (IsGlobalsFile(env.FilePath)) return;

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
        _envScannedSub.Dispose();
        _envOpenSub.Dispose();
        _newEnvCreatedSub.Dispose();
        _activateEnvSub.Dispose();
        _envRenamedSub.Dispose();
        _envDeletedSub.Dispose();
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
