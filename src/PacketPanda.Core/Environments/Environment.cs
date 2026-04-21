namespace PacketPanda.Core.Environments;

public sealed record Environment(
    string Name,
    string FilePath,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyDictionary<string, string>? Descriptions = null);

public sealed record EnvironmentRef(string Name, string FilePath);
