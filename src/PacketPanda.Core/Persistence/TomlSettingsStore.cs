using Tomlyn;
using Tomlyn.Model;

namespace PacketPanda.Core.Persistence;

public sealed class TomlSettingsStore : ISettingsStore
{
    private readonly string _path;

    public TomlSettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PacketPanda",
        "settings.toml");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        string toml;
        try
        {
            toml = await File.ReadAllTextAsync(_path);
        }
        catch (Exception ex)
        {
            throw new SettingsException($"Could not read settings: {ex.Message}", ex);
        }

        TomlTable table;
        try
        {
            table = Toml.ToModel(toml);
        }
        catch (Exception ex)
        {
            throw new SettingsException($"Invalid settings TOML: {ex.Message}", ex);
        }

        string? workspace = null;
        if (table.TryGetValue("last_workspace_path", out var v) && v is string s)
            workspace = s;

        return new AppSettings(workspace);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var table = new TomlTable();
        if (!string.IsNullOrEmpty(settings.LastWorkspacePath))
            table["last_workspace_path"] = settings.LastWorkspacePath;

        var toml = Toml.FromModel(table);
        try
        {
            await File.WriteAllTextAsync(_path, toml);
        }
        catch (Exception ex)
        {
            throw new SettingsException($"Could not write settings: {ex.Message}", ex);
        }
    }
}
