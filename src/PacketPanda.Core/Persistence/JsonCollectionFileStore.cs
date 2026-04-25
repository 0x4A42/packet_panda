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

                result.Add(new RequestNode(id, name, collectionPath, method, url, body));
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
                writer.WriteStartArray("header"); // TODO: custom headers not yet persisted
                writer.WriteEndArray();
                writer.WriteStartObject("url");
                writer.WriteString("raw", req.Url);
                writer.WriteEndObject();
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

    private static FolderNode UpdateInTree(FolderNode folder, string requestId, HttpRequest updated)
    {
        var newChildren = folder.Children.Select(child => child switch
        {
            RequestNode req when req.Id == requestId =>
                req with { Method = updated.Method, Url = updated.Url, Body = updated.Body },
            RequestNode => child,
            FolderNode f => UpdateInTree(f, requestId, updated),
            _ => child
        }).ToList();
        return folder with { Children = newChildren };
    }
}
