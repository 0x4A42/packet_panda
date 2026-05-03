namespace PacketPanda.Core.Scripting;

public sealed record TestResult(string Name, bool Passed, string? ErrorMessage = null);
