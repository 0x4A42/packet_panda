using System.Collections.ObjectModel;
using System.Reactive;
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
    private readonly IDisposable _activateEnvSub;

    private TabViewModel? _activeTab;
    private EnvironmentRef? _selectedEnvironment;

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

        NewTabCommand = ReactiveCommand.Create(AddBlankTab);
        OpenFileInTabCommand = ReactiveCommand.CreateFromTask<string>(OpenFileInTabAsync);
        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        CloseActiveTabCommand = ReactiveCommand.Create(
            () =>
            {
                if (ActiveTab is not null)
                    ActiveTab.CloseCommand.Execute().Subscribe();
            });

        var hasWorkspace = sidebar.WhenAnyValue(s => s.WorkspacePath)
            .Select(p => p is not null);

        EnvironmentSidebar = new EnvironmentSidebarViewModel(
            environmentStore,
            dialog,
            () => Sidebar.WorkspacePath,
            hasWorkspace);

        Sidebar.RequestOpened.InvokeCommand(OpenFileInTabCommand);

        _envScannedSub = sidebar.EnvironmentsScanned.Subscribe(envs =>
        {
            UpdateAvailableEnvironments(envs);
            EnvironmentSidebar.SetEnvironments(envs);
        });

        _envOpenSub = EnvironmentSidebar.EnvironmentOpenRequested
            .Subscribe(path => _ = OpenEnvironmentInTabAsync(path));

        _activateEnvSub = EnvironmentSidebar.ActivateRequested
            .Subscribe(name =>
            {
                SelectedEnvironment = AvailableEnvironments.FirstOrDefault(e => e.Name == name);
            });
    }

    public CollectionSidebarViewModel Sidebar { get; }
    public EnvironmentSidebarViewModel EnvironmentSidebar { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }
    public ObservableCollection<EnvironmentRef> AvailableEnvironments { get; } = new();
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

    public async Task OpenEnvironmentInTabAsync(string filePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var envEditor = new EnvironmentEditorViewModel(_environmentStore);
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
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private void UpdateAvailableEnvironments(IReadOnlyList<EnvironmentRef> envs)
    {
        AvailableEnvironments.Clear();
        foreach (var e in envs)
            AvailableEnvironments.Add(e);

        // Re-select previously active env by name if it still exists;
        // update the backing field directly to avoid reloading the file.
        var currentName = _selectedEnvironment?.Name;
        var match = AvailableEnvironments.FirstOrDefault(e => e.Name == currentName);
        _selectedEnvironment = match;
        this.RaisePropertyChanged(nameof(SelectedEnvironment));
    }

    private async Task OnSelectedEnvironmentChangedAsync(EnvironmentRef? envRef)
    {
        if (envRef is null)
        {
            _activeEnvSubject.OnNext(null);
            await PersistActiveEnvNameAsync(null);
            return;
        }

        try
        {
            var env = await _environmentStore.LoadAsync(envRef.FilePath);
            _activeEnvSubject.OnNext(env);
            await PersistActiveEnvNameAsync(envRef.Name);
        }
        catch (EnvironmentFileException)
        {
            _activeEnvSubject.OnNext(null);
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
        _activateEnvSub.Dispose();
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
