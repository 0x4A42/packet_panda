using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Desktop.ViewModels;

public record EnvironmentContext(
    Environment? Merged,
    IReadOnlyDictionary<string, string>? GlobalsVars,
    IReadOnlyDictionary<string, string>? ActiveEnvVars);
