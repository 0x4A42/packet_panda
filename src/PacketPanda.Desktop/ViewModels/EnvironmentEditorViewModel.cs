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
    private List<(string Name, string Value)> _savedSnapshot = new();

    public EnvironmentEditorViewModel(IEnvironmentFileStore store)
    {
        _store = store;
        Rows = new ObservableCollection<EnvironmentVariableRowViewModel>();
        Rows.CollectionChanged += OnCollectionChanged;

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        AddRowCommand = ReactiveCommand.Create(AddRow);
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
    public ReactiveCommand<Unit, Unit> AddRowCommand { get; }

    public void Load(Environment env)
    {
        // Unsubscribe from all existing rows
        foreach (var existing in Rows)
            existing.PropertyChanged -= OnRowPropertyChanged;

        Rows.Clear();

        foreach (var kvp in env.Variables)
        {
            var row = new EnvironmentVariableRowViewModel { Name = kvp.Key, Value = kvp.Value };
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        EnvironmentName = env.Name;
        CurrentFilePath = env.FilePath;
        CaptureSnapshot();
    }

    public void AddRow()
    {
        var row = new EnvironmentVariableRowViewModel();
        row.PropertyChanged += OnRowPropertyChanged;
        Rows.Add(row);
        RecomputeDirty();
    }

    public void RemoveRow(EnvironmentVariableRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        Rows.Remove(row);
        RecomputeDirty();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe to newly added rows (handles direct collection manipulation)
        if (e.NewItems is not null)
            foreach (EnvironmentVariableRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRowPropertyChanged;

        // Unsubscribe from removed rows
        if (e.OldItems is not null)
            foreach (EnvironmentVariableRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRowPropertyChanged;

        RecomputeDirty();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RecomputeDirty();

    private void RecomputeDirty()
    {
        var current = Rows.Select(r => (r.Name, r.Value)).ToList();
        IsDirty = !current.SequenceEqual(_savedSnapshot);
    }

    private void CaptureSnapshot()
    {
        _savedSnapshot = Rows.Select(r => (r.Name, r.Value)).ToList();
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

        // Validate: no blank names
        var blankRow = Rows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Name));
        if (blankRow is not null)
        {
            ErrorMessage = "Variable names cannot be blank.";
            return;
        }

        // Validate: no duplicate names
        var duplicates = Rows
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            ErrorMessage = $"Duplicate variable names: {string.Join(", ", duplicates)}";
            return;
        }

        var vars = Rows.ToDictionary(r => r.Name, r => r.Value, StringComparer.Ordinal);
        var env = new Environment(EnvironmentName, CurrentFilePath!, vars);
        await _store.SaveAsync(CurrentFilePath!, env);
        CaptureSnapshot();
    }
}
