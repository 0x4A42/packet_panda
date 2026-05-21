using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacketPanda.Core.History;

public sealed class JsonHistoryStore : IHistoryStore
{
    private const int MaxEntries = 200;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _jsonPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonHistoryStore(string path) => _jsonPath = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PacketPanda",
        "history.json");

    public async Task<IReadOnlyList<HistoryEntry>> LoadAsync()
    {
        await _gate.WaitAsync();
        try { return ReadUnlocked(); }
        finally { _gate.Release(); }
    }

    public async Task AppendAsync(HistoryEntry entry)
    {
        await _gate.WaitAsync();
        try
        {
            var entries = new List<HistoryEntry>(ReadUnlocked());
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            WriteUnlocked(entries);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try { WriteUnlocked(new List<HistoryEntry>()); }
        finally { _gate.Release(); }
    }

    private List<HistoryEntry> ReadUnlocked()
    {
        if (!File.Exists(_jsonPath)) return new List<HistoryEntry>();
        try
        {
            var json = File.ReadAllText(_jsonPath);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, Options)
                ?? new List<HistoryEntry>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Best-effort: a corrupt or locked file reads as empty history.
            return new List<HistoryEntry>();
        }
    }

    private void WriteUnlocked(List<HistoryEntry> entries)
    {
        var dir = Path.GetDirectoryName(_jsonPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, Options);
        try { File.WriteAllText(_jsonPath, json); }
        catch (IOException ex)
        {
            throw new HistoryFileException($"Could not write history: {ex.Message}", ex);
        }
    }
}
