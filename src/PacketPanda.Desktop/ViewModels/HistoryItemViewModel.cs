using PacketPanda.Core.History;

namespace PacketPanda.Desktop.ViewModels;

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry entry)
    {
        Entry = entry;
        Method = entry.Method.ToString().ToUpperInvariant();
        Url = entry.Url;
        StatusCode = entry.StatusCode;
        StatusText = entry.ErrorMessage is not null
            ? "ERR"
            : entry.StatusCode?.ToString() ?? "—";
        TimeText = FormatRelative(entry.Timestamp);
    }

    public HistoryEntry Entry { get; }
    public string Method { get; }
    public string Url { get; }
    public int? StatusCode { get; }
    public string StatusText { get; }
    public string TimeText { get; }

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta < TimeSpan.FromMinutes(1)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m ago";
        if (delta < TimeSpan.FromDays(1)) return $"{(int)delta.TotalHours}h ago";
        return when.LocalDateTime.ToString("MMM d");
    }
}
