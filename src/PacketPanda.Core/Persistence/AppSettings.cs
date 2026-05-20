namespace PacketPanda.Core.Persistence;

public sealed record AppSettings(
    IReadOnlyList<string>? OpenCollectionPaths = null,
    string? ActiveEnvironmentName = null,
    IReadOnlyList<string>? KnownEnvironmentPaths = null,
    bool IsDarkTheme = true);
