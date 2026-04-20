using System.Collections.ObjectModel;
using System.Reactive;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class ShellViewModel : ViewModelBase
{
    private readonly Func<RequestEditorViewModel> _editorFactory;
    private readonly IDialogService _dialog;
    private readonly IRequestFileStore _store;
    private readonly Dictionary<TabViewModel, IDisposable> _closeSubscriptions = new();

    private TabViewModel? _activeTab;

    public ShellViewModel(
        CollectionSidebarViewModel sidebar,
        Func<RequestEditorViewModel> editorFactory,
        IDialogService dialog,
        IRequestFileStore store)
    {
        Sidebar = sidebar;
        _editorFactory = editorFactory;
        _dialog = dialog;
        _store = store;

        Tabs = new ObservableCollection<TabViewModel>();

        NewTabCommand = ReactiveCommand.Create(AddBlankTab);
        OpenFileInTabCommand = ReactiveCommand.CreateFromTask<string>(OpenFileInTabAsync);

        Sidebar.RequestOpened.InvokeCommand(OpenFileInTabCommand);
    }

    public CollectionSidebarViewModel Sidebar { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set => this.RaiseAndSetIfChanged(ref _activeTab, value);
    }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<string, Unit> OpenFileInTabCommand { get; }

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
        }

        var tab = new TabViewModel(editor, _dialog);
        _closeSubscriptions[tab] = tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
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
