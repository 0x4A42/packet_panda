using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionSidebarViewModel : ReactiveObject
{
    private readonly IWorkspaceScanner _scanner;
    private readonly ISettingsStore _settings;
    private readonly Subject<string> _requestOpened = new();

    private string? _workspacePath;
    private string? _errorMessage;

    public CollectionSidebarViewModel(
        IWorkspaceScanner scanner,
        ISettingsStore settings)
    {
        _scanner = scanner;
        _settings = settings;

        RootNodes = new ObservableCollection<CollectionNodeViewModel>();
        OpenRequestCommand = ReactiveCommand.CreateFromTask<CollectionNodeViewModel>(OpenRequestAsync);
    }

    public ObservableCollection<CollectionNodeViewModel> RootNodes { get; }

    public string? WorkspacePath
    {
        get => _workspacePath;
        private set => this.RaiseAndSetIfChanged(ref _workspacePath, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<CollectionNodeViewModel, Unit> OpenRequestCommand { get; }

    public IObservable<string> RequestOpened => _requestOpened;

    public async Task LoadWorkspaceAsync(string path)
    {
        ErrorMessage = null;
        try
        {
            var tree = await _scanner.ScanAsync(path);
            ReplaceRoots(tree);
            WorkspacePath = path;
        }
        catch (Exception ex)
        {
            RootNodes.Clear();
            WorkspacePath = null;
            ErrorMessage = $"Could not open workspace: {ex.Message}";
            return;
        }

        try { await _settings.SaveAsync(new AppSettings(path)); }
        catch (SettingsException) { }
    }

    private Task OpenRequestAsync(CollectionNodeViewModel node)
    {
        if (!node.IsFolder)
            _requestOpened.OnNext(node.Path);

        return Task.CompletedTask;
    }

    private void ReplaceRoots(FolderNode tree)
    {
        RootNodes.Clear();
        foreach (var child in tree.Children)
            RootNodes.Add(CollectionNodeViewModel.From(child));
    }
}
