namespace PacketPanda.Core.History;

public sealed class HistoryFileException : Exception
{
    public HistoryFileException(string message) : base(message) { }
    public HistoryFileException(string message, Exception inner) : base(message, inner) { }
}
