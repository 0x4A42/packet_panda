using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Core.Environments;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Desktop.ViewModels;

public class EnvironmentEditorViewModel : ViewModelBase, ITabContent
{
    private readonly IEnvironmentFileStore _store;
    private readonly IDialogService _dialog;
    private string _environmentName = string.Empty;
    private string _savedName = string.Empty;
    private string? _currentFilePath;
    private bool _isDirty;
    private string? _errorMessage;
    private bool _isNew;
    private List<(string Name, string Value, string Description, bool IsEnabled)> _savedSnapshot = new();
    private readonly Subject<Environment> _saved = new();

    public IObservable<Environment> Saved => _saved;

    public EnvironmentEditorViewModel(IEnvironmentFileStore store, IDialogService dialog)
    {
        _store = store;
        _dialog = dialog;
        Rows = new ObservableCollection<EnvironmentVariableRowViewModel>();
        Rows.CollectionChanged += OnCollectionChanged;
        Rows.Add(new EnvironmentVariableRowViewModel());

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
    }

    public string EnvironmentName
    {
        get => _environmentName;
        set
        {
            this.RaiseAndSetIfChanged(ref _environmentName, value);
            RecomputeDirty();
        }
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set => this.RaiseAndSetIfChanged(ref _currentFilePath, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ObservableCollection<EnvironmentVariableRowViewModel> Rows { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    public void Load(Environment env)
    {
        Rows.Clear();

        var disabledSet = env.DisabledVariables is not null
            ? new HashSet<string>(env.DisabledVariables, StringComparer.Ordinal)
            : null;

        foreach (var kvp in env.Variables)
        {
            var description = env.Descriptions?.GetValueOrDefault(kvp.Key) ?? string.Empty;
            Rows.Add(new EnvironmentVariableRowViewModel
            {
                Name = kvp.Key,
                Value = kvp.Value,
                Description = description,
                IsEnabled = disabledSet?.Contains(kvp.Key) != true
            });
        }

        EnsureTrailingEmptyRow();

        _environmentName = env.Name;
        this.RaisePropertyChanged(nameof(EnvironmentName));
        CurrentFilePath = env.FilePath;
        CaptureSnapshot();
    }

    /// <summary>Marks this editor as representing an unsaved new environment.
    /// Must be called after <see cref="Load"/> with an environment that has no variables.</summary>
    public void MarkAsNew()
    {
        _isNew = true;
        _savedName = EnvironmentName;
        _savedSnapshot = [];
        IsDirty = false;
    }

    public void RevertName()
    {
        _environmentName = _savedName;
        this.RaisePropertyChanged(nameof(EnvironmentName));
        RecomputeDirty();
    }

    private void EnsureTrailingEmptyRow()
    {
        var last = Rows.LastOrDefault();
        if (last is null
            || !string.IsNullOrEmpty(last.Name)
            || !string.IsNullOrEmpty(last.Value)
            || !string.IsNullOrEmpty(last.Description))
        {
            Rows.Add(new EnvironmentVariableRowViewModel());
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (EnvironmentVariableRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRowPropertyChanged;

        if (e.OldItems is not null)
            foreach (EnvironmentVariableRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRowPropertyChanged;

        RecomputeDirty();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        EnsureTrailingEmptyRow();
        RecomputeDirty();
    }

    private void RecomputeDirty()
    {
        var currentRows = Rows
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => (r.Name, r.Value, r.Description, r.IsEnabled))
            .ToList();
        IsDirty = EnvironmentName != _savedName || !currentRows.SequenceEqual(_savedSnapshot);
    }

    private void CaptureSnapshot()
    {
        _savedName = EnvironmentName;
        _savedSnapshot = Rows
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => (r.Name, r.Value, r.Description, r.IsEnabled))
            .ToList();
        IsDirty = false;
    }

    // ITabContent
    string ITabContent.DisplayTitle => string.IsNullOrEmpty(EnvironmentName)
        ? Path.GetFileNameWithoutExtension(CurrentFilePath ?? "Untitled")
        : EnvironmentName;

    string? ITabContent.MethodLabel => null;

    private async Task SaveAsync()
    {
        ErrorMessage = null;

        var nonEmpty = Rows.Where(r => !string.IsNullOrEmpty(r.Name)).ToList();

        var duplicates = nonEmpty
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            var msg = $"Duplicate variable names: {string.Join(", ", duplicates)}";
            ErrorMessage = msg;
            await _dialog.AlertAsync("Duplicate Variables", msg);
            return;
        }

        var vars = nonEmpty.ToDictionary(r => r.Name, r => r.Value, StringComparer.Ordinal);
        var descs = nonEmpty
            .Where(r => !string.IsNullOrEmpty(r.Description))
            .ToDictionary(r => r.Name, r => r.Description, StringComparer.Ordinal);
        var disabled = nonEmpty
            .Where(r => !r.IsEnabled)
            .Select(r => r.Name)
            .ToList();

        if (_isNew)
        {
            _isNew = false;
            var safeName = string.Concat(EnvironmentName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            var lastSep = CurrentFilePath!.LastIndexOfAny(new[] { '/', '\\' });
            var dirPrefix = lastSep >= 0 ? CurrentFilePath[..(lastSep + 1)] : string.Empty;
            CurrentFilePath = dirPrefix + safeName + ".toml";
        }

        var env = new Environment(EnvironmentName, CurrentFilePath!, vars, descs,
            disabled.Count > 0 ? disabled : null);
        await _store.SaveAsync(CurrentFilePath!, env);
        CaptureSnapshot();
        _saved.OnNext(env);
    }
}
