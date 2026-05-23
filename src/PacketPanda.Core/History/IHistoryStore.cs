namespace PacketPanda.Core.History;

public interface IHistoryStore
{
    /// <summary>Returns all entries, newest first.</summary>
    Task<IReadOnlyList<HistoryEntry>> LoadAsync();

    /// <summary>Prepends an entry and prunes to the newest 200, then writes the file.</summary>
    Task AppendAsync(HistoryEntry entry);

    /// <summary>Removes all entries.</summary>
    Task ClearAsync();
}
