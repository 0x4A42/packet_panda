namespace PacketPanda.Core.Scripting;

public sealed class ScriptException : Exception
{
    public ScriptException(string message) : base(message) { }
    public ScriptException(string message, Exception inner) : base(message, inner) { }
}
