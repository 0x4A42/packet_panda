using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using PacketPanda.Core.Environments;
using ReactiveUI;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Desktop.ViewModels;

public class EnvironmentEditorViewModel : ViewModelBase, ITabContent
{
    private readonly IEnvironmentFileStore _store;
    private string _environmentName = string.Empty;
    private string? _currentFilePath;
    private bool _isDirty;
    private string? _errorMessage;
    private List<(string Name, string Value, string Description)> _savedSnapshot = new();

    public EnvironmentEditorViewModel(IEnvironmentFileStore store)
    {
        _store = store;
        Rows = new ObservableCollection<EnvironmentVariableRowViewModel>();
        Rows.CollectionChanged += OnCollectionChanged;
        Rows.Add(new EnvironmentVariableRowViewModel()); // start with one empty trailing row

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
    }

    public string EnvironmentName
    {
        get => _environmentName;
        private set => this.RaiseAndSetIfChanged(ref _environmentName, value);
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
        Rows.Clear(); // CollectionChanged handles unsubscribing old rows

        foreach (var kvp in env.Variables)
        {
            var description = env.Descriptions?.GetValueOrDefault(kvp.Key) ?? string.Empty;
            Rows.Add(new EnvironmentVariableRowViewModel
            {
                Name = kvp.Key,
                Value = kvp.Value,
                Description = description
            });
        }

        EnsureTrailingEmptyRow();

        EnvironmentName = env.Name;
        CurrentFilePath = env.FilePath;
        CaptureSnapshot();
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
        var current = Rows
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => (r.Name, r.Value, r.Description))
            .ToList();
        IsDirty = !current.SequenceEqual(_savedSnapshot);
    }

    private void CaptureSnapshot()
    {
        _savedSnapshot = Rows
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => (r.Name, r.Value, r.Description))
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
            ErrorMessage = $"Duplicate variable names: {string.Join(", ", duplicates)}";
            return;
        }

        var vars = nonEmpty.ToDictionary(r => r.Name, r => r.Value, StringComparer.Ordinal);
        var descs = nonEmpty
            .Where(r => !string.IsNullOrEmpty(r.Description))
            .ToDictionary(r => r.Name, r => r.Description, StringComparer.Ordinal);

        var env = new Environment(EnvironmentName, CurrentFilePath!, vars, descs);
        await _store.SaveAsync(CurrentFilePath!, env);
        CaptureSnapshot();
    }
}
