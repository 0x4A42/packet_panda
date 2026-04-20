using Tomlyn;
using Tomlyn.Model;

namespace PacketPanda.Core.Environments;

public sealed class TomlEnvironmentFileStore : IEnvironmentFileStore
{
    public async Task<Environment> LoadAsync(string path)
    {
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { throw new EnvironmentFileException($"Could not read file: {ex.Message}", ex); }

        TomlTable table;
        try { table = Toml.ToModel(text); }
        catch (Exception ex) { throw new EnvironmentFileException($"Invalid TOML: {ex.Message}", ex); }

        var name = table.TryGetValue("name", out var n) && n is string s && s.Length > 0
            ? s
            : Path.GetFileNameWithoutExtension(path);

        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (table.TryGetValue("variables", out var v) && v is TomlTable vt)
        {
            foreach (var kvp in vt)
            {
                if (kvp.Value is not string str)
                    throw new EnvironmentFileException($"Variable '{kvp.Key}' must be a string.");
                vars[kvp.Key] = str;
            }
        }

        return new Environment(name, path, vars);
    }

    public async Task SaveAsync(string path, Environment env)
    {
        var table = new TomlTable { ["name"] = env.Name };
        var vars = new TomlTable();
        foreach (var kvp in env.Variables)
            vars[kvp.Key] = kvp.Value;
        table["variables"] = vars;

        var toml = Toml.FromModel(table);
        try { await File.WriteAllTextAsync(path, toml); }
        catch (Exception ex) { throw new EnvironmentFileException($"Could not write file: {ex.Message}", ex); }
    }
}
