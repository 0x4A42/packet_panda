namespace PacketPanda.Core.Persistence;

public sealed class RequestFileException : Exception
{
    public RequestFileException(string message) : base(message) { }
    public RequestFileException(string message, Exception inner) : base(message, inner) { }
}
