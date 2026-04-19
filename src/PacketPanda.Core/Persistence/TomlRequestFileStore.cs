using PacketPanda.Core.Http;
using Tomlyn;
using Tomlyn.Model;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Core.Persistence;

public sealed class TomlRequestFileStore : IRequestFileStore
{
    public async Task<HttpRequest> LoadAsync(string path)
    {
        string toml;
        try
        {
            toml = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            throw new RequestFileException($"Could not read file: {ex.Message}", ex);
        }

        TomlTable table;
        try
        {
            table = Toml.ToModel(toml);
        }
        catch (Exception ex)
        {
            throw new RequestFileException($"Invalid TOML: {ex.Message}", ex);
        }

        if (!table.TryGetValue("method", out var methodObj) || methodObj is not string methodStr)
            throw new RequestFileException("Missing required field 'method'.");

        if (!table.TryGetValue("url", out var urlObj) || urlObj is not string url)
            throw new RequestFileException("Missing required field 'url'.");

        if (!Enum.TryParse<HttpMethod>(methodStr, ignoreCase: true, out var method))
            throw new RequestFileException($"Unknown HTTP method '{methodStr}'. Expected GET, POST, PUT, or DELETE.");

        string? body = null;
        if (table.TryGetValue("body", out var bodyObj) && bodyObj is string bodyStr && bodyStr.Length > 0)
            body = bodyStr;

        return new HttpRequest(method, url, body);
    }

    public async Task SaveAsync(string path, HttpRequest request)
    {
        var table = new TomlTable
        {
            ["method"] = request.Method.ToString().ToUpperInvariant(),
            ["url"] = request.Url
        };

        if (!string.IsNullOrEmpty(request.Body))
            table["body"] = request.Body;

        var toml = Toml.FromModel(table);
        try
        {
            await File.WriteAllTextAsync(path, toml);
        }
        catch (Exception ex)
        {
            throw new RequestFileException($"Could not write file: {ex.Message}", ex);
        }
    }
}
