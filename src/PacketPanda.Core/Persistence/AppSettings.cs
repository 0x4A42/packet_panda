namespace PacketPanda.Core.Persistence;

public sealed record AppSettings(
    string? LastWorkspacePath = null,
    string? ActiveEnvironmentName = null);
