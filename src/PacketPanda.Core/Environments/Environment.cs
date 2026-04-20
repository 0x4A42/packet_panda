namespace PacketPanda.Core.Environments;

public sealed record Environment(
    string Name,
    string FilePath,
    IReadOnlyDictionary<string, string> Variables);

public sealed record EnvironmentRef(string Name, string FilePath);
