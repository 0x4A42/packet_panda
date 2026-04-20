namespace PacketPanda.Core.Persistence;

public abstract record CollectionNode(string Name, string Path);

public sealed record FolderNode(string Name, string Path, IReadOnlyList<CollectionNode> Children)
    : CollectionNode(Name, Path);

public sealed record RequestNode(string Name, string Path, string? Method = null)
    : CollectionNode(Name, Path);
