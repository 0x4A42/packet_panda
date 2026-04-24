namespace PacketPanda.Core.Environments;

public sealed class EnvironmentFileException : Exception
{
    public EnvironmentFileException(string message) : base(message) { }
    public EnvironmentFileException(string message, Exception inner) : base(message, inner) { }
}
