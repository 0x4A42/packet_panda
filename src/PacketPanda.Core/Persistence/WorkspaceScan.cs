using PacketPanda.Core.Environments;

namespace PacketPanda.Core.Persistence;

public sealed record WorkspaceScan(
    FolderNode Collections,
    IReadOnlyList<EnvironmentRef> Environments);
