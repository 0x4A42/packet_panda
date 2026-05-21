using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Core.History;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class HistorySidebarViewModel : ReactiveObject, IDisposable
{
    private readonly IHistoryStore _store;
    private readonly Subject<HistoryEntry> _openRequested = new();

    public HistorySidebarViewModel(IHistoryStore store)
    {
        _store = store;
        Items = new ObservableCollection<HistoryItemViewModel>();

        OpenCommand = ReactiveCommand.Create<HistoryItemViewModel>(
            item => _openRequested.OnNext(item.Entry));
        ClearCommand = ReactiveCommand.CreateFromTask(ClearAsync);
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; }
    public ReactiveCommand<HistoryItemViewModel, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }
    public IObservable<HistoryEntry> HistoryOpenRequested => _openRequested;

    public async Task LoadAsync()
    {
        var entries = await _store.LoadAsync();
        Items.Clear();
        foreach (var entry in entries)
            Items.Add(new HistoryItemViewModel(entry));
    }

    public void AddEntry(HistoryEntry entry)
        => Items.Insert(0, new HistoryItemViewModel(entry));

    private async Task ClearAsync()
    {
        try { await _store.ClearAsync(); }
        catch (HistoryFileException) { /* best-effort */ }
        Items.Clear();
    }

    public void Dispose() => _openRequested.Dispose();
}
