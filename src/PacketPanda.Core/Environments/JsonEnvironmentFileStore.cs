using System.Text;
using System.Text.Json;

namespace PacketPanda.Core.Environments;

public sealed class JsonEnvironmentFileStore : IEnvironmentFileStore
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public async Task<Environment> LoadAsync(string path)
    {
        string json;
        try { json = await File.ReadAllTextAsync(path); }
        catch (IOException ex) { throw new EnvironmentFileException($"Could not read file: {ex.Message}", ex); }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EnvironmentFileException($"Invalid JSON: {ex.Message}", ex); }

        using (doc)
        {
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var nameEl) && nameEl.GetString() is string n && n.Length > 0
                ? n
                : Path.GetFileNameWithoutExtension(path);

            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            var disabled = new List<string>();

            if (root.TryGetProperty("values", out var values))
            {
                foreach (var entry in values.EnumerateArray())
                {
                    var key = entry.TryGetProperty("key", out var kEl) ? kEl.GetString() : null;
                    if (key is null) continue;
                    var value = entry.TryGetProperty("value", out var vEl) ? (vEl.GetString() ?? string.Empty) : string.Empty;
                    var enabled = !entry.TryGetProperty("enabled", out var enEl) || enEl.GetBoolean();

                    vars[key] = value;
                    if (!enabled) disabled.Add(key);
                }
            }

            return new Environment(name, path, vars, Descriptions: null,
                DisabledVariables: disabled.Count > 0 ? disabled : null);
        }
    }

    public async Task SaveAsync(string path, Environment env)
    {
        var disabledSet = env.DisabledVariables is not null
            ? new HashSet<string>(env.DisabledVariables, StringComparer.Ordinal)
            : null;

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("id", Guid.NewGuid().ToString());
            writer.WriteString("name", env.Name);
            writer.WriteStartArray("values");

            foreach (var kvp in env.Variables)
            {
                var isEnabled = disabledSet?.Contains(kvp.Key) != true;
                writer.WriteStartObject();
                writer.WriteString("key", kvp.Key);
                writer.WriteString("value", kvp.Value);
                writer.WriteString("type", "default");
                writer.WriteBoolean("enabled", isEnabled);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        var json = Encoding.UTF8.GetString(ms.ToArray());
        try { await File.WriteAllTextAsync(path, json); }
        catch (IOException ex) { throw new EnvironmentFileException($"Could not write file: {ex.Message}", ex); }
    }
}
