using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacketPanda.Core.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _jsonPath;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonSettingsStore(string path)
    {
        _jsonPath = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PacketPanda",
        "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_jsonPath))
        {
            var tomlPath = Path.ChangeExtension(_jsonPath, ".toml");
            if (File.Exists(tomlPath))
                await SaveAsync(new AppSettings());
            return new AppSettings();
        }

        string json;
        try { json = await File.ReadAllTextAsync(_jsonPath); }
        catch (IOException ex) { throw new SettingsException($"Could not read settings: {ex.Message}", ex); }

        try
        {
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, Options)
                ?? new SettingsDto();
            return FromDto(dto);
        }
        catch (JsonException ex)
        {
            throw new SettingsException($"Invalid settings JSON: {ex.Message}", ex);
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_jsonPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var dto = new SettingsDto
        {
            OpenCollectionPaths = settings.OpenCollectionPaths is not null and { Count: > 0 }
                ? settings.OpenCollectionPaths.ToList()
                : null,
            ActiveEnvironmentName = settings.ActiveEnvironmentName,
            KnownEnvironmentPaths = settings.KnownEnvironmentPaths is not null and { Count: > 0 }
                ? settings.KnownEnvironmentPaths.ToList()
                : null,
            IsDarkTheme = settings.IsDarkTheme,
            SslVerification = settings.SslVerification,
            FollowRedirects = settings.FollowRedirects,
            TimeoutSeconds = settings.TimeoutSeconds,
            HistoryLimit = settings.HistoryLimit
        };

        var json = JsonSerializer.Serialize(dto, Options);
        try { await File.WriteAllTextAsync(_jsonPath, json); }
        catch (IOException ex) { throw new SettingsException($"Could not write settings: {ex.Message}", ex); }
    }

    public static AppSettings LoadOrDefault()
    {
        try
        {
            if (!File.Exists(DefaultPath)) return new AppSettings();
            var json = File.ReadAllText(DefaultPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, Options) ?? new SettingsDto();
            return FromDto(dto);
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static AppSettings FromDto(SettingsDto dto) => new AppSettings(
        OpenCollectionPaths: dto.OpenCollectionPaths,
        ActiveEnvironmentName: dto.ActiveEnvironmentName,
        KnownEnvironmentPaths: dto.KnownEnvironmentPaths,
        IsDarkTheme: dto.IsDarkTheme,
        SslVerification: dto.SslVerification,
        FollowRedirects: dto.FollowRedirects,
        TimeoutSeconds: dto.TimeoutSeconds,
        HistoryLimit: dto.HistoryLimit);

    private sealed class SettingsDto
    {
        public List<string>? OpenCollectionPaths { get; set; }
        public string? ActiveEnvironmentName { get; set; }
        public List<string>? KnownEnvironmentPaths { get; set; }
        public bool IsDarkTheme { get; set; }
        public bool SslVerification { get; set; } = true;
        public bool FollowRedirects { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public int HistoryLimit { get; set; } = 200;
    }
}
