using PacketPanda.Core.Http;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Core.Persistence;

public abstract record CollectionNode(string Id, string Name);

public sealed record FolderNode(string Id, string Name, IReadOnlyList<CollectionNode> Children)
    : CollectionNode(Id, Name);

public sealed record RequestNode(
    string Id,
    string Name,
    string CollectionFilePath,
    HttpMethod Method,
    string Url,
    BodyConfig Body,
    IReadOnlyList<HttpHeader> Headers,
    AuthConfig Auth,
    IReadOnlyList<QueryParam> QueryParams,
    string? PreRequestScript = null,
    string? PostResponseScript = null)
    : CollectionNode(Id, Name);
