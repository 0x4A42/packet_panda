using System.Collections.ObjectModel;
using System.Reactive;
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

    private readonly Subject<string> _environmentOpenRequested = new();
    private readonly Subject<string> _activateRequested = new();

    private string? _activeEnvironmentName;

    public ObservableCollection<EnvironmentRef> Items { get; } = new();
    public IObservable<string> EnvironmentOpenRequested => _environmentOpenRequested;
    public IObservable<string> ActivateRequested => _activateRequested;

    public string? ActiveEnvironmentName
    {
        get => _activeEnvironmentName;
        set => this.RaiseAndSetIfChanged(ref _activeEnvironmentName, value);
    }

    public ReactiveCommand<Unit, Unit> NewEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> DeleteEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> OpenEnvironmentCommand { get; }
    public ReactiveCommand<EnvironmentRef, Unit> ActivateEnvironmentCommand { get; }

    public EnvironmentSidebarViewModel(
        IEnvironmentFileStore store,
        IDialogService dialog,
        Func<string?> getWorkspaceRoot)
    {
        _store = store;
        _dialog = dialog;
        _getWorkspaceRoot = getWorkspaceRoot;

        NewEnvironmentCommand = ReactiveCommand.CreateFromTask(NewEnvironmentAsync);
        DeleteEnvironmentCommand = ReactiveCommand.CreateFromTask<EnvironmentRef>(DeleteEnvironmentAsync);
        OpenEnvironmentCommand = ReactiveCommand.Create<EnvironmentRef>(env =>
            _environmentOpenRequested.OnNext(env.FilePath));
        ActivateEnvironmentCommand = ReactiveCommand.Create<EnvironmentRef>(env =>
            _activateRequested.OnNext(env.Name));
    }

    public void SetEnvironments(IReadOnlyList<EnvironmentRef> envs)
    {
        Items.Clear();
        foreach (var e in envs)
            Items.Add(e);
    }

    private async Task NewEnvironmentAsync()
    {
        var root = _getWorkspaceRoot();
        if (root is null) return;

        var name = await _dialog.PromptTextAsync("New Environment", "Environment name");
        if (string.IsNullOrWhiteSpace(name)) return;

        var safeName = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(safeName)) return;

        var envDir = Path.Combine(root, "environments");
        Directory.CreateDirectory(envDir);
        var filePath = Path.Combine(envDir, $"{safeName}.toml");

        var env = new Environment(name, filePath,
            new Dictionary<string, string>(StringComparer.Ordinal));
        await _store.SaveAsync(filePath, env);

        var envRef = new EnvironmentRef(name, filePath);
        if (!Items.Any(i => i.FilePath == filePath))
            Items.Add(envRef);

        _environmentOpenRequested.OnNext(filePath);
    }

    private async Task DeleteEnvironmentAsync(EnvironmentRef envRef)
    {
        var confirmed = await _dialog.ConfirmAsync(
            "Delete Environment",
            $"Delete '{envRef.Name}'? This cannot be undone.");
        if (!confirmed) return;

        try { File.Delete(envRef.FilePath); } catch { /* best-effort */ }

        var existing = Items.FirstOrDefault(i => i.FilePath == envRef.FilePath);
        if (existing is not null)
            Items.Remove(existing);
    }
}
