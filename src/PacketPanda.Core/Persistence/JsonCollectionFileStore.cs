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

    public async Task UpdateRequestAsync(string collectionPath, string requestId, HttpRequest updated,
        string? preRequestScript = null, string? postResponseScript = null)
    {
        var collection = await LoadAsync(collectionPath);
        var newRoot = UpdateInTree(collection.Root, requestId, updated, preRequestScript, postResponseScript);
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
                var queryParams = new List<QueryParam>();
                if (requestEl.TryGetProperty("url", out var urlEl))
                {
                    if (urlEl.ValueKind == JsonValueKind.Object)
                    {
                        if (urlEl.TryGetProperty("raw", out var rawEl))
                            url = rawEl.GetString() ?? string.Empty;

                        if (urlEl.TryGetProperty("query", out var queryArr))
                        {
                            foreach (var q in queryArr.EnumerateArray())
                            {
                                var key = q.TryGetProperty("key", out var kEl) ? kEl.GetString() ?? string.Empty : string.Empty;
                                var val = q.TryGetProperty("value", out var vEl) ? vEl.GetString() ?? string.Empty : string.Empty;
                                var disabled = q.TryGetProperty("disabled", out var dEl) && dEl.GetBoolean();
                                queryParams.Add(new QueryParam(key, val, !disabled));
                            }
                        }
                    }
                    else if (urlEl.ValueKind == JsonValueKind.String)
                    {
                        url = urlEl.GetString() ?? string.Empty;
                    }
                }

                BodyConfig body = new NoBody();
                if (requestEl.TryGetProperty("body", out var bodyEl) &&
                    bodyEl.ValueKind != JsonValueKind.Null)
                {
                    var mode = bodyEl.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null;
                    var raw = bodyEl.TryGetProperty("raw", out var rawEl) ? rawEl.GetString() : null;

                    if (mode == "urlencoded")
                    {
                        var fields = new List<FormField>();
                        if (bodyEl.TryGetProperty("urlencoded", out var ueArr))
                        {
                            foreach (var f in ueArr.EnumerateArray())
                            {
                                var fKey = f.TryGetProperty("key", out var fkEl) ? fkEl.GetString() ?? string.Empty : string.Empty;
                                var fVal = f.TryGetProperty("value", out var fvEl) ? fvEl.GetString() ?? string.Empty : string.Empty;
                                var fDis = f.TryGetProperty("disabled", out var fdEl) && fdEl.GetBoolean();
                                fields.Add(new FormField(fKey, fVal, !fDis));
                            }
                        }
                        body = new FormBody(fields);
                    }
                    else if (mode == "raw" && !string.IsNullOrEmpty(raw))
                    {
                        var language = string.Empty;
                        if (bodyEl.TryGetProperty("options", out var optEl) &&
                            optEl.TryGetProperty("raw", out var rawOptEl) &&
                            rawOptEl.TryGetProperty("language", out var langEl))
                            language = langEl.GetString() ?? string.Empty;

                        body = language switch
                        {
                            "json" => new JsonBody(raw),
                            "xml"  => new XmlBody(raw),
                            _      => new PlainTextBody(raw)
                        };
                    }
                    // else: mode == "raw" with empty/null raw → NoBody (default)
                }

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

                // Parse scripts from event array
                string? preRequestScript = null;
                string? postResponseScript = null;
                if (item.TryGetProperty("event", out var eventArr))
                {
                    foreach (var ev in eventArr.EnumerateArray())
                    {
                        var listen = ev.TryGetProperty("listen", out var listenEl) ? listenEl.GetString() : null;
                        if (listen is null) continue;
                        if (!ev.TryGetProperty("script", out var scriptEl)) continue;
                        if (!scriptEl.TryGetProperty("exec", out var execEl)) continue;
                        var lines = execEl.EnumerateArray()
                            .Select(l => l.GetString() ?? string.Empty)
                            .ToList();
                        var text = string.Join("\n", lines);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (listen == "prerequest") preRequestScript = text;
                        else if (listen == "test") postResponseScript = text;
                    }
                }

                result.Add(new RequestNode(id, name, collectionPath, method, url, body, headers, auth, queryParams,
                    preRequestScript, postResponseScript));
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
                if (req.QueryParams.Count > 0)
                {
                    writer.WriteStartArray("query");
                    foreach (var q in req.QueryParams)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("key", q.Key);
                        writer.WriteString("value", q.Value);
                        if (!q.Enabled) writer.WriteBoolean("disabled", true);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();

                // Body
                switch (req.Body)
                {
                    case JsonBody json:
                        writer.WriteStartObject("body");
                        writer.WriteString("mode", "raw");
                        writer.WriteString("raw", json.Text);
                        writer.WriteStartObject("options");
                        writer.WriteStartObject("raw");
                        writer.WriteString("language", "json");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        break;
                    case PlainTextBody text:
                        writer.WriteStartObject("body");
                        writer.WriteString("mode", "raw");
                        writer.WriteString("raw", text.Text);
                        writer.WriteStartObject("options");
                        writer.WriteStartObject("raw");
                        writer.WriteString("language", "text");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        break;
                    case XmlBody xml:
                        writer.WriteStartObject("body");
                        writer.WriteString("mode", "raw");
                        writer.WriteString("raw", xml.Text);
                        writer.WriteStartObject("options");
                        writer.WriteStartObject("raw");
                        writer.WriteString("language", "xml");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        break;
                    case FormBody form:
                        writer.WriteStartObject("body");
                        writer.WriteString("mode", "urlencoded");
                        writer.WriteStartArray("urlencoded");
                        foreach (var f in form.Fields)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("key", f.Name);
                            writer.WriteString("value", f.Value);
                            if (!f.Enabled) writer.WriteBoolean("disabled", true);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                        break;
                    default: // NoBody
                        writer.WriteNull("body");
                        break;
                }

                // Auth
                if (req.Auth is not NoAuth)
                    WriteAuth(writer, req.Auth);

                writer.WriteEndObject();

                // Scripts (event array)
                if (!string.IsNullOrEmpty(req.PreRequestScript) || !string.IsNullOrEmpty(req.PostResponseScript))
                {
                    writer.WriteStartArray("event");
                    if (!string.IsNullOrEmpty(req.PreRequestScript))
                        WriteScriptEvent(writer, "prerequest", req.PreRequestScript);
                    if (!string.IsNullOrEmpty(req.PostResponseScript))
                        WriteScriptEvent(writer, "test", req.PostResponseScript);
                    writer.WriteEndArray();
                }

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

    private static void WriteScriptEvent(Utf8JsonWriter writer, string listen, string script)
    {
        writer.WriteStartObject();
        writer.WriteString("listen", listen);
        writer.WriteStartObject("script");
        writer.WriteString("type", "text/javascript");
        writer.WriteStartArray("exec");
        foreach (var line in script.Replace("\r\n", "\n").Split('\n'))
            writer.WriteStringValue(line);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static FolderNode UpdateInTree(FolderNode folder, string requestId, HttpRequest updated,
        string? preRequestScript, string? postResponseScript)
    {
        var newChildren = folder.Children.Select(child => child switch
        {
            RequestNode req when req.Id == requestId =>
                req with
                {
                    Method = updated.Method,
                    Url = updated.Url,
                    Body = updated.Body ?? req.Body,
                    Headers = updated.Headers ?? req.Headers,
                    Auth = updated.Auth ?? req.Auth,
                    QueryParams = updated.QueryParams ?? req.QueryParams,
                    PreRequestScript = preRequestScript ?? req.PreRequestScript,
                    PostResponseScript = postResponseScript ?? req.PostResponseScript
                },
            RequestNode => child,
            FolderNode f => UpdateInTree(f, requestId, updated, preRequestScript, postResponseScript),
            _ => child
        }).ToList();
        return folder with { Children = newChildren };
    }
}
