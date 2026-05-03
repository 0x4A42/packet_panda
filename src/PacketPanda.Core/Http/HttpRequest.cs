namespace PacketPanda.Core.Http;

public sealed record HttpRequest(
    HttpMethod Method,
    string Url,
    string? Body = null,
    IReadOnlyList<HttpHeader>? Headers = null,
    AuthConfig? Auth = null,
    IReadOnlyList<QueryParam>? QueryParams = null);
