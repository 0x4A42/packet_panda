namespace PacketPanda.Core.Environments;

public sealed record ResolveResult(string Text, IReadOnlyList<string> UnresolvedNames);
