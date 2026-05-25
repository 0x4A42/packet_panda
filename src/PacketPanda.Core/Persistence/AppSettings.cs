namespace PacketPanda.Core.Persistence;

public sealed record AppSettings(
    IReadOnlyList<string>? OpenCollectionPaths = null,
    string? ActiveEnvironmentName = null,
    IReadOnlyList<string>? KnownEnvironmentPaths = null,
    bool IsDarkTheme = false,
    bool SslVerification = true,
    bool FollowRedirects = true,
    int TimeoutSeconds = 30,
    int HistoryLimit = 200);
