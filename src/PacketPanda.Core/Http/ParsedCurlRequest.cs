namespace PacketPanda.Core.Http;

public sealed record ParsedCurlRequest(
    string Url,
    HttpMethod Method,
    IReadOnlyList<HttpHeader> Headers,
    BodyConfig Body);
