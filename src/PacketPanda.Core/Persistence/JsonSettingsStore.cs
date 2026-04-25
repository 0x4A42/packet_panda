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
            return new AppSettings(
                OpenCollectionPaths: dto.OpenCollectionPaths,
                ActiveEnvironmentName: dto.ActiveEnvironmentName);
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
            OpenCollectionPaths = settings.OpenCollectionPaths is { Count: > 0 }
                ? settings.OpenCollectionPaths.ToList()
                : null,
            ActiveEnvironmentName = settings.ActiveEnvironmentName
        };

        var json = JsonSerializer.Serialize(dto, Options);
        try { await File.WriteAllTextAsync(_jsonPath, json); }
        catch (IOException ex) { throw new SettingsException($"Could not write settings: {ex.Message}", ex); }
    }

    private sealed class SettingsDto
    {
        public List<string>? OpenCollectionPaths { get; set; }
        public string? ActiveEnvironmentName { get; set; }
    }
}
