using System.Text;
using System.Text.Json;
using PacketPanda.Core.Http;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Core.Persistence;

public sealed class JsonCollectionFileStore : ICollectionFileStore
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public async Task<Collection> LoadAsync(string path)
    {
        string json;
        try { json = await File.ReadAllTextAsync(path); }
        catch (IOException ex) { throw new RequestFileException($"Could not read file: {ex.Message}", ex); }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new RequestFileException($"Invalid JSON: {ex.Message}", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("info", out var info) ||
                !info.TryGetProperty("name", out var nameEl) ||
                nameEl.GetString() is not string name)
                throw new RequestFileException("Missing required field 'info.name'.");

            var children = root.TryGetProperty("item", out var items)
                ? ParseItems(items, path)
                : (IReadOnlyList<CollectionNode>)Array.Empty<CollectionNode>();

            return new Collection(name, path, new FolderNode(Guid.NewGuid().ToString(), name, children));
        }
    }

    public async Task SaveAsync(string path, Collection collection)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("info");
            writer.WriteString("name", collection.Name);
            writer.WriteString("schema", "https://schema.getpostman.com/json/collection/v2.1.0/collection.json");
            writer.WriteEndObject();

            writer.WriteStartArray("item");
            WriteItems(writer, collection.Root.Children);
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
        }

        var json = Encoding.UTF8.GetString(ms.ToArray());
        try { await File.WriteAllTextAsync(path, json); }
        catch (Exception ex) { throw new RequestFileException($"Could not write file: {ex.Message}", ex); }
    }

    public async Task UpdateRequestAsync(string collectionPath, string requestId, HttpRequest updated)
    {
        var collection = await LoadAsync(collectionPath);
        var newRoot = UpdateInTree(collection.Root, requestId, updated);
        await SaveAsync(collectionPath, collection with { Root = newRoot });
    }

    private static IReadOnlyList<CollectionNode> ParseItems(JsonElement items, string collectionPath)
    {
        var result = new List<CollectionNode>();
        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl)
                ? (idEl.GetString() ?? Guid.NewGuid().ToString())
                : Guid.NewGuid().ToString();

            var name = item.TryGetProperty("name", out var nameEl)
                ? (nameEl.GetString() ?? "Unnamed")
                : "Unnamed";

            if (item.TryGetProperty("request", out var requestEl))
            {
                var methodStr = requestEl.TryGetProperty("method", out var mEl)
                    ? (mEl.GetString() ?? "GET")
                    : "GET";
                if (!Enum.TryParse<HttpMethod>(methodStr, ignoreCase: true, out var method))
                    method = HttpMethod.Get;

                var url = string.Empty;
                if (requestEl.TryGetProperty("url", out var urlEl))
                {
                    if (urlEl.ValueKind == JsonValueKind.Object &&
                        urlEl.TryGetProperty("raw", out var rawEl))
                        url = rawEl.GetString() ?? string.Empty;
                    else if (urlEl.ValueKind == JsonValueKind.String)
                        url = urlEl.GetString() ?? string.Empty;
                }

                string? body = null;
                if (requestEl.TryGetProperty("body", out var bodyEl) &&
                    bodyEl.ValueKind != JsonValueKind.Null &&
                    bodyEl.TryGetProperty("mode", out var modeEl) &&
                    modeEl.GetString() == "raw" &&
                    bodyEl.TryGetProperty("raw", out var rawBody))
                    body = rawBody.GetString();

                // Parse headers
                var headers = new List<HttpHeader>();
                if (requestEl.TryGetProperty("header", out var headerArr))
                {
                    foreach (var h in headerArr.EnumerateArray())
                    {
                        var key = h.TryGetProperty("key", out var kEl) ? kEl.GetString() ?? string.Empty : string.Empty;
                        var val = h.TryGetProperty("value", out var vEl) ? vEl.GetString() ?? string.Empty : string.Empty;
                        var disabled = h.TryGetProperty("disabled", out var dEl) && dEl.GetBoolean();
                        headers.Add(new HttpHeader(key, val, !disabled));
                    }
                }

                // Parse auth
                AuthConfig auth = new NoAuth();
                if (requestEl.TryGetProperty("auth", out var authEl) &&
                    authEl.TryGetProperty("type", out var typeEl))
                {
                    auth = typeEl.GetString() switch
                    {
                        "bearer" => ParseBearerAuth(authEl),
                        "apikey" => ParseApiKeyAuth(authEl),
                        "basic" => ParseBasicAuth(authEl),
                        _ => new NoAuth()
                    };
                }

                result.Add(new RequestNode(id, name, collectionPath, method, url, body, headers, auth));
            }
            else if (item.TryGetProperty("item", out var subItems))
            {
                result.Add(new FolderNode(id, name, ParseItems(subItems, collectionPath)));
            }
        }
        return result;
    }

    private static void WriteItems(Utf8JsonWriter writer, IReadOnlyList<CollectionNode> items)
    {
        foreach (var item in items)
        {
            writer.WriteStartObject();
            writer.WriteString("id", item.Id);
            writer.WriteString("name", item.Name);

            if (item is RequestNode req)
            {
                writer.WriteStartObject("request");
                writer.WriteString("method", req.Method.ToString().ToUpperInvariant());

                // Headers
                writer.WriteStartArray("header");
                foreach (var h in req.Headers)
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", h.Name);
                    writer.WriteString("value", h.Value);
                    if (!h.Enabled) writer.WriteBoolean("disabled", true);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                // URL
                writer.WriteStartObject("url");
                writer.WriteString("raw", req.Url);
                writer.WriteEndObject();

                // Body
                if (req.Body is not null)
                {
                    writer.WriteStartObject("body");
                    writer.WriteString("mode", "raw");
                    writer.WriteString("raw", req.Body);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNull("body");
                }

                // Auth
                if (req.Auth is not NoAuth)
                    WriteAuth(writer, req.Auth);

                writer.WriteEndObject();
                writer.WriteStartArray("response");
                writer.WriteEndArray();
            }
            else if (item is FolderNode folder)
            {
                writer.WriteStartArray("item");
                WriteItems(writer, folder.Children);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }

    private static BearerAuth ParseBearerAuth(JsonElement authEl)
    {
        var token = string.Empty;
        if (authEl.TryGetProperty("bearer", out var arr))
            foreach (var p in arr.EnumerateArray())
                if (p.TryGetProperty("key", out var k) && k.GetString() == "token" &&
                    p.TryGetProperty("value", out var v))
                    token = v.GetString() ?? string.Empty;
        return new BearerAuth(token);
    }

    private static ApiKeyAuth ParseApiKeyAuth(JsonElement authEl)
    {
        var name = string.Empty;
        var value = string.Empty;
        var target = ApiKeyTarget.Header;
        if (authEl.TryGetProperty("apikey", out var arr))
            foreach (var p in arr.EnumerateArray())
            {
                if (!p.TryGetProperty("key", out var k) || !p.TryGetProperty("value", out var v)) continue;
                switch (k.GetString())
                {
                    case "key": name = v.GetString() ?? string.Empty; break;
                    case "value": value = v.GetString() ?? string.Empty; break;
                    case "in": target = v.GetString() == "query_param" ? ApiKeyTarget.QueryParam : ApiKeyTarget.Header; break;
                }
            }
        return new ApiKeyAuth(name, value, target);
    }

    private static BasicAuth ParseBasicAuth(JsonElement authEl)
    {
        var username = string.Empty;
        var password = string.Empty;
        if (authEl.TryGetProperty("basic", out var arr))
            foreach (var p in arr.EnumerateArray())
            {
                if (!p.TryGetProperty("key", out var k) || !p.TryGetProperty("value", out var v)) continue;
                switch (k.GetString())
                {
                    case "username": username = v.GetString() ?? string.Empty; break;
                    case "password": password = v.GetString() ?? string.Empty; break;
                }
            }
        return new BasicAuth(username, password);
    }

    private static void WriteAuth(Utf8JsonWriter writer, AuthConfig auth)
    {
        writer.WriteStartObject("auth");
        switch (auth)
        {
            case BearerAuth bearer:
                writer.WriteString("type", "bearer");
                writer.WriteStartArray("bearer");
                WriteKV(writer, "token", bearer.Token);
                writer.WriteEndArray();
                break;
            case ApiKeyAuth apiKey:
                writer.WriteString("type", "apikey");
                writer.WriteStartArray("apikey");
                WriteKV(writer, "key", apiKey.Name);
                WriteKV(writer, "value", apiKey.Value);
                WriteKV(writer, "in", apiKey.Target == ApiKeyTarget.QueryParam ? "query_param" : "header");
                writer.WriteEndArray();
                break;
            case BasicAuth basic:
                writer.WriteString("type", "basic");
                writer.WriteStartArray("basic");
                WriteKV(writer, "username", basic.Username);
                WriteKV(writer, "password", basic.Password);
                writer.WriteEndArray();
                break;
            default:
                writer.WriteString("type", "noauth");
                break;
        }
        writer.WriteEndObject();
    }

    private static void WriteKV(Utf8JsonWriter writer, string key, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteString("value", value);
        writer.WriteString("type", "string");
        writer.WriteEndObject();
    }

    private static FolderNode UpdateInTree(FolderNode folder, string requestId, HttpRequest updated)
    {
        var newChildren = folder.Children.Select(child => child switch
        {
            RequestNode req when req.Id == requestId =>
                req with
                {
                    Method = updated.Method,
                    Url = updated.Url,
                    Body = updated.Body,
                    Headers = updated.Headers ?? req.Headers,
                    Auth = updated.Auth ?? req.Auth
                },
            RequestNode => child,
            FolderNode f => UpdateInTree(f, requestId, updated),
            _ => child
        }).ToList();
        return folder with { Children = newChildren };
    }
}
